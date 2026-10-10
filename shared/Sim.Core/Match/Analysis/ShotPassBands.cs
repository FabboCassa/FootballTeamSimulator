namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The bands of docs/specs/real-match-and-playing-styles.md that are not R2 rows, and so not
    /// in <see cref="RealismReference"/>: R7 (keeper reach), R9 (passing chains) and R10 (the
    /// open-goal shortcut), plus R9's pass volume
    /// (R2's row scaled to the engine's ball in play). Percentages are 0-100.
    /// </summary>
    public static class ShotPassBands
    {
        /// <summary>R7: on at least 70% of saves the keeper is within 1.5 m of the crossing point or the ball.</summary>
        public static readonly RealismBand KeeperReachPercent =
            new RealismBand("keeper within 1.5 m on saves %", 70, double.PositiveInfinity);

        /// <summary>R9: open-play shots after 0-1 passes in the sequence, at most 35%.</summary>
        public static readonly RealismBand OpenPlayShotsZeroToOnePercent =
            new RealismBand("open-play shots after 0-1 passes %", double.NegativeInfinity, 35);

        /// <summary>
        /// R9: the same, rebounds and high turnovers not counted, at most 25%: they leave both the
        /// 0-1 count and the open-play shots it is a share of.
        /// </summary>
        public static readonly RealismBand OpenPlayShotsZeroToOneExclPercent =
            new RealismBand("  ... excl. rebounds/high turnovers %", double.NegativeInfinity, 25);

        /// <summary>R9: open-play shots after 3+ passes in the sequence, at least 40%.</summary>
        public static readonly RealismBand OpenPlayShotsThreePlusPercent =
            new RealismBand("open-play shots after 3+ passes %", 40, double.PositiveInfinity);

        /// <summary>
        /// R9's volume row: passes attempted per team, read against R2's 380-520
        /// (<see cref="RealismReference.PassesPerTeam"/>) scaled by 81/56, because the engine's ball is
        /// in play about 81 minutes, not real football's 53-59 (user decision 2026-10-10, #82).
        /// </summary>
        public static readonly RealismBand PassesPerTeamAt81Minutes =
            new RealismBand(RealismReference.PassesPerTeam.Name, 550, 750);

        /// <summary>R10: shots from the open-goal shortcut, at most 15% of all shots.</summary>
        public static readonly RealismBand OpenGoalShortcutPercent =
            new RealismBand("open-goal shortcut % of shots", double.NegativeInfinity, 15);
    }
}
