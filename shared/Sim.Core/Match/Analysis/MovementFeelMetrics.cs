using System;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The movement-feel counts of one match (believable-match spec R1/R2), open play, outfielders of
    /// both sides (see <see cref="MovementFeelAnalyzer"/> for the definitions). Raw counts only, so
    /// matches pool by adding them (<see cref="Plus"/>); the median of R comes off a histogram.
    /// </summary>
    public sealed class MovementFeelMetrics
    {
        /// <summary>R is binned to the nearest thousandth: bin i holds R ≈ i ÷ 1000.</summary>
        public const int AgreementBins = 1001;
        /// <summary>&lt; 0.2, 0.2-2, 2-4, 4-5.5, 5.5-7 and &gt; 7 m/s.</summary>
        public const int SpeedBands = 6;

        private readonly long[] _agreement = new long[AgreementBins];
        private readonly long[] _speed = new long[SpeedBands];

        public int Matches { get; set; } = 1;

        /// <summary>Team 1 s steps with at least six outfielders moving over 1 m/s.</summary>
        public long AgreementWindows { get; private set; }
        public long AgreementAbove09Windows { get; private set; }
        public long Reversals { get; set; }
        public long Accelerations { get; set; }

        /// <summary>Outfielder open-play frames with a speed: the speed-band denominator.</summary>
        public long PlayerFrames { get; private set; }
        public double PlayerMinutes { get; set; }

        public void AddWindow(double r)
        {
            int bin = (int)Math.Round(r * (AgreementBins - 1));
            _agreement[Math.Max(0, Math.Min(AgreementBins - 1, bin))]++;
            AgreementWindows++;
            if (r > 0.9) AgreementAbove09Windows++;
        }

        public void AddSpeedFrame(int band)
        {
            _speed[band]++;
            PlayerFrames++;
        }

        /// <summary>Median R over every window, from the thousandth bins (lower and upper middle averaged).</summary>
        public double AgreementMedian
        {
            get
            {
                if (AgreementWindows == 0) return 0;
                return (Quantile((AgreementWindows - 1) / 2) + Quantile(AgreementWindows / 2)) / 2.0 / (AgreementBins - 1);
            }
        }

        public double AgreementAbove09Percent => Percent(AgreementAbove09Windows, AgreementWindows);
        public double ReversalsPerPlayerMinute => PlayerMinutes <= 0 ? 0 : Reversals / PlayerMinutes;
        public double AccelerationsPerPlayerMinute => PlayerMinutes <= 0 ? 0 : Accelerations / PlayerMinutes;
        public double SpeedPercent(int band) => Percent(_speed[band], PlayerFrames);

        /// <summary>A new reading holding both: every count added, matches included.</summary>
        public MovementFeelMetrics Plus(MovementFeelMetrics o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            var sum = new MovementFeelMetrics
            {
                Matches = Matches + o.Matches,
                AgreementWindows = AgreementWindows + o.AgreementWindows,
                AgreementAbove09Windows = AgreementAbove09Windows + o.AgreementAbove09Windows,
                Reversals = Reversals + o.Reversals,
                Accelerations = Accelerations + o.Accelerations,
                PlayerFrames = PlayerFrames + o.PlayerFrames,
                PlayerMinutes = PlayerMinutes + o.PlayerMinutes
            };
            for (int i = 0; i < AgreementBins; i++) sum._agreement[i] = _agreement[i] + o._agreement[i];
            for (int i = 0; i < SpeedBands; i++) sum._speed[i] = _speed[i] + o._speed[i];
            return sum;
        }

        /// <summary>The bin holding the window of 0-based rank <paramref name="rank"/>.</summary>
        private int Quantile(long rank)
        {
            long seen = 0;
            for (int i = 0; i < AgreementBins; i++)
            {
                seen += _agreement[i];
                if (seen > rank) return i;
            }

            return AgreementBins - 1;
        }

        private static double Percent(long part, long whole) => whole == 0 ? 0 : 100.0 * part / whole;
    }
}
