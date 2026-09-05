using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The ball as an OBJECT (task 13.2): a position, a velocity and friction. Nothing
    /// teleports it and nothing steers it in mid-air — it is kicked, it runs down, and it
    /// is either collected or it crosses a line and the game restarts. That is where
    /// throw-ins, corners, goal kicks and goals come from: they are DETECTED, not written
    /// into a script, which is why they land where the ball actually went out.
    ///
    /// Because it slows down, a pass can be read and cut out: the whole of the passing
    /// model rests on <see cref="TicksToCover"/> — how long the ball needs to reach a
    /// point against how long a defender needs to get there.
    ///
    /// PHASE 1 OF THE ENGINE REWORK put the ball on real units. Friction is written per
    /// SECOND in <see cref="MatchBalance.BallSpeedKeptPermillePerSecond"/> and the per-tick
    /// figure is derived from the tick rate, so the ball decelerates the same way whatever
    /// the simulation's frequency. It is multiplicative rather than a flat subtraction on
    /// purpose: a real ball is slowed by rolling resistance AND by air, so it sheds roughly
    /// seven metres per second squared at twenty-five metres a second and less than one at
    /// walking pace — which is what an exponential gives and a constant does not.
    ///
    /// The three questions the passing model asks — how far does a ball struck this hard
    /// go, how long does it take, how hard must it be struck — are answered from ONE
    /// cumulative table built in the constructor, not by iterating a recurrence per
    /// opponent per candidate pass. At 10 Hz that difference is the whole cost of a match.
    /// </summary>
    internal sealed class MatchBall
    {
        /// <summary>A ball is unreachable at this many ticks: it will never get there.</summary>
        public const int Unreachable = 9999;

        /// <summary>Speed kept per tick, in permille. Derived from the per-second figure.</summary>
        private readonly int _frictionPermille;

        /// <summary>A ball slower than this has stopped, in units per tick.</summary>
        private readonly int _restSpeed;

        /// <summary>
        /// Ticks tracked by <see cref="_cumulative"/>. Long enough for any ball that is still
        /// worth predicting: past this it is a ball rolling out of play, not a pass.
        /// </summary>
        private readonly int _trackedTicks;

        /// <summary>
        /// _cumulative[n] = 1000 * (1 + k + ... + k^(n-1)), so a ball struck at force F has
        /// covered F * _cumulative[n] / 1000 after n ticks. Monotone, which is what lets
        /// <see cref="TicksToCover"/> be a binary search instead of a loop.
        /// </summary>
        private readonly int[] _cumulative;

        /// <summary>Speed left after n ticks, in permille of the force it was struck with.</summary>
        private readonly int[] _remaining;

        /// <summary>The longest flight the passing model will consider.</summary>
        private readonly int _maxFlightTicks;

        public int X, Y;      // units
        public int Vx, Vy;    // units per tick

        /// <summary>Where it was before the last step, so a line crossing can be found ON the line.</summary>
        public int PrevX, PrevY;

        /// <summary>-1 when the ball is running free; otherwise the side and slot holding it.</summary>
        public int OwnerSide = -1;
        public int OwnerSlot = -1;

        /// <summary>Who touched it last, so a restart can be awarded against them.</summary>
        public int LastTouchSide = -1;
        public int LastTouchSlot = -1;

        /// <summary>True while the ball is out of play waiting to be put back.</summary>
        public bool Dead;

        public bool Free => OwnerSide < 0;

        public MatchBall(MatchBalance cfg)
        {
            int perSecond = cfg.BallSpeedKeptPermillePerSecond;
            if (perSecond < 1) perSecond = 1;
            if (perSecond > 999) perSecond = 999;

            _frictionPermille = PerTickRetention(perSecond, cfg.TicksPerSecond);
            _restSpeed = cfg.BallRestSpeedDmPerSecond * U.Scale / cfg.TicksPerSecond;
            if (_restSpeed < 1) _restSpeed = 1;

            _maxFlightTicks = cfg.MaxFlightTicks;
            _trackedTicks = cfg.BallTrackedSeconds * cfg.TicksPerSecond;
            if (_trackedTicks < _maxFlightTicks) _trackedTicks = _maxFlightTicks;

            _cumulative = new int[_trackedTicks + 1];
            _remaining = new int[_trackedTicks + 1];
            long sum = 0;
            int term = 1000;
            for (int n = 0; n <= _trackedTicks; n++)
            {
                _cumulative[n] = sum > int.MaxValue ? int.MaxValue : (int)sum;
                _remaining[n] = term;
                sum += term;
                term = term * _frictionPermille / 1000;
            }
        }

        /// <summary>
        /// The per-tick retention whose <paramref name="ticksPerSecond"/>-th power is closest to
        /// the per-second one. Integer binary search on the very recurrence the simulation runs,
        /// so the answer is identical on .NET, Mono and IL2CPP — no root, no logarithm, no float.
        /// </summary>
        private static int PerTickRetention(int perSecond, int ticksPerSecond)
        {
            if (ticksPerSecond <= 1) return perSecond;

            int lo = perSecond, hi = 1000;   // the per-tick figure is never below the per-second one
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (Power(mid, ticksPerSecond) >= perSecond) hi = mid;
                else lo = mid + 1;
            }

            return lo;
        }

        /// <summary>permille^n, folded through the same truncating integer step the ball takes.</summary>
        private static int Power(int permille, int n)
        {
            int value = 1000;
            for (int i = 0; i < n; i++) value = value * permille / 1000;
            return value;
        }

        public void Place(int x, int y)
        {
            X = U.ClampX(x);
            Y = U.ClampY(y);
            PrevX = X;
            PrevY = Y;
            Vx = 0;
            Vy = 0;
        }

        /// <summary>
        /// Where the ball actually crossed a line, interpolated between the last two steps.
        /// Reading the position AFTER the step instead is how a shot aimed inside the posts
        /// gets called a goal kick: by then it is a metre past the line and a metre wider.
        /// </summary>
        public void CrossingOfX(int boundary, out int y)
        {
            int span = X - PrevX;
            y = span == 0 ? Y : PrevY + (int)((long)(Y - PrevY) * (boundary - PrevX) / span);
        }

        public void CrossingOfY(int boundary, out int x)
        {
            int span = Y - PrevY;
            x = span == 0 ? X : PrevX + (int)((long)(X - PrevX) * (boundary - PrevY) / span);
        }

        public void Kick(int side, int slot, int dirX, int dirY, int force)
        {
            U.Scaled(dirX, dirY, force, out Vx, out Vy);
            OwnerSide = -1;
            OwnerSlot = -1;
            LastTouchSide = side;
            LastTouchSlot = slot;
            Dead = false;
        }

        public void Take(int side, int slot)
        {
            OwnerSide = side;
            OwnerSlot = slot;
            LastTouchSide = side;
            LastTouchSlot = slot;
            Vx = 0;
            Vy = 0;
            Dead = false;
        }

        /// <summary>Rolls the ball on one tick. Positions are NOT clamped: crossing a line is the point.</summary>
        public void Advance()
        {
            if (Dead || !Free) return;

            PrevX = X;
            PrevY = Y;
            X += Vx;
            Y += Vy;
            Vx = Vx * _frictionPermille / 1000;
            Vy = Vy * _frictionPermille / 1000;

            if (U.Length(Vx, Vy) < _restSpeed)
            {
                Vx = 0;
                Vy = 0;
            }
        }

        public int Speed => U.Length(Vx, Vy);

        /// <summary>How far a ball struck at <paramref name="force"/> rolls in <paramref name="ticks"/> ticks.</summary>
        public int RangeInTicks(int force, int ticks)
        {
            if (ticks <= 0 || force <= 0) return 0;
            if (ticks > _trackedTicks) ticks = _trackedTicks;
            return (int)((long)force * _cumulative[ticks] / 1000);
        }

        /// <summary>
        /// How far a ball struck at <paramref name="force"/> can be DELIVERED — the ground it
        /// covers inside the longest flight the passing model will wait for. The asymptotic roll
        /// is longer, but a ball still trickling four seconds later has not been passed to anyone.
        /// </summary>
        public int RangeOf(int force) => RangeInTicks(force, _maxFlightTicks);

        /// <summary>
        /// Ticks for a ball struck at <paramref name="force"/> to roll <paramref name="distance"/>,
        /// or <see cref="Unreachable"/> if it runs out of legs first. Binary search on the
        /// cumulative table, which is monotone by construction.
        /// </summary>
        public int TicksToCover(int distance, int force)
        {
            if (distance <= 0) return 0;
            if (force <= 0) return Unreachable;
            if ((long)force * _cumulative[_trackedTicks] / 1000 < distance) return Unreachable;

            int lo = 1, hi = _trackedTicks;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if ((long)force * _cumulative[mid] / 1000 >= distance) hi = mid;
                else lo = mid + 1;
            }

            return lo;
        }

        /// <summary>The force needed to send the ball <paramref name="distance"/>, capped at <paramref name="maxForce"/>.</summary>
        public int ForceFor(int distance, int maxForce) =>
            ForceForTicks(distance, _maxFlightTicks, maxForce);

        /// <summary>
        /// The force that carries the ball <paramref name="distance"/> in about
        /// <paramref name="ticks"/> ticks. Using the WEAKEST force that eventually covers the
        /// distance instead leaves passes trickling across the pitch, which is both slow to
        /// watch and easy to intercept.
        /// </summary>
        /// <summary>
        /// The force that delivers the ball <paramref name="distance"/> and has it DYING as it
        /// arrives — down to <paramref name="arrivalSpeed"/>, a speed a footballer can take in
        /// his stride (engine phase 4). This is what "the weight of the pass" means, and until
        /// this phase the model had no notion of it at all: every ball was struck at the force
        /// that REACHES the target in the nominal flight time and then ran on at almost the
        /// speed it left with. An eleven-metre pass rolled fifty-six metres. The receiver had a
        /// two-tick window to step into its path or it was gone — which is why a pass into
        /// twelve metres of clear space was still lost more than a quarter of the time, and why
        /// there were eighty throw-ins a match.
        ///
        /// Binary search on the two tables the ball is built from, which are monotone by
        /// construction: the ground covered in n ticks, and the speed left after them.
        /// </summary>
        public int ForceToArrive(int distance, int arrivalSpeed, int maxForce, out int ticks)
        {
            ticks = _maxFlightTicks;
            if (distance <= 0 || arrivalSpeed <= 0) return ForceForTicks(distance, 1, maxForce);

            int lo = 1, hi = _maxFlightTicks;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                // force(mid) * remaining(mid) / 1000 <= arrivalSpeed, with force(mid) = distance * 1000 / cumulative(mid)
                if ((long)distance * _remaining[mid] <= (long)arrivalSpeed * _cumulative[mid]) hi = mid;
                else lo = mid + 1;
            }

            ticks = lo;
            return ForceForTicks(distance, lo, maxForce);
        }

        public int ForceForTicks(int distance, int ticks, int maxForce)
        {
            if (ticks < 1) ticks = 1;
            if (ticks > _trackedTicks) ticks = _trackedTicks;

            long span = _cumulative[ticks];
            long force = span <= 0 ? maxForce : (long)distance * 1000 / span;
            if (force > maxForce) force = maxForce;
            return force < 1 ? 1 : (int)force;
        }

        /// <summary>Where the ball will be in <paramref name="ticks"/> ticks if nobody touches it.</summary>
        public void Future(int ticks, out int x, out int y)
        {
            x = X;
            y = Y;
            int vx = Vx, vy = Vy;
            for (int t = 0; t < ticks; t++)
            {
                x += vx;
                y += vy;
                vx = vx * _frictionPermille / 1000;
                vy = vy * _frictionPermille / 1000;
            }
        }

        /// <summary>One tick of free flight applied to a running prediction (no allocation, no rewind).</summary>
        public void StepPrediction(ref int x, ref int y, ref int vx, ref int vy)
        {
            x += vx;
            y += vy;
            vx = vx * _frictionPermille / 1000;
            vy = vy * _frictionPermille / 1000;
        }
    }
}
