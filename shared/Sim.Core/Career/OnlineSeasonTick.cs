using System.Collections.Generic;
using Sim.Core.Condition;
using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Domain;

namespace Sim.Core.Career
{
    /// <summary>
    /// Composes the deterministic whole-world condition and development progressors into ONE
    /// server-authoritative weekly tick for an online private-league season (Phase 8.4). One resolved
    /// round is one week (the cadence chosen with the user), applied right after the round's matches:
    ///   1. <b>Match day</b> — <see cref="ConditionProgressor"/> drains the clubs that played (their
    ///      starters lose fitness and take the form/morale step) and rests every idle club one day;
    ///   2. <b>Recovery</b> — the rest of the week: every player recovers
    ///      (<see cref="ConditionModel.ApplyRest"/>) toward the next match — pure, no RNG;
    ///   3. <b>Development</b> — one <see cref="DevelopmentProgressor"/> week for the whole world, with
    ///      each player's minutes context derived from whether he started (starters develop with full
    ///      minutes, the rest at the benched floor), the user clubs using their submitted
    ///      <see cref="TrainingPlan"/> and every other club the AI default.
    ///
    /// Pure and deterministic — every RNG draw is seeded from (worldSeed, day/week, clubId) inside the
    /// progressors, so the result is independent of visit order and identical across .NET / Mono /
    /// IL2CPP. This is the ONE piece of logic the server runs to advance authoritative state and that a
    /// client re-runs to verify agreement via <see cref="WorldStateHasher"/> (the 8.4 ✅). Opt-in by being
    /// called: the match engine never references it, so match golden masters are unaffected.
    /// </summary>
    public static class OnlineSeasonTick
    {
        /// <summary>
        /// Evolves the whole world one round-week. <paramref name="played"/> maps a club id
        /// (<c>Club.Id</c> = the world-unique external id) to what it did this round (its starters + its
        /// result); a club absent from it did not play and simply rests the week.
        /// <paramref name="trainingPlans"/> maps a club id to its submitted plan (absent → AI default).
        /// <paramref name="performanceRatings"/> (engine phase 7) maps a player id to the 0-100
        /// performance signal his match earned — built from a played match's statistics by
        /// <see cref="MatchPerformanceFeed"/>. Absent (the default, and everything before that
        /// phase), every player develops on the neutral rating, exactly as before.
        /// </summary>
        public static void EvolveWeek(
            IReadOnlyList<League> leagues,
            IReadOnlyDictionary<int, ConditionProgressor.Participation> played,
            IReadOnlyDictionary<int, TrainingPlan>? trainingPlans,
            ulong worldSeed,
            int round,
            BalanceConfig cfg,
            IReadOnlyDictionary<int, int>? performanceRatings = null)
        {
            int daysBetween = cfg.Season.DaysBetweenRounds;
            int matchDay = round * (daysBetween > 0 ? daysBetween : 1);
            int restDays = daysBetween > 1 ? daysBetween - 1 : 0;

            // 1) Match day: participants drain, idle clubs take one rest day.
            new ConditionProgressor(cfg.Condition).Evolve(leagues, played, worldSeed, matchDay);

            // 2) The rest of the week: everyone recovers toward the next match (fitness up, morale toward
            //    neutral). ApplyRest is pure integer math with no RNG, so it needs no per-day seeding.
            if (restDays > 0)
            {
                foreach (League league in leagues)
                    foreach (Club club in league.Clubs)
                        foreach (Player player in club.Squad.Players)
                            ConditionModel.ApplyRest(player.Condition, restDays, cfg.Condition);
            }

            // 3) One development week for the whole world, minutes derived from who started this round.
            IReadOnlyDictionary<int, DevelopmentContext> contexts =
                BuildMinutesContexts(leagues, played, cfg.Development, performanceRatings);
            new DevelopmentProgressor(cfg.Development).EvolveWeek(leagues, trainingPlans, contexts, worldSeed, round);
        }

        /// <summary>
        /// A per-player <see cref="DevelopmentContext"/> for the week: a player who started this round
        /// develops with full minutes (100%), the rest at the benched share (0% → the minutes floor).
        /// Facility and performance are neutral (online clubs have no facilities yet — that's 8.5/§5.5),
        /// so growth is driven by age, focus and minutes. Every player is listed explicitly so the
        /// progressor never falls back to its full-minutes neutral default for the benched.
        /// </summary>
        private static IReadOnlyDictionary<int, DevelopmentContext> BuildMinutesContexts(
            IReadOnlyList<League> leagues,
            IReadOnlyDictionary<int, ConditionProgressor.Participation> played,
            DevelopmentBalance cfg,
            IReadOnlyDictionary<int, int>? performanceRatings)
        {
            var starters = new HashSet<int>();
            var minutes = new Dictionary<int, int>();
            foreach (KeyValuePair<int, ConditionProgressor.Participation> kv in played)
            {
                foreach (int id in kv.Value.StarterIds)
                    starters.Add(id);

                // Real minutes, when the host handed them over (engine phase 7). Absent, every
                // player keeps the share he has always had: full for a starter, none for the rest.
                if (kv.Value.MinutesById == null) continue;
                foreach (KeyValuePair<int, int> played90 in kv.Value.MinutesById)
                    minutes[played90.Key] = played90.Value;
            }

            int facility = cfg.FacilityNeutralLevel;
            int neutral = cfg.PerformanceNeutralRating;

            var contexts = new Dictionary<int, DevelopmentContext>();
            foreach (League league in leagues)
                foreach (Club club in league.Clubs)
                    foreach (Player player in club.Squad.Players)
                        contexts[player.Id] = new DevelopmentContext(
                            Share(player.Id, starters, minutes),
                            facility,
                            performanceRatings != null && performanceRatings.TryGetValue(player.Id, out int rating)
                                ? rating
                                : neutral);

            return contexts;
        }

        /// <summary>
        /// How much of the round he played, as a percentage. Ninety minutes or more is a full
        /// share; without real minutes it is the old yes-or-no (a starter, or nobody).
        /// </summary>
        private static int Share(int playerId, HashSet<int> starters, Dictionary<int, int> minutes)
        {
            if (minutes.Count == 0) return starters.Contains(playerId) ? 100 : 0;
            if (!minutes.TryGetValue(playerId, out int played)) return 0;

            int share = played * 100 / 90;
            return share > 100 ? 100 : share;
        }
    }
}
