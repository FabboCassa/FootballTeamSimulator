namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The ball-freedom, crowding, height, pass-flight and loose-ball readings of one match
    /// (free-ball-real-movement spec R2), both sides together. See <see cref="BallFreedomAnalyzer"/>
    /// for the definitions. The pass percentiles are the match's own: like the reference
    /// (doc §10.4), many matches pool by the mean of their values (<see cref="BallFreedomTally"/>).
    /// </summary>
    public sealed class BallFreedomMetrics
    {
        public int InPlayFrames { get; set; }

        /// <summary>In-play frames with nobody within 1.5 m / 3 m, and with the ball above 1 m.</summary>
        public int Free15Frames { get; set; }
        public int Free3Frames { get; set; }
        public int Above1mFrames { get; set; }

        /// <summary>Players within 5 m / 10 m of the ball, summed over in-play frames.</summary>
        public long Within5Sum { get; set; }
        public long Within10Sum { get; set; }

        /// <summary>Tracking-derived passes, and those whose receiver stood >= 3 m from the arrival point at release.</summary>
        public int Passes { get; set; }
        public int PassesIntoSpace { get; set; }

        /// <summary>The match's pass percentiles; 0 with no passes.</summary>
        public double FlightP50Seconds { get; set; }
        public double FlightP90Seconds { get; set; }
        public double ReceiverToArrivalP50Meters { get; set; }

        /// <summary>Loose-ball episodes after a duel, and after the other team's touch.</summary>
        public int LooseDuels { get; set; }
        public int LooseDeflections { get; set; }

        public int LooseBalls => LooseDuels + LooseDeflections;

        public double Free15Percent => Percent(Free15Frames, InPlayFrames);
        public double Free3Percent => Percent(Free3Frames, InPlayFrames);
        public double Above1mPercent => Percent(Above1mFrames, InPlayFrames);
        public double Within5 => InPlayFrames == 0 ? 0 : (double)Within5Sum / InPlayFrames;
        public double Within10 => InPlayFrames == 0 ? 0 : (double)Within10Sum / InPlayFrames;
        public double IntoSpacePercent => Percent(PassesIntoSpace, Passes);

        private static double Percent(long part, long whole) => whole <= 0 ? 0 : 100.0 * part / whole;
    }
}
