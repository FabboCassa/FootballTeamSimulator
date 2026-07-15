using System.Collections.Generic;
using System.Text.Json;
using Sim.Core.Domain;
using EntClub = Fts.Infrastructure.Persistence.Entities.Club;
using EntPlayer = Fts.Infrastructure.Persistence.Entities.Player;
using SimClub = Sim.Core.Domain.Club;
using SimLeague = Sim.Core.Domain.League;
using SimPlayer = Sim.Core.Domain.Player;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// Rebuilds a persisted club into a live Sim.Core <see cref="SimClub"/> (with its squad) so the shared
/// match engine can simulate it (Phase 8.3). The reverse of <see cref="WorldFactory"/>: the club's
/// <c>ExternalId</c> becomes the Sim.Core id (unique per world, so lineup/plan player ids resolve), and
/// the jsonb <c>AttributesJson</c> deserialises back to <see cref="PlayerAttributes"/>. Only the fields
/// the engine reads (id, role, attributes) are reconstructed — condition/development are session-derived
/// and become server-authoritative in 8.4; the match uses raw attributes here.
/// </summary>
public static class WorldSquadReader
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The 10 skills as serialized by WorldFactory (canonical Sim.Core skill names).</summary>
    private sealed record AttributeBlob(
        int Pace, int Strength, int Stamina, int Technique, int Passing,
        int Dribbling, int Shooting, int Defending, int Positioning, int Goalkeeping);

    /// <summary>Serialises the 10 skills back to the same jsonb shape <see cref="WorldFactory"/> writes,
    /// so the development tick (Phase 8.4) can persist a player's evolved attributes and they round-trip
    /// exactly to <see cref="PlayerAttributes"/> on the next read.</summary>
    public static string SerializeAttributes(PlayerAttributes a) =>
        JsonSerializer.Serialize(new AttributeBlob(
            a.Pace, a.Strength, a.Stamina, a.Technique, a.Passing,
            a.Dribbling, a.Shooting, a.Defending, a.Positioning, a.Goalkeeping));

    /// <summary>Builds a single-division Sim.Core <see cref="SimLeague"/> holding every given club (with
    /// its squad, condition and potential), so the whole-world condition/development tick (Phase 8.4) and
    /// the <c>WorldStateHasher</c> can operate on the reconstructed world. Division is cosmetic here.</summary>
    public static SimLeague ToSimLeague(IEnumerable<EntClub> clubs)
    {
        var league = new SimLeague { Name = string.Empty, Division = 1 };
        foreach (EntClub club in clubs)
            league.Clubs.Add(ToSimClub(club));
        return league;
    }

    /// <summary>Builds a Sim.Core club from a persisted club whose <c>Players</c> are loaded.</summary>
    public static SimClub ToSimClub(EntClub club)
    {
        var sim = new SimClub
        {
            Id = club.ExternalId,
            Name = club.Name,
            ShortName = club.ShortName,
        };

        foreach (EntPlayer p in club.Players)
            sim.Squad.Players.Add(ToSimPlayer(p));

        return sim;
    }

    private static SimPlayer ToSimPlayer(EntPlayer p)
    {
        var player = new SimPlayer
        {
            Id = p.ExternalId,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Age = p.Age,
            Role = (PositionRole)p.Role,
            MarketValue = p.MarketValue,
        };

        // Server-authoritative condition + potential (Phase 8.4): the reconstructed player carries his
        // stored form/morale/fitness (so a match can resolve on live condition) and potential (so the
        // development tick knows his growth ceiling). Setters clamp to the valid ranges.
        player.Condition.Form = p.Form;
        player.Condition.Morale = p.Morale;
        player.Condition.Fitness = p.Fitness;
        player.Development.Potential = p.Potential;

        AttributeBlob? a = null;
        try { a = JsonSerializer.Deserialize<AttributeBlob>(p.AttributesJson, Json); }
        catch (JsonException) { /* fall through to neutral defaults */ }

        if (a != null)
        {
            player.Attributes.Pace = a.Pace;
            player.Attributes.Strength = a.Strength;
            player.Attributes.Stamina = a.Stamina;
            player.Attributes.Technique = a.Technique;
            player.Attributes.Passing = a.Passing;
            player.Attributes.Dribbling = a.Dribbling;
            player.Attributes.Shooting = a.Shooting;
            player.Attributes.Defending = a.Defending;
            player.Attributes.Positioning = a.Positioning;
            player.Attributes.Goalkeeping = a.Goalkeeping;
        }

        return player;
    }
}
