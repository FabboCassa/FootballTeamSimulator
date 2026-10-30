namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The crosses of one match by geometry (free-ball-real-movement spec R2), per side, beside the
    /// passes the engine itself labels a cross. See <see cref="GeometricCrossAnalyzer"/>.
    /// </summary>
    public sealed class GeometricCrossMetrics
    {
        public int HomeHigh { get; set; }
        public int HomeLow { get; set; }
        public int AwayHigh { get; set; }
        public int AwayLow { get; set; }

        /// <summary>The stream's own <see cref="BallActionKind.Cross"/> actions, the label the geometry replaces.</summary>
        public int HomeLabelled { get; set; }
        public int AwayLabelled { get; set; }

        public int High => HomeHigh + AwayHigh;
        public int Low => HomeLow + AwayLow;
        public int Labelled => HomeLabelled + AwayLabelled;
    }
}
