namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The ball under a man's control still rolls by its own physics (free-ball spec R3, issue
    /// #179): a first touch leaves it running off his foot, and a man running with it plays it ahead
    /// of himself touch after touch. Possession is who controls it, not where it is drawn.
    /// </summary>
    internal sealed partial class MatchBall
    {
        /// <summary>Search bound of <see cref="DribbleTouchSpeed"/>: no dribble touch is played faster than this many times his pace.</summary>
        private const int MaxDribbleTouchPaceMultiple = 3;

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
        /// at most <paramref name="leadU"/> ahead of him before he catches it again. Binary search on the
        /// gain a ball faster than him makes while it is faster, which grows with its speed.
        /// </summary>
        public int DribbleTouchSpeed(int aheadU, int carrierU, int leadU)
        {
            if (carrierU < 1) carrierU = 1;
            // Beyond it already, he holds it back: slower than him by what it is beyond.
            if (aheadU >= leadU) return carrierU > aheadU - leadU ? carrierU - (aheadU - leadU) : 0;

            // The fastest that does not run it beyond his lead.
            int lo = carrierU, hi = MaxDribbleTouchPaceMultiple * carrierU;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (aheadU + LeadGain(mid, carrierU) <= leadU) lo = mid;
                else hi = mid - 1;
            }

            return lo;
        }

        /// <summary>
        /// How far a ball played at <paramref name="speed"/> from <paramref name="aheadU"/> in front of a
        /// man running at <paramref name="carrierU"/> a tick runs, slowing, before it is back within
        /// <paramref name="reachU"/> of him: where he can play it again.
        /// </summary>
        public int TravelUntilBackInReach(int speed, int aheadU, int carrierU, int reachU)
        {
            long gap = aheadU, travel = 0;
            for (int t = 0; t < _trackedTicks; t++)
            {
                gap += speed - carrierU;
                travel += speed;
                speed = speed * _frictionPermille / 1000;
                if (speed <= carrierU && gap <= reachU) break;
            }

            return travel > int.MaxValue ? int.MaxValue : (int)travel;
        }

        /// <summary>
        /// Ticks until a ball played as in <see cref="TravelUntilBackInReach"/> is back within
        /// <paramref name="reachU"/> of him.
        /// </summary>
        public int TicksUntilBackInReach(int speed, int aheadU, int carrierU, int reachU)
        {
            long gap = aheadU;
            for (int t = 0; t < _trackedTicks; t++)
            {
                gap += speed - carrierU;
                speed = speed * _frictionPermille / 1000;
                if (speed <= carrierU && gap <= reachU) return t + 1;
            }

            return _trackedTicks;
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
