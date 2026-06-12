using System.Collections.Generic;

namespace Sim.Core.Match
{
    /// <summary>
    /// One snapshot of every on-pitch entity at a given tick.
    /// Player arrays are indexed by lineup slot order (Lineup.Slots).
    /// </summary>
    public sealed class PositionFrame
    {
        public int Tick { get; set; }
        public PitchPoint Ball { get; set; }
        public PitchPoint[] Home { get; set; } = System.Array.Empty<PitchPoint>();
        public PitchPoint[] Away { get; set; } = System.Array.Empty<PitchPoint>();
    }

    /// <summary>
    /// Replayable top-down movement of players and ball (task 1.5).
    /// Tick 0 is kickoff; the last tick is minute 90. An event at minute M
    /// resolves exactly at tick M * TicksPerMinute, where the ball coincides
    /// with the shooter's position. Derived deterministically from
    /// (seed + lineups), so it never needs to travel over the network.
    /// </summary>
    public sealed class PositionStream
    {
        public int TicksPerMinute { get; set; }
        public List<PositionFrame> Frames { get; set; } = new List<PositionFrame>();

        /// <summary>Tick at which an event with the given minute resolves.</summary>
        public int TickOfMinute(int minute) => minute * TicksPerMinute;
    }
}
