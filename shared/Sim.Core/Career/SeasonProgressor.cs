using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

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
        private readonly int _familiarityMax;

        public SeasonProgressor(BalanceConfig? config = null)
        {
            BalanceConfig cfg = config ?? new BalanceConfig();
            _engine = new MatchEngine(cfg);
            _familiarityMax = cfg.Tactics.FamiliarityMax;
        }

        /// <summary>Single-league convenience overload.</summary>
        public List<MatchOutcome> AdvanceDay(
            League league,
            Season season,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans = null,
            IReadOnlyDictionary<int, TacticContext>? tactics = null)
        {
            return AdvanceDay(new[] { league }, season, worldSeed, lineupPlans, tactics);
        }

        /// <summary>
        /// Moves to the next day and simulates every unplayed fixture due by
        /// then, across all divisions. Returns the outcomes (empty on a quiet day).
        ///
        /// <paramref name="tactics"/> maps a club id to its own tactical setup
        /// (tactic + familiarity). A club with no entry plays a neutral tactic,
        /// which is the engine identity, so a fixture in which neither side has a
        /// tactic is byte-identical to the pre-tactics result (task 3.2 guarantee).
        /// </summary>
        public List<MatchOutcome> AdvanceDay(
            IReadOnlyList<League> leagues,
            Season season,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans = null,
            IReadOnlyDictionary<int, TacticContext>? tactics = null)
        {
            season.CurrentDay++;

            var outcomes = new List<MatchOutcome>();
            foreach (Fixture fixture in season.Fixtures)
            {
                if (fixture.Played || fixture.Day > season.CurrentDay)
                    continue;

                outcomes.Add(Simulate(leagues, season, fixture, worldSeed, lineupPlans, tactics));
            }

            return outcomes;
        }

        private MatchOutcome Simulate(
            IReadOnlyList<League> leagues,
            Season season,
            Fixture fixture,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans,
            IReadOnlyDictionary<int, TacticContext>? tactics)
        {
            Club home = FindClub(leagues, fixture.HomeClubId)
                ?? throw new InvalidOperationException($"Fixture {fixture.Id}: home club {fixture.HomeClubId} not in world.");
            Club away = FindClub(leagues, fixture.AwayClubId)
                ?? throw new InvalidOperationException($"Fixture {fixture.Id}: away club {fixture.AwayClubId} not in world.");

            Pcg32 rng = FixtureRng(worldSeed, fixture.Id);
            MatchTactics? matchTactics = BuildTactics(fixture, tactics);
            MatchReport report = _engine.Simulate(
                ResolveLineup(home, lineupPlans), ResolveLineup(away, lineupPlans), rng, matchTactics);

            RecordResult(season, fixture, report);
            return new MatchOutcome(fixture, report);
        }

        /// <summary>
        /// The deterministic RNG for a fixture, derived from (world seed, fixture id).
        /// Public so a host can re-simulate the very same match — e.g. a watched
        /// user match that accepts live substitutions/tactic changes (task 3.4) —
        /// or verify a server result (ARCHITECTURE.md §6.4).
        /// </summary>
        public static Pcg32 FixtureRng(ulong worldSeed, int fixtureId) =>
            new Pcg32(worldSeed ^ ((ulong)fixtureId * FixtureSeedMix), (ulong)fixtureId);

        /// <summary>
        /// Commits a report to the fixture (score + played) and adds its goals to
        /// the scorer tally. Public so a host can commit a match it simulated
        /// itself (the watched user match), not just the ones AdvanceDay runs.
        /// </summary>
        public static void RecordResult(Season season, Fixture fixture, MatchReport report)
        {
            fixture.HomeGoals = report.HomeGoals;
            fixture.AwayGoals = report.AwayGoals;
            fixture.Played = true;

            foreach (MatchEvent matchEvent in report.Events)
            {
                if (matchEvent.Type == MatchEventType.Goal)
                    AddGoal(season, matchEvent);
            }
        }

        /// <summary>
        /// Undoes a previously-recorded report's contribution to the scorer tally
        /// (zeroed tallies are removed). Lets a host replace a provisional result
        /// with the post-intervention one: RevertResult(old) then RecordResult(new).
        /// The fixture score is left to the following RecordResult to overwrite.
        /// </summary>
        public static void RevertResult(Season season, Fixture fixture, MatchReport report)
        {
            foreach (MatchEvent matchEvent in report.Events)
            {
                if (matchEvent.Type == MatchEventType.Goal)
                    RemoveGoal(season, matchEvent);
            }
        }

        /// <summary>
        /// Builds the per-fixture tactical setup. Returns null when neither side
        /// has a tactic (the engine then runs in its pre-tactics path); otherwise
        /// each side falls back to a neutral, fully-familiar tactic = identity.
        /// </summary>
        private MatchTactics? BuildTactics(Fixture fixture, IReadOnlyDictionary<int, TacticContext>? tactics)
        {
            if (tactics == null)
                return null;

            bool hasHome = tactics.TryGetValue(fixture.HomeClubId, out TacticContext home);
            bool hasAway = tactics.TryGetValue(fixture.AwayClubId, out TacticContext away);
            if (!hasHome && !hasAway)
                return null;

            return new MatchTactics(
                hasHome ? home : TacticContext.Neutral(_familiarityMax),
                hasAway ? away : TacticContext.Neutral(_familiarityMax));
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

        private static void RemoveGoal(Season season, MatchEvent matchEvent)
        {
            for (int i = 0; i < season.Scorers.Count; i++)
            {
                ScorerTally tally = season.Scorers[i];
                if (tally.PlayerId != matchEvent.PlayerId)
                    continue;

                tally.Goals--;
                if (tally.Goals <= 0)
                    season.Scorers.RemoveAt(i); // keep the "one positive tally per scorer" invariant
                return;
            }
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
