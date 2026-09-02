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
    /// </summary>
    internal sealed class MatchBall
    {
        /// <summary>Speed kept per tick, in permille. Lower = the ball pulls up sooner.</summary>
        public const int FrictionPermille = 880;

        /// <summary>A ball slower than this has stopped.</summary>
        private const int RestSpeed = 6 * U.Scale / 10;

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
            Vx = Vx * FrictionPermille / 1000;
            Vy = Vy * FrictionPermille / 1000;

            if (U.Length(Vx, Vy) < RestSpeed)
            {
                Vx = 0;
                Vy = 0;
            }
        }

        public int Speed => U.Length(Vx, Vy);

        /// <summary>
        /// Ticks for a ball struck at <paramref name="force"/> to roll <paramref name="distance"/>,
        /// or <see cref="Unreachable"/> if friction stops it first. Iterative on purpose: the
        /// closed form needs a logarithm, and this has to give the same answer everywhere.
        /// </summary>
        public const int Unreachable = 9999;

        public static int TicksToCover(int distance, int force)
        {
            if (distance <= 0) return 0;

            long travelled = 0;
            int speed = force;
            for (int t = 1; t <= 40; t++)
            {
                travelled += speed;
                if (travelled >= distance) return t;
                speed = speed * FrictionPermille / 1000;
                if (speed < RestSpeed) break;
            }

            return Unreachable;
        }

        /// <summary>How far a ball struck at <paramref name="force"/> rolls before it stops.</summary>
        public static int RangeOf(int force)
        {
            long travelled = 0;
            int speed = force;
            for (int t = 0; t < 40 && speed >= RestSpeed; t++)
            {
                travelled += speed;
                speed = speed * FrictionPermille / 1000;
            }

            return travelled > int.MaxValue ? int.MaxValue : (int)travelled;
        }

        /// <summary>The force needed to send the ball <paramref name="distance"/>, capped at <paramref name="maxForce"/>.</summary>
        public static int ForceFor(int distance, int maxForce)
        {
            // Binary search on RangeOf, which is monotone in force. ~11 iterations.
            int lo = U.Scale, hi = maxForce;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (RangeOf(mid) >= distance) hi = mid;
                else lo = mid + 1;
            }

            return lo;
        }

        /// <summary>
        /// The force that carries the ball <paramref name="distance"/> in about
        /// <paramref name="ticks"/> ticks. Using the WEAKEST force that eventually covers the
        /// distance instead leaves passes trickling across the pitch for ten ticks, which is
        /// both slow to watch and easy to intercept.
        /// </summary>
        public static int ForceForTicks(int distance, int ticks, int maxForce)
        {
            if (ticks < 1) ticks = 1;

            long sum = 0, term = 1000;
            for (int i = 0; i < ticks; i++)
            {
                sum += term;
                term = term * FrictionPermille / 1000;
            }

            long force = (long)distance * 1000 / (sum <= 0 ? 1 : sum);
            if (force > maxForce) force = maxForce;
            return force < 1 ? 1 : (int)force;
        }

        /// <summary>How far a ball struck at <paramref name="force"/> rolls in <paramref name="ticks"/> ticks.</summary>
        public static int RangeInTicks(int force, int ticks)
        {
            long travelled = 0;
            int speed = force;
            for (int t = 0; t < ticks; t++)
            {
                travelled += speed;
                speed = speed * FrictionPermille / 1000;
            }

            return travelled > int.MaxValue ? int.MaxValue : (int)travelled;
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
                vx = vx * FrictionPermille / 1000;
                vy = vy * FrictionPermille / 1000;
            }
        }
    }
}
