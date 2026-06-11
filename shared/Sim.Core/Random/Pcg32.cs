using System;

namespace Sim.Core.Random
{
    /// <summary>
    /// PCG32 (Permuted Congruential Generator, O'Neill 2014).
    /// Small, fast, statistically strong and fully deterministic across platforms:
    /// uses only unsigned integer arithmetic, no floating point in state transitions.
    /// </summary>
    public sealed class Pcg32 : IRandomSource
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _increment; // must be odd

        public Pcg32(ulong seed, ulong sequence = 54u)
        {
            _state = 0UL;
            _increment = (sequence << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = old * Multiplier + _increment;
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << (-rot & 31));
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive),
                    $"Range [{minInclusive}, {maxExclusive}) is empty.");

            uint range = (uint)((long)maxExclusive - minInclusive);

            // Debiased modulo (rejection sampling) - uniform for any range.
            uint threshold = (uint)(-range) % range;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                    return (int)(minInclusive + (long)(r % range));
            }
        }

        public double NextDouble()
        {
            // 32 bits -> [0, 1). Multiplication by a constant is IEEE-exact and portable.
            return NextUInt() * (1.0 / 4294967296.0);
        }

        public bool Chance(double probability)
        {
            if (probability <= 0.0) return false;
            if (probability >= 1.0) return true;
            return NextDouble() < probability;
        }

        public RandomState GetState() => new RandomState(_state, _increment);

        public void SetState(RandomState state)
        {
            _state = state.State;
            _increment = state.Increment | 1UL; // increment must stay odd
        }

        /// <summary>Derives an independent child generator (e.g. one per match) from this one.</summary>
        public Pcg32 Split()
        {
            ulong seed = ((ulong)NextUInt() << 32) | NextUInt();
            ulong seq = ((ulong)NextUInt() << 32) | NextUInt();
            return new Pcg32(seed, seq);
        }
    }
}
