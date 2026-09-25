using System;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// R2 of the watchable-match spec: how long each outfielder spends more than 25 m from his
    /// phase target while neither chasing the ball nor marking a man.
    ///
    /// The stream does not carry the brain's targets, so the target is read back from the play
    /// itself: the phase is which side last held the ball (in possession / out of it), and a
    /// man's target is the centroid of his outfield team-mates plus his own median offset from
    /// that centroid in that phase — where he usually stands relative to the block. Excluding
    /// him from the centroid keeps his own wandering from dragging the target after him.
    ///
    ///   * chasing: he holds the ball, or he is his side's outfielder nearest to it (ties to the
    ///     lower slot);
    ///   * marking: an opponent is within 3 m of him.
    ///
    /// Keepers and men already sent off are left out. The reading of a match is the median over
    /// the outfielders of both sides. Buffers are reused between matches.
    /// </summary>
    internal sealed class OffTargetMeter
    {
        private const int OffTargetDm = 250;
        private const int MarkingDm = 30;
        private const int Unknown = -1;

        private int _n;
        private int _ticks;
        private int[] _phase = Array.Empty<int>();      // per frame: side that last held the ball, or Unknown
        private int[] _offX = Array.Empty<int>();       // per (side, slot, frame): offset from team-mates' centroid
        private int[] _offY = Array.Empty<int>();
        private int[] _scratch = Array.Empty<int>();
        private int[] _medianX = Array.Empty<int>();    // per (side, slot, phase)
        private int[] _medianY = Array.Empty<int>();
        private int[] _frames = Array.Empty<int>();     // per (side, slot): frames off target
        private int _ticksPerMinute = 1;

        public double Seconds(bool home, int slot) =>
            slot < 0 || slot >= _n ? 0 : _frames[(home ? 0 : _n) + slot] * 60.0 / _ticksPerMinute;

        /// <summary>Measures one match and returns the median off-target seconds over the outfielders.</summary>
        public double Measure(PositionStream s, int ticks, int[] keeper, int[] sentOffFrom)
        {
            Ensure(s.PlayerCount, ticks);
            _ticksPerMinute = s.TicksPerMinute > 0 ? s.TicksPerMinute : 1;
            TrackPhase(s);
            StoreOffsets(s, keeper, sentOffFrom);
            TakeMedians(keeper, sentOffFrom);
            CountFrames(s, keeper, sentOffFrom);
            return MedianOverOutfielders(keeper);
        }

        private void Ensure(int n, int ticks)
        {
            _n = n;
            _ticks = ticks;
            if (_phase.Length < ticks) _phase = new int[ticks];
            if (_offX.Length < 2 * n * ticks)
            {
                _offX = new int[2 * n * ticks];
                _offY = new int[2 * n * ticks];
            }

            if (_scratch.Length < ticks) _scratch = new int[ticks];
            if (_frames.Length != 2 * n)
            {
                _frames = new int[2 * n];
                _medianX = new int[2 * n * 2];
                _medianY = new int[2 * n * 2];
            }

            Array.Clear(_frames, 0, _frames.Length);
        }

        private void TrackPhase(PositionStream s)
        {
            int last = Unknown;
            for (int t = 0; t < _ticks; t++)
            {
                int code = s.Owner[t];
                if (code != PositionStream.NoOwner)
                {
                    s.TryOwner(code, out bool home, out int _);
                    last = home ? 0 : 1;
                }

                _phase[t] = last;
            }
        }

        private bool Active(int side, int slot, int t, int[] keeper, int[] sentOffFrom) =>
            slot != keeper[side] && t < sentOffFrom[side * _n + slot];

        private int Index(int side, int slot, int t) => (side * _n + slot) * _ticks + t;

        private void StoreOffsets(PositionStream s, int[] keeper, int[] sentOffFrom)
        {
            for (int side = 0; side < 2; side++)
            {
                int[] xy = side == 0 ? s.HomeXY : s.AwayXY;
                for (int t = 0; t < _ticks; t++)
                {
                    long sumX = 0, sumY = 0;
                    int count = 0;
                    for (int k = 0; k < _n; k++)
                    {
                        if (!Active(side, k, t, keeper, sentOffFrom)) continue;
                        sumX += s.PlayerX(xy, t, k);
                        sumY += s.PlayerY(xy, t, k);
                        count++;
                    }

                    for (int k = 0; k < _n; k++)
                    {
                        if (!Active(side, k, t, keeper, sentOffFrom)) continue;
                        int x = s.PlayerX(xy, t, k), y = s.PlayerY(xy, t, k);
                        _offX[Index(side, k, t)] = count < 2 ? 0 : x - (int)((sumX - x) / (count - 1));
                        _offY[Index(side, k, t)] = count < 2 ? 0 : y - (int)((sumY - y) / (count - 1));
                    }
                }
            }
        }

        private int MedianKey(int side, int slot, int inPossession) => (side * _n + slot) * 2 + inPossession;

        private void TakeMedians(int[] keeper, int[] sentOffFrom)
        {
            for (int side = 0; side < 2; side++)
            for (int k = 0; k < _n; k++)
            for (int held = 0; held < 2; held++)
            {
                int key = MedianKey(side, k, held);
                int until = sentOffFrom[side * _n + k];
                _medianX[key] = k == keeper[side] ? 0 : Median(_offX, side, k, held, until);
                _medianY[key] = k == keeper[side] ? 0 : Median(_offY, side, k, held, until);
            }
        }

        /// <summary>Lower median of one man's offsets over the frames of one phase, up to his sending-off.</summary>
        private int Median(int[] offsets, int side, int slot, int held, int until)
        {
            int count = 0;
            for (int t = 0; t < _ticks && t < until; t++)
            {
                if (_phase[t] == Unknown || (_phase[t] == side ? 1 : 0) != held) continue;
                _scratch[count++] = offsets[Index(side, slot, t)];
            }

            if (count == 0) return 0;
            Array.Sort(_scratch, 0, count);
            return _scratch[(count - 1) / 2];
        }

        private void CountFrames(PositionStream s, int[] keeper, int[] sentOffFrom)
        {
            for (int t = 0; t < _ticks; t++)
            {
                if (_phase[t] == Unknown) continue;
                int bx = s.BallXY[t * 2], by = s.BallXY[t * 2 + 1];

                for (int side = 0; side < 2; side++)
                {
                    int[] xy = side == 0 ? s.HomeXY : s.AwayXY;
                    int[] foes = side == 0 ? s.AwayXY : s.HomeXY;
                    int held = _phase[t] == side ? 1 : 0;
                    int chaser = Chaser(s, xy, side, t, bx, by, keeper, sentOffFrom);

                    for (int k = 0; k < _n; k++)
                    {
                        if (k == chaser || !Active(side, k, t, keeper, sentOffFrom)) continue;
                        // target - position = (centroid + median offset) - (centroid + offset now)
                        int key = MedianKey(side, k, held);
                        long dx = _medianX[key] - _offX[Index(side, k, t)];
                        long dy = _medianY[key] - _offY[Index(side, k, t)];
                        if (dx * dx + dy * dy <= (long)OffTargetDm * OffTargetDm) continue;
                        if (Marking(s, foes, 1 - side, t, s.PlayerX(xy, t, k), s.PlayerY(xy, t, k), sentOffFrom)) continue;
                        _frames[side * _n + k]++;
                    }
                }
            }
        }

        private int Chaser(PositionStream s, int[] xy, int side, int t, int bx, int by, int[] keeper, int[] sentOffFrom)
        {
            int code = s.Owner[t];
            if (code != PositionStream.NoOwner)
            {
                s.TryOwner(code, out bool home, out int slot);
                if ((home ? 0 : 1) == side) return slot;
            }

            int best = -1;
            long bestD = long.MaxValue;
            for (int k = 0; k < _n; k++)
            {
                if (!Active(side, k, t, keeper, sentOffFrom)) continue;
                long dx = s.PlayerX(xy, t, k) - bx, dy = s.PlayerY(xy, t, k) - by;
                long d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }

            return best;
        }

        private bool Marking(PositionStream s, int[] foes, int foeSide, int t, int x, int y, int[] sentOffFrom)
        {
            for (int k = 0; k < _n; k++)
            {
                if (t >= sentOffFrom[foeSide * _n + k]) continue;
                long dx = s.PlayerX(foes, t, k) - x, dy = s.PlayerY(foes, t, k) - y;
                if (dx * dx + dy * dy <= (long)MarkingDm * MarkingDm) return true;
            }

            return false;
        }

        private double MedianOverOutfielders(int[] keeper)
        {
            var values = new double[2 * _n];
            int count = 0;
            for (int side = 0; side < 2; side++)
            for (int k = 0; k < _n; k++)
                if (k != keeper[side]) values[count++] = Seconds(side == 0, k);

            if (count == 0) return 0;
            Array.Sort(values, 0, count);
            return count % 2 == 1 ? values[count / 2] : (values[count / 2 - 1] + values[count / 2]) / 2;
        }
    }
}
