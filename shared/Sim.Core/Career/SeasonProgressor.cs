using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Career
{
    /// <summary>
    /// Advances the career calendar one day at a time, simulating due
    /// fixtures headless (task 2.3). Clubs with a valid LineupPlan use it
    /// (task 2.4); everyone else gets an AI-picked best eleven — invalid
    /// plans fall back to AI silently, mirroring the online no-show rule
    /// (ARCHITECTURE.md §6.4).
    ///
    /// Each fixture gets its own RNG derived deterministically from
    /// (world seed, fixture id): results never depend on the order or the
    /// day matches are simulated on.
    /// </summary>
    public sealed class SeasonProgressor
    {
        /// <summary>Golden-ratio odd constant, decorrelates per-fixture seeds.</summary>
        private const ulong FixtureSeedMix = 0x9E3779B97F4A7C15UL;

        private readonly MatchEngine _engine;

        public SeasonProgressor(BalanceConfig? config = null)
        {
            _engine = new MatchEngine(config);
        }

        /// <summary>Single-league convenience overload.</summary>
        public List<MatchOutcome> AdvanceDay(
            League league,
            Season season,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans = null)
        {
            return AdvanceDay(new[] { league }, season, worldSeed, lineupPlans);
        }

        /// <summary>
        /// Moves to the next day and simulates every unplayed fixture due by
        /// then, across all divisions. Returns the outcomes (empty on a quiet day).
        /// </summary>
        public List<MatchOutcome> AdvanceDay(
            IReadOnlyList<League> leagues,
            Season season,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans = null)
        {
            season.CurrentDay++;

            var outcomes = new List<MatchOutcome>();
            foreach (Fixture fixture in season.Fixtures)
            {
                if (fixture.Played || fixture.Day > season.CurrentDay)
                    continue;

                outcomes.Add(Simulate(leagues, season, fixture, worldSeed, lineupPlans));
            }

            return outcomes;
        }

        private MatchOutcome Simulate(
            IReadOnlyList<League> leagues,
            Season season,
            Fixture fixture,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans)
        {
            Club home = FindClub(leagues, fixture.HomeClubId)
                ?? throw new InvalidOperationException($"Fixture {fixture.Id}: home club {fixture.HomeClubId} not in world.");
            Club away = FindClub(leagues, fixture.AwayClubId)
                ?? throw new InvalidOperationException($"Fixture {fixture.Id}: away club {fixture.AwayClubId} not in world.");

            var rng = new Pcg32(worldSeed ^ ((ulong)fixture.Id * FixtureSeedMix), (ulong)fixture.Id);
            MatchReport report = _engine.Simulate(ResolveLineup(home, lineupPlans), ResolveLineup(away, lineupPlans), rng);

            fixture.HomeGoals = report.HomeGoals;
            fixture.AwayGoals = report.AwayGoals;
            fixture.Played = true;

            foreach (MatchEvent matchEvent in report.Events)
            {
                if (matchEvent.Type == MatchEventType.Goal)
                    AddGoal(season, matchEvent);
            }

            return new MatchOutcome(fixture, report);
        }

        private static Club? FindClub(IReadOnlyList<League> leagues, int clubId)
        {
            foreach (League league in leagues)
            {
                Club? club = league.FindClub(clubId);
                if (club != null)
                    return club;
            }

            return null;
        }

        private static void AddGoal(Season season, MatchEvent matchEvent)
        {
            foreach (ScorerTally tally in season.Scorers)
            {
                if (tally.PlayerId == matchEvent.PlayerId)
                {
                    tally.Goals++;
                    return;
                }
            }

            season.Scorers.Add(new ScorerTally
            {
                PlayerId = matchEvent.PlayerId,
                ClubId = matchEvent.ClubId,
                Goals = 1
            });
        }

        private static Lineup ResolveLineup(Club club, IReadOnlyDictionary<int, LineupPlan>? lineupPlans)
        {
            if (lineupPlans != null
                && lineupPlans.TryGetValue(club.Id, out LineupPlan? plan)
                && plan.TryMaterialize(club, out Lineup? lineup))
            {
                return lineup!;
            }

            return LineupSelector.BestEleven(club);
        }
    }
}
