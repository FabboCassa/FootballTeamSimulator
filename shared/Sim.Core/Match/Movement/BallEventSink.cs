namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// What changed the ball (free-ball-real-movement spec R2), for the harness's cause trace of
    /// free-ball turns. In precedence order: when one window holds several, a turn's cause is the
    /// highest. <see cref="None"/> is never recorded; it is the cause of a turn nothing explains.
    /// </summary>
    public enum BallEventKind
    {
        None = 0,

        /// <summary>The ball came down onto the grass.</summary>
        Bounce = 1,

        /// <summary>A man struck or gathered it.</summary>
        Touch = 2,

        /// <summary>It came off a man who did not mean to play it (a shin, a block).</summary>
        Deflection = 3,

        /// <summary>
        /// Off the woodwork. The engine has none yet (a strike inside a post is in, one outside it
        /// out), so nothing records this today; it is the cause the spec names for when it has.
        /// </summary>
        Post = 4
    }

    /// <summary>
    /// Hears every touch, deflection and landing of the ball. Optional and null in production:
    /// attached, it only reads (no random draw, no state written), so the match is the same.
    /// </summary>
    public interface IBallEventSink
    {
        void Record(in BallEvent ballEvent);
    }

    /// <summary>One thing that happened to the ball, at a simulation tick (not a stream frame).</summary>
    public struct BallEvent
    {
        public int Tick;
        public BallEventKind Kind;

        /// <summary>The man it happened off: side 0 home, 1 away; -1 for a bounce nobody had touched.</summary>
        public int Side;
        public int Slot;

        /// <summary>Where the ball was, in dm.</summary>
        public int XDm;
        public int YDm;
    }
}
