using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Staggered re-reads (free-ball spec R7): each outfielder looks up at the game on his own
    /// clock rather than all eleven re-targeting on the tick the ball moves. A sharper reader of
    /// the game (Positioning) looks up more often. Pure and draw-free.
    /// </summary>
    public static class V11ReRead
    {
        // Coprime with every period the config can give at 10 Hz (2 to 6 ticks), so neighbouring
        // slots never share a phase.
        private const int Stride = 7;

        /// <summary>His re-read period in ticks: V11ReReadSlowMs at Positioning 1, V11ReReadFastMs at 100.</summary>
        public static int PeriodTicks(int positioning, MatchBalance cfg)
        {
            int p = MovementGeometry.Clamp(positioning, 1, 100);
            int ms = cfg.V11ReReadSlowMs - (cfg.V11ReReadSlowMs - cfg.V11ReReadFastMs) * (p - 1) / 99;
            return cfg.TicksOfMs(ms);
        }

        /// <summary>Whether man <paramref name="k"/> (side * players + slot) re-reads on this tick.</summary>
        public static bool Due(int k, int tick, int periodTicks) =>
            periodTicks <= 1 || (tick + k * Stride) % periodTicks == 0;
    }
}
