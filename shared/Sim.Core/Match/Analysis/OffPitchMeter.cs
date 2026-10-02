using System;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// R5 of the real-match spec: outfielder-frames more than 1 m outside a touchline or goal line.
    /// Fed one frame at a time; allocation-free per frame, buffers reused between matches.
    ///
    /// An excursion is an unbroken run of frames one active outfielder spends outside. The whole
    /// excursion is exempt when:
    ///   * he is the throw-in or corner taker: some frame of it falls between the whistle for his
    ///     restart and the frame he has the ball (convention: walking back on after the throw is
    ///     part of the same excursion);
    ///   * it is a ball carry: he held the ball in the frame before it began or during it, and it
    ///     lasted under 2 s.
    /// Every frame of any other excursion counts. Keepers and men sent off are left out.
    /// </summary>
    internal sealed class OffPitchMeter
    {
        private const int MarginDm = 10;
        private const int CarrySeconds = 2;

        private int _n;
        private int _carryFrames;
        private int[] _start = Array.Empty<int>();      // per (side, slot): first frame outside, or -1
        private bool[] _carry = Array.Empty<bool>();
        private bool[] _taker = Array.Empty<bool>();

        public long Frames { get; private set; }

        public void Begin(int playerCount, int ticksPerMinute)
        {
            _n = playerCount;
            _carryFrames = Math.Max(1, CarrySeconds * ticksPerMinute / 60);
            if (_start.Length != 2 * playerCount)
            {
                _start = new int[2 * playerCount];
                _carry = new bool[2 * playerCount];
                _taker = new bool[2 * playerCount];
            }

            for (int i = 0; i < _start.Length; i++) _start[i] = -1;
            Frames = 0;
        }

        /// <param name="takerCode">Owner code of the throw-in or corner taker while his restart is pending (and on the frame he takes it), else <see cref="PositionStream.NoOwner"/>.</param>
        public void Frame(PositionStream s, int t, int takerCode, int[] keeper, int[] sentOffFrom)
        {
            for (int side = 0; side < 2; side++)
            {
                int[] xy = side == 0 ? s.HomeXY : s.AwayXY;
                for (int k = 0; k < _n; k++)
                {
                    int i = side * _n + k;
                    bool active = k != keeper[side] && t < sentOffFrom[i];
                    if (!active || !Outside(s.PlayerX(xy, t, k), s.PlayerY(xy, t, k)))
                    {
                        if (_start[i] >= 0) Close(i, t);
                        continue;
                    }

                    int code = s.OwnerCode(side == 0, k);
                    if (_start[i] < 0)
                    {
                        _start[i] = t;
                        _carry[i] = t > 0 && s.Owner[t - 1] == code;
                        _taker[i] = false;
                    }

                    if (s.Owner[t] == code) _carry[i] = true;
                    if (takerCode == code) _taker[i] = true;
                }
            }
        }

        /// <summary>Closes the excursions still open at the final whistle.</summary>
        public void End(int ticks)
        {
            for (int i = 0; i < _start.Length; i++)
                if (_start[i] >= 0) Close(i, ticks);
        }

        private void Close(int i, int end)
        {
            int length = end - _start[i];
            bool exempt = _taker[i] || (_carry[i] && length < _carryFrames);
            if (!exempt) Frames += length;
            _start[i] = -1;
        }

        private static bool Outside(int x, int y) =>
            x < -MarginDm || x > Pitch.LengthDm + MarginDm || y < -MarginDm || y > Pitch.WidthDm + MarginDm;
    }
}
