namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The targets of docs/specs/real-match-and-playing-styles.md R4, R5, R6 and R8 that
    /// <see cref="ShapeMovementTally"/> prints against. They are spec targets, not R2 bands: the
    /// reference doc gives no league figure for them, so they live here and not in
    /// <see cref="RealismReference"/>. Distance covered is the one R2 band these readings share,
    /// and it is read from there.
    /// </summary>
    public static class ShapeMovementTargets
    {
        public const string R4 = "spec R4";
        public const string R5 = "spec R5";
        public const string R6 = "spec R6";
        public const string R8 = "spec R8";

        public static readonly RealismBand OwnHalfBlockLengthM =
            new RealismBand("OOP block length, own half m", 25, 40);
        public static readonly RealismBand BlockLengthM =
            new RealismBand("OOP block length, anywhere m", double.NegativeInfinity, 45);
        public static readonly RealismBand BlockWidthM = new RealismBand("OOP block width m", 30, 45);
        public static readonly RealismBand AttackingWidthM =
            new RealismBand("IP width, opposition half m", 45, double.PositiveInfinity);

        /// <summary>R4 asks for a man within 8 m of each touchline but sets no share of time: printed, not judged.</summary>
        public const string BothTouchlinesName = "IP both touchline zones manned %";

        public static readonly RealismBand MedianOffTargetSeconds =
            new RealismBand("off-target s/player (median)", double.NegativeInfinity, 5);
        public static readonly RealismBand StandStillPercent =
            new RealismBand("stand-still % of open play", double.NegativeInfinity, 15);

        /// <summary>A total over every match, not a mean: R5 wants the counter at 0 over the run.</summary>
        public static readonly RealismBand OffPitchFrames =
            new RealismBand("outfield frames >1 m off pitch", double.NegativeInfinity, 0);

        public static readonly RealismBand KeeperDepthBreakPercent =
            new RealismBand("keeper depth breaks % open play", double.NegativeInfinity, 1);
    }
}
