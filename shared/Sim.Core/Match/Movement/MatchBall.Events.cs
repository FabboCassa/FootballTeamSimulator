namespace Sim.Core.Match.Movement
{
    /// <summary>Where a man's feet are, for the reach stamped on a ball event (free-ball spec R3).</summary>
    internal interface IBallFeet
    {
        void FeetOf(int side, int slot, out int x, out int y, out bool keeper);
    }

    /// <summary>
    /// The ball's touches and landings, told to an optional <see cref="IBallEventSink"/>
    /// (free-ball-real-movement spec R2). Null in production; attached, it only reads.
    /// </summary>
    internal sealed partial class MatchBall
    {
        public IBallEventSink? EventSink;

        /// <summary>Reads the toucher's feet for <see cref="BallEvent.FromFeetMm"/>; null leaves it 0.</summary>
        public IBallFeet? Feet;

        /// <summary>The tick the simulation is on, stamped on every event.</summary>
        public int EventTick;

        public void Heard(BallEventKind kind, int side, int slot)
        {
            if (EventSink == null) return;
            var e = new BallEvent
            {
                Tick = EventTick, Kind = kind, Side = side, Slot = slot, XDm = U.Dm(X), YDm = U.Dm(Y), ZDm = Z
            };
            if (kind != BallEventKind.Bounce && side >= 0 && Feet != null)
            {
                Feet.FeetOf(side, slot, out int fx, out int fy, out e.Keeper);
                e.FromFeetMm = (int)((long)U.Length(X - fx, Y - fy) * 100 / U.Scale);
            }

            EventSink.Record(e);
        }
    }
}
