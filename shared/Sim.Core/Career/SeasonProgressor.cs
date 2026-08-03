using System;
using System.Collections.Generic;
using Sim.Core.Condition;
using Sim.Core.Config;
using Sim.Core.Difficulty;
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

        /// <summary>Odd constants giving the AI lineup RNG (task 5.7) its own stream, independent of
        /// the match RNG — so degrading an AI XI never perturbs the seeded match draw.</summary>
        private const ulong LineupSeedMix = 0xD1B54A32D192ED03UL;
        private const ulong LineupClubMix = 0xCBF29CE484222325UL;

        private readonly MatchEngine _engine;
        private readonly ConditionProgressor _conditionProgressor;
        private readonly int _familiarityMax;

        /// <summary>
        /// <paramref name="applyCondition"/> opts simulated matches into the 4.1 model
        /// (ratings scaled by form/morale/fitness); defaults false so AdvanceDay stays
        /// byte-identical. <paramref name="applyMatchFatigue"/> additionally opts into
        /// within-match fatigue (4.2 refinement; separate flag, default off). Condition
        /// evolution across days is a separate host-driven step (<see cref="EvolveCondition"/>),
        /// so results and evolution opt in independently. <paramref name="applyPositioning"/>
        /// opts into free positioning (task 6.10): a club fielding a lineup with custom
        /// off-anchor positions gets the small shape tilt; AI clubs field clean presets
        /// (best XI, no custom positions), so their matches stay byte-identical even with
        /// the flag on — only the user's own custom shape is affected.
        /// </summary>
        public SeasonProgressor(BalanceConfig? config = null, bool applyCondition = false, bool applyMatchFatigue = false, bool applyPositioning = false)
        {
            BalanceConfig cfg = config ?? new BalanceConfig();
            _engine = new MatchEngine(cfg, applyCondition, applyMatchFatigue, applyPositioning);
            _conditionProgressor = new ConditionProgressor(cfg.Condition);
            _familiarityMax = cfg.Tactics.FamiliarityMax;
        }

        /// <summary>Single-league convenience overload.</summary>
        public List<MatchOutcome> AdvanceDay(
            League league,
            Season season,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans = null,
            IReadOnlyDictionary<int, TacticContext>? tactics = null,
            IReadOnlyDictionary<int, IReadOnlyList<MatchRule>>? rules = null,
            DifficultyContext? difficulty = null)
        {
            return AdvanceDay(new[] { league }, season, worldSeed, lineupPlans, tactics, rules, difficulty);
        }

        /// <summary>
        /// Moves to the next day and simulates every unplayed fixture due by
        /// then, across all divisions. Returns the outcomes (empty on a quiet day).
        /// <paramref name="tactics"/> maps a club id to its tactic+familiarity (no
        /// entry = neutral = engine identity, task 3.2). <paramref name="rules"/> maps a
        /// club id to its resolved conditional plan (task 3.5); neither side having
        /// rules keeps the byte-identical legacy path, a club with rules has them
        /// executed automatically (the skipped/unwatched "AI fallback" path).
        /// </summary>
        public List<MatchOutcome> AdvanceDay(
            IReadOnlyList<League> leagues,
            Season season,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans = null,
            IReadOnlyDictionary<int, TacticContext>? tactics = null,
            IReadOnlyDictionary<int, IReadOnlyList<MatchRule>>? rules = null,
            DifficultyContext? difficulty = null)
        {
            season.CurrentDay++;

            var outcomes = new List<MatchOutcome>();
            foreach (Fixture fixture in season.Fixtures)
            {
                if (fixture.Played || fixture.Day > season.CurrentDay)
                    continue;

                outcomes.Add(Simulate(leagues, season, fixture, worldSeed, lineupPlans, tactics, rules, difficulty));
            }

            return outcomes;
        }

        /// <summary>
        /// Evolves the whole world's condition for the day just advanced (task 4.2).
        /// Call AFTER AdvanceDay: clubs that played (from <paramref name="dayOutcomes"/>)
        /// drain their kickoff XI and step the whole squad; every other club rests a day.
        /// Kickoff lineups are resolved exactly as the match used them (same
        /// <paramref name="lineupPlans"/> fallback); subs' partial minutes are not modelled
        /// in v1. Opt-in by being called — never calling it leaves condition untouched.
        /// </summary>
        public void EvolveCondition(
            IReadOnlyList<League> leagues,
            Season season,
            IReadOnlyList<MatchOutcome> dayOutcomes,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans = null,
            DifficultyContext? difficulty = null)
        {
            var played = new Dictionary<int, ConditionProgressor.Participation>();
            foreach (MatchOutcome outcome in dayOutcomes)
            {
                Fixture fixture = outcome.Fixture;
                Club? home = FindClub(leagues, fixture.HomeClubId);
                Club? away = FindClub(leagues, fixture.AwayClubId);

                if (home != null)
                    played[fixture.HomeClubId] = new ConditionProgressor.Participation(
                        StarterIds(ResolveLineup(home, lineupPlans, difficulty, worldSeed, fixture.Id)),
                        ResultFor(fixture, asHome: true));
                if (away != null)
                    played[fixture.AwayClubId] = new ConditionProgressor.Participation(
                        StarterIds(ResolveLineup(away, lineupPlans, difficulty, worldSeed, fixture.Id)),
                        ResultFor(fixture, asHome: false));
            }

            _conditionProgressor.Evolve(leagues, played, worldSeed, season.CurrentDay);
        }

        private static HashSet<int> StarterIds(Lineup lineup)
        {
            var ids = new HashSet<int>();
            foreach (LineupSlot slot in lineup.Slots) ids.Add(slot.Player.Id);
            return ids;
        }

        private static TeamResult ResultFor(Fixture fixture, bool asHome)
        {
            int own = asHome ? fixture.HomeGoals : fixture.AwayGoals;
            int opp = asHome ? fixture.AwayGoals : fixture.HomeGoals;
            return own > opp ? TeamResult.Win : own < opp ? TeamResult.Loss : TeamResult.Draw;
        }

        private MatchOutcome Simulate(
            IReadOnlyList<League> leagues,
            Season season,
            Fixture fixture,
            ulong worldSeed,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans,
            IReadOnlyDictionary<int, TacticContext>? tactics,
            IReadOnlyDictionary<int, IReadOnlyList<MatchRule>>? rules,
            DifficultyContext? difficulty)
        {
            Club home = FindClub(leagues, fixture.HomeClubId)
                ?? throw new InvalidOperationException($"Fixture {fixture.Id}: home club {fixture.HomeClubId} not in world.");
            Club away = FindClub(leagues, fixture.AwayClubId)
                ?? throw new InvalidOperationException($"Fixture {fixture.Id}: away club {fixture.AwayClubId} not in world.");

            Pcg32 rng = FixtureRng(worldSeed, fixture.Id);
            MatchTactics? matchTactics = BuildTactics(fixture, tactics);
            Lineup homeLineup = ResolveLineup(home, lineupPlans, difficulty, worldSeed, fixture.Id);
            Lineup awayLineup = ResolveLineup(away, lineupPlans, difficulty, worldSeed, fixture.Id);

            IReadOnlyList<MatchRule>? homeRules = RulesFor(rules, fixture.HomeClubId);
            IReadOnlyList<MatchRule>? awayRules = RulesFor(rules, fixture.AwayClubId);

            // No rules on either side -> unchanged legacy call (byte-identical).
            MatchReport report = homeRules == null && awayRules == null
                ? _engine.Simulate(homeLineup, awayLineup, rng, matchTactics)
                : _engine.Simulate(new MatchPlan(new MatchInput(homeLineup, awayLineup, matchTactics)), homeRules, awayRules, rng);

            RecordResult(season, fixture, report);
            return new MatchOutcome(fixture, report);
        }

        /// <summary>A club's resolved rules, or null when it has none (so the legacy path is kept).</summary>
        private static IReadOnlyList<MatchRule>? RulesFor(
            IReadOnlyDictionary<int, IReadOnlyList<MatchRule>>? rules, int clubId)
        {
            if (rules != null && rules.TryGetValue(clubId, out IReadOnlyList<MatchRule>? r) && r != null && r.Count > 0)
                return r;

            return null;
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

        private static Lineup ResolveLineup(
            Club club,
            IReadOnlyDictionary<int, LineupPlan>? lineupPlans,
            DifficultyContext? difficulty,
            ulong worldSeed,
            int fixtureId)
        {
            if (lineupPlans != null
                && lineupPlans.TryGetValue(club.Id, out LineupPlan? plan)
                && plan.TryMaterialize(club, out Lineup? lineup))
            {
                return lineup!;
            }

            return ResolveAiLineup(club, worldSeed, fixtureId, difficulty);
        }

        /// <summary>
        /// Resolves an AI club's lineup for a fixture EXACTLY as <see cref="AdvanceDay(IReadOnlyList{League}, Season, ulong, IReadOnlyDictionary{int, LineupPlan}?, IReadOnlyDictionary{int, TacticContext}?, IReadOnlyDictionary{int, IReadOnlyList{MatchRule}}?, DifficultyContext?)"/>
        /// does: under difficulty (task 5.7), an AI club below full competence fields a weaker (real)
        /// XI via the dedicated per-fixture lineup RNG; the human club, a fully-competent AI, and the
        /// null/legacy path all field the best XI (byte-identical to the pre-5.7 engine). Public so a
        /// host can reproduce the very same opponent XI the season used — e.g. the watched user match
        /// (task 3.4), whose re-sim must reproduce the committed result.
        /// </summary>
        public static Lineup ResolveAiLineup(Club club, ulong worldSeed, int fixtureId, DifficultyContext? difficulty)
        {
            if (difficulty.HasValue)
            {
                DifficultyContext d = difficulty.Value;
                // A fully competent manager who also never rotates is the best XI, so the legacy path is
                // kept literally; anything else goes through the selector (which still returns the best XI
                // when the squad is fully fit — the rotation policy only ever discounts tired players).
                if (club.Id != d.HumanClubId && d.DegradesAiLineups)
                {
                    Pcg32 lineupRng = LineupRng(worldSeed, fixtureId, club.Id);
                    return LineupSelector.CompetentEleven(
                        club, LineupSelector.DefaultFormation,
                        d.AiLineupCompetence, d.AiLineupMaxSlips, d.Rotation, lineupRng);
                }
            }

            return LineupSelector.BestEleven(club);
        }

        /// <summary>An independent per-(fixture, club) RNG for AI lineup selection — decorrelated from
        /// the match RNG so a degraded XI never alters the seeded match draw.</summary>
        private static Pcg32 LineupRng(ulong worldSeed, int fixtureId, int clubId) =>
            new Pcg32(
                worldSeed ^ ((ulong)(uint)fixtureId * LineupSeedMix) ^ ((ulong)(uint)clubId * LineupClubMix),
                0xA17EUL);
    }
}
