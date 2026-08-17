using System.Reflection;
using System.Text.Json;
using Fts.Application.Balance;
using Fts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sim.Core.Config;

namespace Fts.Infrastructure.Balance;

/// <summary>
/// Reads/validates balance revisions (Phase 10.3). The serialisation settings live here so the push
/// endpoint, the loader and the dashboard all agree on one shape.
/// </summary>
public static class BalanceStore
{
    /// <summary>Indented on the way out (the dashboard shows the raw document and an operator edits it by
    /// hand) and case-insensitive on the way in (a hand-edited or tool-generated file should not fail on
    /// casing). No enum converter: <c>BalanceConfig</c> is numbers and booleans all the way down.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>The top-level section names of <c>BalanceConfig</c> ("Match", "Market", …), used to reject
    /// a partial push. Computed once by reflection so adding a section to Sim.Core cannot leave this list
    /// silently stale.</summary>
    public static readonly IReadOnlyList<string> SectionNames = typeof(BalanceConfig)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanWrite && !p.PropertyType.IsPrimitive && p.PropertyType != typeof(string))
        .Select(p => p.Name)
        .ToArray();

    public static string Serialize(BalanceConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>
    /// Parses a pushed document into a config, or explains why it cannot.
    ///
    /// The partial-document check is the important half. <c>JsonSerializer</c> is happy to deserialise
    /// <c>{"Match":{...}}</c> into a whole <c>BalanceConfig</c> — every section the document omits comes
    /// back as its constructor default. For a balance push that is the worst possible failure mode: an
    /// operator pastes the one section they meant to tune and silently resets fourteen others. So a push
    /// must carry EVERY top-level section; the dashboard hands the operator the full active document to
    /// edit, which makes that the natural thing to do anyway.
    /// </summary>
    public static bool TryParse(string json, out BalanceConfig config, out string error)
    {
        config = new BalanceConfig();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The balance document is empty.";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"The balance document is not valid JSON: {ex.Message}";
            return false;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "The balance document must be a JSON object.";
                return false;
            }

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in doc.RootElement.EnumerateObject()) present.Add(property.Name);

            var missing = SectionNames.Where(s => !present.Contains(s)).ToList();
            if (missing.Count > 0)
            {
                error = "The balance document is incomplete — a push must carry the whole config, not a "
                      + $"fragment. Missing: {string.Join(", ", missing)}.";
                return false;
            }
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<BalanceConfig>(json, Options);
            if (parsed is null)
            {
                error = "The balance document deserialised to nothing.";
                return false;
            }
            config = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"The balance document does not match BalanceConfig: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Loads the active revision (the highest one) into <paramref name="provider"/>. Called at startup and
    /// once a minute by <c>BalanceReloadJob</c> so a push made on one instance reaches the others.
    ///
    /// Every failure here is swallowed into a log line ON PURPOSE: the balance is a tuning layer, and an
    /// unreachable database or an unreadable payload must degrade to "keep running the balance we already
    /// have" rather than take the API down. The metrics endpoint reports the revision each instance is
    /// actually on, which is how a stuck instance becomes visible.
    /// </summary>
    public static async Task LoadActiveAsync(
        FtsDbContext db, IBalanceProvider provider, ILogger? log = null, CancellationToken ct = default)
    {
        try
        {
            var active = await db.BalanceRevisions
                .OrderByDescending(r => r.Revision)
                .FirstOrDefaultAsync(ct);

            if (active is null || active.Revision <= provider.Revision) return;

            if (!TryParse(active.Json, out var config, out var error))
            {
                log?.LogError(
                    "Balance revision {Revision} is stored but unreadable ({Error}); staying on revision {Current}.",
                    active.Revision, error, provider.Revision);
                return;
            }

            provider.Set(config, active.Revision);
            log?.LogInformation(
                "Balance revision {Revision} loaded (config version {Version}).",
                active.Revision, config.Version);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "Could not load the active balance revision; staying on revision {Current}.",
                provider.Revision);
        }
    }
}
