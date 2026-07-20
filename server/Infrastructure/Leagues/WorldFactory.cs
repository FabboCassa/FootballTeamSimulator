using System.Text.Json;
using Fts.Infrastructure.Persistence.Entities;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;
using SimClub = Sim.Core.Domain.Club;
using SimLeague = Sim.Core.Domain.League;
using SimPlayer = Sim.Core.Domain.Player;
using EntClub = Fts.Infrastructure.Persistence.Entities.Club;
using EntLeague = Fts.Infrastructure.Persistence.Entities.League;
using EntPlayer = Fts.Infrastructure.Persistence.Entities.Player;
using EntCoach = Fts.Infrastructure.Persistence.Entities.Coach;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// Runs the shared Sim.Core world generation (Phase 8.1) and maps it onto the persisted world graph
/// (world → division → clubs → players/coaches). The <c>ExternalId</c> on each row is the Sim.Core id,
/// unique per world (ARCHITECTURE §6.3), so the persisted world round-trips to the shared logic and a
/// server re-run of the same seed rebuilds byte-identical squads. Same seed ⇒ same world ⇒ every member
/// sees the same squads with no duplicate players. Denormalised columns (overall/potential/value) are
/// computed here so the lobby/market can query without deserialising the jsonb attributes.
/// </summary>
public static class WorldFactory
{
    /// <summary>Builds a fully-populated (but untracked) <see cref="World"/> aggregate with all Guids
    /// and foreign keys wired, ready to <c>Add</c> + <c>SaveChanges</c> in one graph insert.</summary>
    public static World Build(long seed, int size, string worldName)
    {
        var cfg = new BalanceConfig();
        var options = new LeagueGenerationOptions
        {
            ClubCount = size,
            Division = 1,
            LeagueName = worldName,
        };

        SimLeague sim = new LeagueGenerator(options, cfg).Generate(new Pcg32(unchecked((ulong)seed)));

        var now = DateTime.UtcNow;
        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = worldName,
            Seed = seed,
            CreatedUtc = now,
        };

        var league = new EntLeague
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            World = world,
            Name = sim.Name,
            Division = sim.Division,
            CreatedUtc = now,
        };
        world.Leagues.Add(league);

        foreach (SimClub simClub in sim.Clubs)
        {
            var club = new EntClub
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                World = world,
                LeagueId = league.Id,
                League = league,
                ExternalId = simClub.Id,
                Name = simClub.Name,
                ShortName = simClub.ShortName,
                Strength = SquadStrength(simClub),
                TransferBudget = 0,
                Balance = 0,
            };

            var coach = new EntCoach
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                World = world,
                ClubId = club.Id,
                Club = club,
                ExternalId = simClub.Coach.Id,
                OwnerUserId = null,
                Name = simClub.Coach.Name,
                IsHuman = false,
                Reputation = simClub.Coach.Reputation,
                BoardConfidence = simClub.Coach.BoardConfidence,
                ObjectiveExpectedPosition = simClub.Coach.ObjectiveExpectedPosition,
                LastFinishPosition = simClub.Coach.LastFinishPosition,
            };
            club.Coach = coach;
            world.Coaches.Add(coach);

            foreach (SimPlayer p in simClub.Squad.Players)
            {
                var player = new EntPlayer
                {
                    Id = Guid.NewGuid(),
                    WorldId = world.Id,
                    World = world,
                    ClubId = club.Id,
                    Club = club,
                    ExternalId = p.Id,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    Age = p.Age,
                    Role = (int)p.Role,
                    Overall = PlayerRating.Overall(p),
                    Potential = p.Development.Potential,
                    MarketValue = ValuationModel.Value(p, sim.Division, cfg),
                    WeeklyWage = p.Contract.WeeklyWage,
                    ContractSeasonsRemaining = p.Contract.SeasonsRemaining,
                    AttributesJson = SerializeAttributes(p.Attributes),
                };
                club.Players.Add(player);
                world.Players.Add(player);
            }

            league.Clubs.Add(club);
            world.Clubs.Add(club);
        }

        // Free-agent pool (Phase 8.5): unattached players (ClubId = null) the season-start / mid-season
        // auctions run on. A distinct RNG sub-stream, so club generation — and every golden master — is
        // byte-identical. They carry a high ExternalId range so they never collide with club players, and
        // they are excluded from the draft/squad-equaliser (which only touch club players) and from the
        // world-state hash / development tick (which reconstruct clubs, not clubless players).
        foreach (SimPlayer p in FreeAgentFactory.Generate(seed, cfg))
        {
            var freeAgent = new EntPlayer
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                World = world,
                ClubId = null,
                Club = null,
                ExternalId = p.Id,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Age = p.Age,
                Role = (int)p.Role,
                Overall = PlayerRating.Overall(p),
                Potential = p.Development.Potential,
                MarketValue = ValuationModel.Value(p, sim.Division, cfg),
                WeeklyWage = p.Contract.WeeklyWage,
                ContractSeasonsRemaining = p.Contract.SeasonsRemaining,
                AttributesJson = SerializeAttributes(p.Attributes),
            };
            world.Players.Add(freeAgent);
        }

        return world;
    }

    /// <summary>Coarse squad strength = average overall of the whole squad (used to seed budgets /
    /// finances / objectives when the season starts). A first-pass, best-XI refinement can come later.</summary>
    private static int SquadStrength(SimClub club)
    {
        int count = club.Squad.Players.Count;
        if (count == 0) return 0;
        long sum = 0;
        foreach (SimPlayer p in club.Squad.Players) sum += PlayerRating.Overall(p);
        return (int)(sum / count);
    }

    /// <summary>Serialises the 10 skills by their canonical names so the jsonb blob round-trips
    /// exactly to <see cref="PlayerAttributes"/> (matching the client's persisted shape).</summary>
    private static string SerializeAttributes(PlayerAttributes a) => JsonSerializer.Serialize(new
    {
        a.Pace,
        a.Strength,
        a.Stamina,
        a.Technique,
        a.Passing,
        a.Dribbling,
        a.Shooting,
        a.Defending,
        a.Positioning,
        a.Goalkeeping,
    });
}
