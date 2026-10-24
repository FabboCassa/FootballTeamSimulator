using Sim.Core.Random;

namespace Sim.Core.Domain
{
    /// <summary>
    /// A coach's footballing leanings (real-match spec R22), each in [-2, 2] with 0 neutral.
    /// The AI weighs them against its squad when it picks a style
    /// (<see cref="Tactics.AiStyleChooser"/>). Additive: an old save reads all-neutral.
    /// </summary>
    public sealed class ManagerProfile
    {
        public const int MaxLean = 2;

        private int _attack, _possession, _pressing;

        /// <summary>Defensive (-) to attacking (+).</summary>
        public int Attack { get => _attack; set => _attack = Clamp(value); }

        /// <summary>Direct (-) to possession (+).</summary>
        public int Possession { get => _possession; set => _possession = Clamp(value); }

        /// <summary>Sits off (-) to presses high (+).</summary>
        public int Pressing { get => _pressing; set => _pressing = Clamp(value); }

        /// <summary>A uniform draw on every lean, three draws from <paramref name="rng"/>.</summary>
        public static ManagerProfile Draw(IRandomSource rng) => new ManagerProfile
        {
            Attack = rng.NextInt(-MaxLean, MaxLean + 1),
            Possession = rng.NextInt(-MaxLean, MaxLean + 1),
            Pressing = rng.NextInt(-MaxLean, MaxLean + 1)
        };

        private static int Clamp(int lean) => lean < -MaxLean ? -MaxLean : lean > MaxLean ? MaxLean : lean;
    }
}
