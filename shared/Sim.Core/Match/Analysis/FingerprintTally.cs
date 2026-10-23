namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Pools the home side's R19 readings over matches (<see cref="StyleFingerprints"/>). Counts are
    /// summed and read as pooled ratios, as the doc's §1.5 asks. A measuring instrument: it reads
    /// finished reports and draws nothing.
    /// </summary>
    public sealed class FingerprintTally
    {
        private readonly ShotPassAnalyzer _analyzer = new ShotPassAnalyzer();
        private ShotPassMetrics? _home;
        private long _awayPasses, _lineSumDm, _defendingFrames;

        public int Matches => _home?.Matches ?? 0;

        /// <summary>Adds the home side of one match. A report with no stream is skipped.</summary>
        public void Add(MatchReport report)
        {
            ShotPassMetrics? m = _analyzer.MeasureSide(report, home: true);
            ShotPassMetrics? away = _analyzer.MeasureSide(report, home: false);
            if (m == null || away == null || report.Positions == null) return;
            _home = _home == null ? m : _home.Plus(m);
            _awayPasses += away.PassesAttempted;

            PositionStream s = report.Positions;
            s.Unpack();
            for (int t = 0; t < s.Owner.Length; t++)
            {
                if (!s.TryOwner(s.Owner[t], out bool home, out int _) || home) continue;
                _lineSumDm += LastLineDm(s, t);
                _defendingFrames++;
            }
        }

        public void Merge(FingerprintTally o)
        {
            if (o._home != null) _home = _home == null ? o._home : _home.Plus(o._home);
            _awayPasses += o._awayPasses;
            _lineSumDm += o._lineSumDm;
            _defendingFrames += o._defendingFrames;
        }

        public FingerprintReading Reading()
        {
            ShotPassMetrics m = _home ?? new ShotPassMetrics { Matches = 0 };
            double matches = m.Matches;
            return new FingerprintReading
            {
                PassesPerTeam = matches == 0 ? 0 : m.PassesAttempted / matches,
                PossessionPercent = m.PassesAttempted + _awayPasses == 0
                    ? 0
                    : 100.0 * m.PassesAttempted / (m.PassesAttempted + _awayPasses),
                PassesPerSequence = m.PassesPerSequence,
                TenPlusSequencesPerTeam = matches == 0 ? 0 : m.TenPlusSequences / matches,
                DirectSpeed = m.DirectSpeed,
                Ppda = m.Ppda,
                CrossesPerTeam = matches == 0 ? 0 : m.Crosses / matches,
                LongBallSharePercent = m.LongBallSharePercent,
                LineHeightM = _defendingFrames == 0 ? 0 : _lineSumDm / (double)_defendingFrames / 10.0,
                HighTurnoversPerTeam = matches == 0 ? 0 : m.HighTurnovers / matches,
            };
        }

        /// <summary>The home side's last line: its second-deepest man, the keeper being the deepest. Home defends x = 0.</summary>
        private static int LastLineDm(PositionStream s, int t)
        {
            int deepest = int.MaxValue, second = int.MaxValue;
            for (int slot = 0; slot < s.PlayerCount; slot++)
            {
                int x = s.HomeAt(t, slot).X;
                if (x < deepest)
                {
                    second = deepest;
                    deepest = x;
                }
                else if (x < second) second = x;
            }

            return second;
        }
    }
}
