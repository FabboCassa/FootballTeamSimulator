using System;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The striker offside-position counts of one match (free-ball spec R2), both sides together;
    /// see <see cref="StrikerOffsideAnalyzer"/> for the definitions. Raw counts, so matches pool by
    /// adding them (<see cref="Plus"/>).
    /// </summary>
    public sealed class StrikerOffsideMetrics
    {
        public int Matches { get; set; } = 1;

        /// <summary>Striker-frames of open play with his team in possession: the share's denominator.</summary>
        public long PossessionFrames { get; set; }

        /// <summary>Of those, the frames he stood in an offside position.</summary>
        public long OffsideFrames { get; set; }

        /// <summary>Offsides the referee called, both sides.</summary>
        public int Offsides { get; set; }

        public double OffsidePercent => PossessionFrames == 0 ? 0 : 100.0 * OffsideFrames / PossessionFrames;

        public double OffsidesPerTeamMatch => Matches == 0 ? 0 : Offsides / (2.0 * Matches);

        public StrikerOffsideMetrics Plus(StrikerOffsideMetrics o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            return new StrikerOffsideMetrics
            {
                Matches = Matches + o.Matches,
                PossessionFrames = PossessionFrames + o.PossessionFrames,
                OffsideFrames = OffsideFrames + o.OffsideFrames,
                Offsides = Offsides + o.Offsides
            };
        }
    }
}
