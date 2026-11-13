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
        private readonly int _fadePercent, _kneePercent;

        // A pace v that stops in d at a braking rate b is v = sqrt(2 b d): in units per tick that is
        // sqrt(2 b[dm/s^2] x Scale x d[units] / tps^2); a b in cm/s^2 is divided by ten more.
        private readonly int _jogBrake, _settleBrake, _perTick2, _settlePerTick2;

        public PlayerSteering(MatchBalance cfg)
        {
            _approachU = U.Units(cfg.PlayerApproachDm);
            if (_approachU < 1) _approachU = 1;
            int accel = U.PerTickPerTick(cfg.PlayerAccelDmPerSecond2, cfg);
            Accel = accel < 1 ? 1 : accel;
            int brake = U.PerTickPerTick(cfg.PlayerBrakeDmPerSecond2, cfg);
            Brake = brake < Accel ? Accel : brake;
            int jog = U.PerTickPerTick(cfg.PlayerJogGainDmPerSecond2, cfg);
            JogGain = jog < 1 ? 1 : jog;
            int run = U.PerTickPerTick(cfg.PlayerRunGainDmPerSecond2, cfg);
            RunGain = run < 1 ? 1 : run;
            _fadePercent = cfg.PlayerSprintFadePercent;
            _kneePercent = cfg.PlayerSprintFadeKneePercent;
            _jogBrake = 2 * U.Scale * cfg.PlayerJogBrakeDmPerSecond2;
            _settleBrake = 2 * U.Scale * cfg.PlayerSettleWalkCmPerSecond2;
            _perTick2 = cfg.TicksPerSecond * cfg.TicksPerSecond;
            _settlePerTick2 = 10 * _perTick2;
        }

        /// <summary>The most a man who is not sprinting gains in speed in one tick, in units per tick.</summary>
        public int JogGain { get; }

        /// <summary>The same on a positional run (<see cref="Gain"/>).</summary>
        public int RunGain { get; }

        /// <summary>The most a man's velocity can change in one tick, in units per tick.</summary>
        public int Accel { get; }

        /// <summary>The most a man off the ball can slow down in one tick, in units per tick (at least <see cref="Accel"/>).</summary>
        public int Brake { get; }

        /// <summary>A speed in decimetres per second, as units per tick.</summary>
        public static int SpeedPerTick(int dmPerSecond, MatchBalance cfg) => U.PerTick(dmPerSecond, cfg);

        /// <summary>
        /// The velocity a man wants this tick toward a target (dx, dy) away, given the pace he is
        /// allowed (<paramref name="top"/>, at least 1). Returns that pace as the approach left it,
        /// which is also the cap on his actual speed this tick.
        /// </summary>
        public int Desired(int dx, int dy, int top, bool sprint, out int wx, out int wy, bool jog = false)
        {
            long gap = (long)dx * dx + (long)dy * dy;
            int length = 0;

            // He WALKS to a place a few metres away and jogs to one across the pitch. Setting
            // off at a constant jog for a five-metre correction is a kilometre a match of
            // running no footballer does, and it also means a man chasing a spot that jitters
            // can never settle: at walking pace he averages the jitter out instead, which is
            // what actually happens on a pitch (engine phase 2). The pace falls with the gap all
            // the way in, so he slows onto his spot rather than stopping short of it: this easing
            // replaced a 3 m arrival deadband that parked men off their places (R8).
            // Issue #215: a man who has set off for his place (jog) holds his jog until he must
            // brake for it; one who has settled walks at least the pace that stops him gently on it,
            // so he no longer crawls the last metres (58% of the frames at walking pace, real 34-51%).
            if (!sprint && gap < (long)_approachU * _approachU)
            {
                length = U.Length(dx, dy);
                int paced;
                if (jog)
                {
                    long q = (long)_jogBrake * length / _perTick2;
                    // floor(sqrt(q)) >= top  <=>  q >= top*top: skip the root when it cannot win.
                    paced = q >= (long)top * top ? top : MovementGeometry.Sqrt((int)q);
                }
                else
                {
                    paced = top * length / _approachU;
                    long q = (long)_settleBrake * length / _settlePerTick2;
                    // floor(sqrt(q)) > paced  <=>  q >= (paced+1)^2: skip the root when it cannot win.
                    long need = (long)(paced + 1) * (paced + 1);
                    if (q >= need)
                    {
                        int walk = MovementGeometry.Sqrt((int)q);
                        if (walk > paced) paced = walk;
                    }
                }

                if (paced > top) paced = top;
                if (paced < 1) paced = 1;
                top = paced;
            }

            if (gap <= (long)top * top)
            {
                // Within one step of the target the vector IS the step, so no root is needed.
                wx = dx;
                wy = dy;
            }
            else if (length > 0)
            {
                // U.Scaled with the root already taken above: the same step, one root fewer.
                wx = (int)((long)dx * top / length);
                wy = (int)((long)dy * top / length);
            }
            else
            {
                U.Scaled(dx, dy, top, out wx, out wy);
            }

            return top;
        }

        /// <summary>
        /// One tick of inertia: velocity turns toward the wanted one by at most accel, capped at top.
        /// With a <paramref name="brake"/> the cap falls to top no faster than that a tick, so a man
        /// whose eased pace drops sharply turns on a curve instead of pivoting on the spot; with a
        /// <paramref name="gain"/> his speed rises by no more than that a tick (see <see cref="Gain"/>).
        /// <paramref name="speed"/> is his speed when the caller has already measured it.
        /// </summary>
        public static void Accelerate(ref int vx, ref int vy, int wx, int wy, int accel, int top, int brake = int.MaxValue,
            int gain = int.MaxValue, int speed = -1)
        {
            int before = speed >= 0 ? speed : brake == int.MaxValue && gain == int.MaxValue ? 0 : U.Length(vx, vy);
            int ax = wx - vx, ay = wy - vy;
            U.Cap(ref ax, ref ay, accel);
            vx += ax;
            vy += ay;
            int cap = brake != int.MaxValue && before - brake > top ? before - brake : top;
            if (gain != int.MaxValue && before + gain < cap) cap = before + gain;
            U.Cap(ref vx, ref vy, cap);
        }

        /// <summary>
        /// The most a man running at <paramref name="speed"/> (units per tick, his top speed
        /// <paramref name="max"/>) may gain in one tick (issue #215). Sprinting he has his whole
        /// acceleration up to the knee of his top speed and less and less above it, so a short burst
        /// is as sharp as ever but top speed takes a long run to reach; on a positional
        /// <paramref name="run"/> or at a jog he picks up pace under the 2 m/s^2 of a hard effort
        /// (a run fades the same way above the knee).
        /// </summary>
        public int Gain(bool sprint, bool run, int speed, int max)
        {
            int gain = run ? RunGain : sprint ? Accel : JogGain;
            if (sprint && _fadePercent > 0)
            {
                int knee = max * _kneePercent / 100;
                if (max > knee)
                {
                    int over = (speed < max ? speed : max) - knee;
                    if (over > 0)
                    {
                        int left = 100 - _fadePercent * over / (max - knee);
                        gain = gain * left / 100;
                    }
                }
            }

            return gain < 1 ? 1 : gain;
        }
    }
}

