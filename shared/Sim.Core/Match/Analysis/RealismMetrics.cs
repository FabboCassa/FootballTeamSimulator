namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The realism readings of one match (watchable-match spec R2, R4, R5, R7). See
    /// <see cref="RealismAnalyzer"/> for the definitions.
    /// </summary>
    public sealed class RealismMetrics
    {
        /// <summary>R4: spells of a carrier within 20 m of goal with a clear lane, and how many he shot from within 1.5 s.</summary>
        public int OpenGoalChances { get; set; }
        public int OpenGoalShots { get; set; }

        /// <summary>A match with no open-goal chance missed none.</summary>
        public double OpenGoalShotRate =>
            OpenGoalChances <= 0 ? 1.0 : (double)OpenGoalShots / OpenGoalChances;

        /// <summary>R5: every possession, and the sterile ones among them.</summary>
        public int Possessions { get; set; }
        public int SterilePossessions { get; set; }

        public double SterilePossessionShare =>
            Possessions <= 0 ? 0 : (double)SterilePossessions / Possessions;

        /// <summary>R7: open-play possessions of each side that reached the opponent's penalty area.</summary>
        public int HomeBoxEntries { get; set; }
        public int AwayBoxEntries { get; set; }

        /// <summary>R2: the median, over the outfielders of both sides, of each man's off-target seconds.</summary>
        public double MedianOffTargetSeconds { get; set; }
    }
}
