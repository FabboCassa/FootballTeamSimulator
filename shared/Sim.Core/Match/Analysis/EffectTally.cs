namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// A treatment against its baseline over the same fixtures (same seeds): the two means and the
    /// difference. The effect measurements of R9–R11 are all read this way.
    /// </summary>
    public sealed class PairedEffect
    {
        private double _treatment, _baseline;

        public int Count { get; private set; }

        public double TreatmentMean => Count == 0 ? 0 : _treatment / Count;

        public double BaselineMean => Count == 0 ? 0 : _baseline / Count;

        public double Delta => TreatmentMean - BaselineMean;

        /// <summary>The change relative to the baseline, in percent (0 when the baseline reads 0).</summary>
        public double ChangePercent => BaselineMean == 0 ? 0 : 100 * Delta / BaselineMean;

        public void Add(double treatment, double baseline)
        {
            Count++;
            _treatment += treatment;
            _baseline += baseline;
        }
    }

    /// <summary>A reading averaged separately for each <see cref="ScoreState"/>.</summary>
    public sealed class StateTally
    {
        private const int States = 3;

        private readonly double[] _sum = new double[States];
        private readonly int[] _count = new int[States];

        public void Add(ScoreState state, double value)
        {
            _sum[(int)state] += value;
            _count[(int)state]++;
        }

        public int Count(ScoreState state) => _count[(int)state];

        public double Mean(ScoreState state) => _count[(int)state] == 0 ? 0 : _sum[(int)state] / _count[(int)state];
    }
}
