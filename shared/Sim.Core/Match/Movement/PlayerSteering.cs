using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// A player's legs: the velocity he wants toward a target, and the inertia that turns his
    /// actual velocity toward it. Integer units — sixteenths of a decimetre, per tick — so the
    /// stream stays bit-identical on every runtime.
    /// </summary>
    public readonly struct PlayerSteering
    {
        /// <summary>Movement units in one decimetre.</summary>
        public const int UnitsPerDm = U.Scale;

        private readonly int _approachU;

        public PlayerSteering(MatchBalance cfg)
        {
            _approachU = U.Units(cfg.PlayerApproachDm);
            if (_approachU < 1) _approachU = 1;
            int accel = U.PerTickPerTick(cfg.PlayerAccelDmPerSecond2, cfg);
            Accel = accel < 1 ? 1 : accel;
        }

        /// <summary>The most a man's velocity can change in one tick, in units per tick.</summary>
        public int Accel { get; }

        /// <summary>A speed in decimetres per second, as units per tick.</summary>
        public static int SpeedPerTick(int dmPerSecond, MatchBalance cfg) => U.PerTick(dmPerSecond, cfg);

        /// <summary>
        /// The velocity a man wants this tick toward a target (dx, dy) away, given the pace he is
        /// allowed (<paramref name="top"/>, at least 1). Returns that pace as the approach left it,
        /// which is also the cap on his actual speed this tick.
        /// </summary>
        public int Desired(int dx, int dy, int top, bool sprint, out int wx, out int wy)
        {
            long gap = (long)dx * dx + (long)dy * dy;

            // He WALKS to a place a few metres away and jogs to one across the pitch. Setting
            // off at a constant jog for a five-metre correction is a kilometre a match of
            // running no footballer does, and it also means a man chasing a spot that jitters
            // can never settle: at walking pace he averages the jitter out instead, which is
            // what actually happens on a pitch (engine phase 2). The pace falls with the gap all
            // the way in, so he slows onto his spot rather than stopping short of it: this easing
            // replaced a 3 m arrival deadband that parked men off their places (R8).
            if (!sprint && gap < (long)_approachU * _approachU)
            {
                int near = U.Length(dx, dy);
                int paced = top * near / _approachU;
                if (paced < 1) paced = 1;
                top = paced;
            }

            if (gap <= (long)top * top)
            {
                // Within one step of the target the vector IS the step, so no root is needed.
                wx = dx;
                wy = dy;
            }
            else
            {
                U.Scaled(dx, dy, top, out wx, out wy);
            }

            return top;
        }

        /// <summary>One tick of inertia: velocity turns toward the wanted one by at most accel, capped at top.</summary>
        public static void Accelerate(ref int vx, ref int vy, int wx, int wy, int accel, int top)
        {
            int ax = wx - vx, ay = wy - vy;
            U.Cap(ref ax, ref ay, accel);
            vx += ax;
            vy += ay;
            U.Cap(ref vx, ref vy, top);
        }
    }
}
