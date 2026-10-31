namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The ball's touches and landings, told to an optional <see cref="IBallEventSink"/>
    /// (free-ball-real-movement spec R2). Null in production; attached, it only reads.
    /// </summary>
    internal sealed partial class MatchBall
    {
        public IBallEventSink? EventSink;

        /// <summary>The tick the simulation is on, stamped on every event.</summary>
        public int EventTick;

        public void Heard(BallEventKind kind, int side, int slot)
        {
            if (EventSink == null) return;
            EventSink.Record(new BallEvent
            {
                Tick = EventTick, Kind = kind, Side = side, Slot = slot, XDm = U.Dm(X), YDm = U.Dm(Y)
            });
        }
    }
}
