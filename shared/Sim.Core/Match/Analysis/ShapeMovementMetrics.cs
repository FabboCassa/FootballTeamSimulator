namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The shape, movement, pitch-bounds and keeper readings of one match (real-match spec R4, R5,
    /// R6, R8). Raw sums and counts, so many matches pool into one figure; see
    /// <see cref="ShapeMovementAnalyzer"/> for the definitions. A "sample" is one side in one
    /// open-play frame. Distances are summed in decimetres, as the stream stores them.
    /// </summary>
    public sealed class ShapeMovementMetrics
    {
        private const double DmPerM = 10;
        private const double DmPerKm = 10_000;
        private const int OutfieldPlayers = 10;

        /// <summary>Frames with the ball in play.</summary>
        public int OpenPlayFrames { get; set; }

        /// <summary>R4, out of possession, anywhere: samples, and the sum of their block lengths and widths.</summary>
        public long DefendingSamples { get; set; }
        public long DefendingLengthDm { get; set; }
        public long DefendingWidthDm { get; set; }

        /// <summary>R4, out of possession with the ball in the defending side's own half.</summary>
        public long OwnHalfDefendingSamples { get; set; }
        public long OwnHalfLengthDm { get; set; }

        /// <summary>R4, in possession with the ball in the opposition half: width, and samples with both touchline zones manned.</summary>
        public long AttackingSamples { get; set; }
        public long AttackingWidthDm { get; set; }
        public long BothTouchlinesSamples { get; set; }

        /// <summary>R4: the median, over the outfielders of both sides, of each man's off-target seconds.</summary>
        public double MedianOffTargetSeconds { get; set; }

        /// <summary>R8: outfielder-frames of open play, and those spent under 0.2 m/s.</summary>
        public long OutfieldOpenPlayFrames { get; set; }
        public long StandStillFrames { get; set; }

        /// <summary>R8 / R2: each side's outfield distance over the whole match, substitutes included.</summary>
        public double HomeOutfieldDistanceDm { get; set; }
        public double AwayOutfieldDistanceDm { get; set; }

        /// <summary>R5: outfielder-frames more than 1 m outside the pitch that no exemption covers.</summary>
        public long OffPitchFrames { get; set; }

        /// <summary>R6: keeper-frames of open play, and those breaking the depth rule.</summary>
        public long KeeperOpenPlayFrames { get; set; }
        public long KeeperDepthBreakFrames { get; set; }

        public double BlockLengthM => Mean(DefendingLengthDm, DefendingSamples);
        public double OwnHalfBlockLengthM => Mean(OwnHalfLengthDm, OwnHalfDefendingSamples);
        public double BlockWidthM => Mean(DefendingWidthDm, DefendingSamples);
        public double AttackingWidthM => Mean(AttackingWidthDm, AttackingSamples);
        public double BothTouchlinesPercent => Percent(BothTouchlinesSamples, AttackingSamples);
        public double StandStillPercent => Percent(StandStillFrames, OutfieldOpenPlayFrames);
        public double KeeperDepthBreakPercent => Percent(KeeperDepthBreakFrames, KeeperOpenPlayFrames);

        /// <summary>Doc §1.5 convention: the side's outfield distance ÷ 10, whoever was sent off.</summary>
        public double DistancePerOutfieldPlayerKm(bool home) =>
            (home ? HomeOutfieldDistanceDm : AwayOutfieldDistanceDm) / OutfieldPlayers / DmPerKm;

        internal static double Mean(long sumDm, long samples) => samples <= 0 ? 0 : sumDm / DmPerM / samples;

        internal static double Percent(long part, long whole) => whole <= 0 ? 0 : 100.0 * part / whole;
    }
}
