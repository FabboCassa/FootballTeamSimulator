using System.Collections.Generic;
using Sim.Core.Domain;
using Sim.Core.Match;

namespace Fts.Services
{
    /// <summary>
    /// Everything the watch screen needs to re-simulate the user's match live
    /// (task 3.4): the deterministic seed, the kickoff <see cref="MatchPlan"/>
    /// (lineups + tactics, reconstructed identically to what SeasonProgressor
    /// used) and which side the user is. Set by LocalClock right after the match
    /// is played; null when the last advance produced no user match.
    /// Lives in the Game scope. Not persisted — fully regenerable from the save.
    ///
    /// <see cref="HomeRules"/>/<see cref="AwayRules"/> carry the user's resolved
    /// conditional plan (task 3.5) on the user's side (the other is null), so a
    /// re-sim reproduces the committed result the headless advance produced. Both
    /// null when the user has no plan — the re-sim is then byte-identical to the
    /// pre-3.5 path.
    ///
    /// <see cref="KickoffCondition"/> freezes each involved player's form/morale/
    /// fitness as it was at kickoff (task 4.2). The headless advance evolves condition
    /// AFTER the match, so by the time the user watches, the live players are already
    /// drained/rested; the watch re-sim restores these kickoff values for the duration
    /// of the sim so the result stays consistent with the committed one. Empty/null
    /// when condition is not applied — the re-sim then reads live values as before.
    /// </summary>
    public sealed class UserMatchContext
    {
        public Fixture Fixture { get; }
        public ulong WorldSeed { get; }
        public MatchPlan Plan { get; }
        public bool UserIsHome { get; }
        public IReadOnlyList<MatchRule> HomeRules { get; }
        public IReadOnlyList<MatchRule> AwayRules { get; }

        /// <summary>Player id → a frozen copy of his kickoff condition (both clubs' squads).</summary>
        public IReadOnlyDictionary<int, PlayerCondition> KickoffCondition { get; }

        public UserMatchContext(
            Fixture fixture, ulong worldSeed, MatchPlan plan, bool userIsHome,
            IReadOnlyList<MatchRule> homeRules = null, IReadOnlyList<MatchRule> awayRules = null,
            IReadOnlyDictionary<int, PlayerCondition> kickoffCondition = null)
        {
            Fixture = fixture;
            WorldSeed = worldSeed;
            Plan = plan;
            UserIsHome = userIsHome;
            HomeRules = homeRules;
            AwayRules = awayRules;
            KickoffCondition = kickoffCondition ?? new Dictionary<int, PlayerCondition>();
        }
    }

    /// <summary>Mutable holder so the Game scope can hand the latest context to the watch screen.</summary>
    public sealed class UserMatchContextHolder
    {
        public UserMatchContext Current { get; set; }
    }
}
