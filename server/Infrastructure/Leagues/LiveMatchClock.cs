using System.Text;
using System.Text.Json;
using Sim.Core.Match;
using Sim.Core.Match.Broadcast;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// The server's reading of the shared live clock (watchable-match-engine R16). Every client shows the
/// moment of the broadcast director's timeline, played from the shared kickoff instant, of the report it
/// holds; the server reads the SAME Sim.Core clock on the SAME stored report, so the minute it judges a
/// change against is the minute both screens show. Ranked and private-league live matches share it.
/// </summary>
public static class LiveMatchClock
{
    /// <summary>A stored report opens with its engine version, so a short prefix is enough to read it.</summary>
    private const int VersionPrefixChars = 256;

    /// <summary>The match minute on screen at <paramref name="nowUtc"/>; 0 while there is no report.</summary>
    public static int MinuteAt(string? reportJson, DateTime kickoffUtc, DateTime nowUtc)
    {
        LiveBroadcastClock? clock = ClockOf(reportJson, kickoffUtc);
        return clock?.MinuteAt(nowUtc) ?? 0;
    }

    /// <summary>The kickoff instant that puts <paramref name="minute"/> on screen at <paramref name="nowUtc"/>
    /// (the dev fast-forward); null while there is no report to build the timeline from.</summary>
    public static DateTime? KickoffShowing(string? reportJson, int minute, DateTime nowUtc)
    {
        LiveBroadcastClock? clock = ClockOf(reportJson, nowUtc);
        return clock is null ? null : nowUtc - TimeSpan.FromSeconds(clock.SecondsToReach(minute));
    }

    /// <summary>The match engine a stored report was simulated with. With no report yet it is this server's
    /// engine, the one that will simulate it.</summary>
    public static int EngineVersionOf(string? reportJson)
    {
        if (string.IsNullOrEmpty(reportJson)) return MatchEngine.Version;

        string prefix = reportJson.Length > VersionPrefixChars ? reportJson[..VersionPrefixChars] : reportJson;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(prefix), isFinalBlock: false, state: default);
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1
                    && reader.ValueTextEquals(nameof(MatchReport.EngineVersion))
                    && reader.Read() && reader.TokenType == JsonTokenType.Number)
                    return reader.GetInt32();
            }
        }
        catch (JsonException)
        {
            // Not in the prefix after all: the full read below settles it.
        }

        return ReplayStore.Read(reportJson)?.EngineVersion ?? 0;
    }

    private static LiveBroadcastClock? ClockOf(string? reportJson, DateTime kickoffUtc)
    {
        if (string.IsNullOrEmpty(reportJson)) return null;
        MatchReport? report = ReplayStore.Read(reportJson);
        return report is null ? null : new LiveBroadcastClock(new BroadcastDirector().Build(report), kickoffUtc);
    }
}
