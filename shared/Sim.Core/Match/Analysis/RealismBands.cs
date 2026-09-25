using System.Globalization;

namespace Sim.Core.Match.Analysis
{
    /// <summary>One band a realism reading is judged against: inclusive on both ends.</summary>
    public readonly struct RealismBand
    {
        public string Name { get; }
        public double Min { get; }
        public double Max { get; }

        public RealismBand(string name, double min, double max)
        {
            Name = name;
            Min = min;
            Max = max;
        }

        public bool Contains(double value) => value >= Min && value <= Max;

        public string Describe()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            if (double.IsPositiveInfinity(Max)) return ">= " + Min.ToString("0.##", inv);
            if (double.IsNegativeInfinity(Min)) return "<= " + Max.ToString("0.##", inv);
            return Min.ToString("0.##", inv) + "-" + Max.ToString("0.##", inv);
        }
    }

    /// <summary>
    /// The realism bands of the watchable-match spec (docs/specs/watchable-match-engine.md), in
    /// one place. R7: equal-strength neutral sides over 1,000 matches. R4, R5, R2: the open-goal,
    /// circling and off-target readings. R19: the new brain's time against V10's in the same run.
    /// </summary>
    public static class RealismBands
    {
        public static readonly RealismBand Goals = new RealismBand("goals/match", 2.4, 3.0);
        public static readonly RealismBand Shots = new RealismBand("shots/match", 20, 28);
        public static readonly RealismBand OnTargetPercent = new RealismBand("on target % of shots", 30, 40);
        public static readonly RealismBand Corners = new RealismBand("corners/match", 8, 12);
        public static readonly RealismBand Fouls = new RealismBand("fouls/match", 20, 28);

        /// <summary>Open-play penalty-area entries per side per match.</summary>
        public static readonly RealismBand BoxEntriesPerSide =
            new RealismBand("open-play box entries/side", 4, double.PositiveInfinity);

        public static readonly RealismBand OpenGoalShotRate =
            new RealismBand("openGoalShotRate", 0.90, double.PositiveInfinity);

        public static readonly RealismBand SterilePossessionShare =
            new RealismBand("sterilePossessionShare", double.NegativeInfinity, 0.15);

        /// <summary>Median over matches of each match's median off-target seconds per outfielder.</summary>
        public static readonly RealismBand MedianOffTargetSeconds =
            new RealismBand("off-target s/player (median)", double.NegativeInfinity, 5);

        /// <summary>Milliseconds per match as a multiple of V10's in the same run.</summary>
        public static readonly RealismBand TimeVsV10 =
            new RealismBand("time vs V10", double.NegativeInfinity, 1.25);
    }
}
