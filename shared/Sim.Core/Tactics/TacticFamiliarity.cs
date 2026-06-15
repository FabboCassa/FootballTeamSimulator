using System.Collections.Generic;
using Sim.Core.Config;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// Familiarity model (task 3.2): a team gets better at a tactic the more it
    /// uses it, and pays an effectiveness malus for fielding an unfamiliar one.
    /// Pure functions; the host owns the stored levels (see TacticFamiliarityLog).
    /// </summary>
    public static class FamiliarityModel
    {
        /// <summary>
        /// Attack/defense effectiveness multiplier for a given familiarity level:
        /// 1.0 at full familiarity, down to 1 - UnfamiliarPenalty at zero.
        /// </summary>
        public static double EffectivenessMultiplier(int familiarity, TacticsBalance cfg)
        {
            int max = cfg.FamiliarityMax > 0 ? cfg.FamiliarityMax : 1;
            int fam = familiarity < 0 ? 0 : (familiarity > max ? max : familiarity);
            double shortfall = 1.0 - (double)fam / max;          // 1 at zero, 0 at full
            return 1.0 - (cfg.UnfamiliarPenaltyPercent / 100.0) * shortfall;
        }

        /// <summary>One match's worth of familiarity gain, capped at the maximum.</summary>
        public static int Accumulate(int current, TacticsBalance cfg)
        {
            int next = current + cfg.FamiliarityGainPerMatch;
            return next > cfg.FamiliarityMax ? cfg.FamiliarityMax : next;
        }
    }

    /// <summary>
    /// A team's stored familiarity per tactic. In-memory for now; the save wiring
    /// (and the tactics screen that reads it) arrives in task 3.3.
    /// </summary>
    public sealed class TacticFamiliarityLog
    {
        private readonly Dictionary<Tactic, int> _levels = new Dictionary<Tactic, int>();

        public int Get(Tactic tactic) => _levels.TryGetValue(tactic, out int v) ? v : 0;

        /// <summary>Records that this tactic was used for one match (raises its familiarity).</summary>
        public void Use(Tactic tactic, TacticsBalance cfg) =>
            _levels[tactic] = FamiliarityModel.Accumulate(Get(tactic), cfg);
    }
}
