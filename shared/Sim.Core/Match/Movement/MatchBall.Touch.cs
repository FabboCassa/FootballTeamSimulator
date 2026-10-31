namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The ball under a man's control still rolls by its own physics (free-ball spec R3, issue
    /// #179): a first touch leaves it running off his foot, and a man running with it plays it ahead
    /// of himself touch after touch. Possession is who controls it, not where it is drawn.
    /// </summary>
    internal sealed partial class MatchBall
    {
        /// <summary>One tick of roll: the step and the friction of <see cref="Advance"/>, whoever controls it.</summary>
        public void Roll()
        {
            PrevX = X;
            PrevY = Y;
            if (Vx == 0 && Vy == 0) return;

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

        /// <summary>Nobody controls it any more; the last man to touch it still did.</summary>
        public void LetGo()
        {
            OwnerSide = -1;
            OwnerSlot = -1;
        }

        /// <summary>How far a ball leaving a foot at <paramref name="speed"/> (units per tick) runs before it stops.</summary>
        public int RollOut(int speed) => RangeInTicks(speed, _trackedTicks);

        /// <summary>
        /// The speed (units per tick) to play the ball at, from <paramref name="aheadU"/> in front of a
        /// man running at <paramref name="carrierU"/> a tick, so that it runs out to
        /// <paramref name="leadU"/> ahead of him before he catches it again. Binary search on the
        /// gain a ball faster than him makes while it is faster, which grows with its speed.
        /// </summary>
        public int DribbleTouchSpeed(int aheadU, int carrierU, int leadU)
        {
            if (carrierU < 1) carrierU = 1;
            if (aheadU >= leadU) return carrierU;

            int lo = carrierU, hi = 3 * carrierU;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (aheadU + LeadGain(mid, carrierU) >= leadU) hi = mid;
                else lo = mid + 1;
            }

            return lo;
        }

        /// <summary>Ground a ball played at <paramref name="speed"/> gains on a man running at <paramref name="carrierU"/>.</summary>
        private long LeadGain(int speed, int carrierU)
        {
            long gain = 0;
            for (int t = 0; t < _trackedTicks && speed > carrierU; t++)
            {
                gain += speed - carrierU;
                speed = speed * _frictionPermille / 1000;
            }

            return gain;
        }
    }
}
