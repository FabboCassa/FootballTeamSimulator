namespace Sim.Core.Random
{
    /// <summary>
    /// Deterministic random source used by every Sim.Core system.
    /// Implementations MUST be seedable and produce identical sequences
    /// on all platforms (.NET server, Unity Mono, Unity IL2CPP/WebGL).
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>Next raw 32 random bits.</summary>
        uint NextUInt();

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>Uniform double in [0, 1).</summary>
        double NextDouble();

        /// <summary>Returns true with the given probability in [0, 1].</summary>
        bool Chance(double probability);

        /// <summary>Captures the full internal state for serialization (save games, replays).</summary>
        RandomState GetState();

        /// <summary>Restores a previously captured state.</summary>
        void SetState(RandomState state);
    }

    /// <summary>Serializable RNG state.</summary>
    public readonly struct RandomState
    {
        public readonly ulong State;
        public readonly ulong Increment;

        public RandomState(ulong state, ulong increment)
        {
            State = state;
            Increment = increment;
        }
    }
}
