namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The ball's height (R7 of believable-match). Since issue #178 the simulation reads it: an
    /// outfield man touches only a ball no higher than a header (free-ball spec R3). It draws no
    /// random number: every flight is derived from the kick that started it and from gravity.
    /// </summary>
    internal sealed partial class MatchBall
    {
        private const int Milli = 1000;

        /// <summary>9.81 m/s², in thousandths of a decimetre per second squared.</summary>
        internal const int GravityMilliDmPerSecond2 = 98_100;

        /// <summary>Where a cross is aimed: a forehead, not a boot.</summary>
        public const int HeadHeightDm = 18;

        /// <summary>A header is aimed down; anything met above the knee counts as one here.</summary>
        private const int HeadedFromDm = 9;
        private const int HeadedArrivalDm = 3;

        /// <summary>
        /// A strike from the edge of the box arrives low, and one from further out rises by a
        /// decimetre for every metre beyond it, up to just under the bar.
        /// </summary>
        private const int LowShotDm = 4;
        private const int LowShotFromDm = 160;
        private const int HighestShotDm = 22;

        /// <summary>
        /// The longest a lofted ball hangs. The 2D ball may take longer to reach its target; past
        /// this it has landed and is running on, which keeps a sixty-metre hoof below a rooftop.
        /// </summary>
        private const int LongestHangMs = 3000;

        private int _gravity;      // milli-dm per tick per tick
        private int _longestHang;  // ticks
        private int _z, _vz;       // milli-dm, milli-dm per tick

        /// <summary>Height above the grass, in decimetres.</summary>
        public int Z => _z / Milli;

        /// <summary>Its height <paramref name="ticks"/> ticks on if nobody touches it, in decimetres (0 once it has come down).</summary>
        public int ZAfter(int ticks)
        {
            if (_z == 0 && _vz <= 0) return 0;
            long z = _z + (long)ticks * _vz - (long)_gravity * ticks * (ticks - 1) / 2;
            return z <= 0 ? 0 : (int)(z / Milli);
        }

        private void InitHeight(int ticksPerSecond)
        {
            if (ticksPerSecond < 1) ticksPerSecond = 1;
            _gravity = GravityMilliDmPerSecond2 / (ticksPerSecond * ticksPerSecond);
            if (_gravity < 1) _gravity = 1;
            _longestHang = LongestHangMs * ticksPerSecond / 1000;
        }

        /// <summary>
        /// Lifts the ball just struck so it comes down to <paramref name="arriveDm"/> as it covers
        /// <paramref name="distance"/>. Struck from where it was met, so a ball headed on starts at
        /// head height.
        /// </summary>
        public void Loft(int distance, int arriveDm)
        {
            int ticks = TicksToCover(distance, Speed);
            if (ticks > _longestHang) ticks = _longestHang;
            if (ticks < 1) ticks = 1;

            // The discrete flight is z(n) = z0 + n*v0 - g*n*(n-1)/2; solved for z(ticks) = arrive.
            _vz = (arriveDm * Milli - _z) / ticks + _gravity * (ticks - 1) / 2;
        }

        /// <summary>A strike at goal from <paramref name="distance"/>: low from close in, rising from range, down off the head.</summary>
        public void LoftShot(int distance)
        {
            int arrive = LowShotDm + (U.Dm(distance) - LowShotFromDm) / 10;
            if (arrive < LowShotDm) arrive = LowShotDm;
            if (arrive > HighestShotDm) arrive = HighestShotDm;
            if (Z >= HeadedFromDm) arrive = HeadedArrivalDm;
            Loft(distance, arrive);
        }

        /// <summary>Struck or cushioned without being lifted: it keeps its height and gravity takes it.</summary>
        private void Drive() => _vz = 0;

        private void Ground()
        {
            _z = 0;
            _vz = 0;
        }

        private void StepHeight()
        {
            if (_z == 0 && _vz <= 0) return;

            _z += _vz;
            _vz -= _gravity;
            if (_z > 0) return;

            Ground();
            Heard(BallEventKind.Bounce, LastTouchSide, LastTouchSlot);
        }
    }
}
