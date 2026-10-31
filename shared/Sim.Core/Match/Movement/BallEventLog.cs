using System.Collections.Generic;

namespace Sim.Core.Match.Movement
{
    /// <summary>The ball-event sink the harness attaches (R2): keeps every event in the order heard, which is tick order.</summary>
    public sealed class BallEventLog : IBallEventSink
    {
        private readonly List<BallEvent> _events = new List<BallEvent>();

        public IReadOnlyList<BallEvent> Records => _events;

        public void Record(in BallEvent ballEvent) => _events.Add(ballEvent);
    }
}
