namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>
        /// Hears every touch, deflection and landing of the ball, for the harness's cause trace of
        /// free-ball turns (free-ball-real-movement spec R2). Null in production; attached, it only
        /// reads, so the match does not move.
        /// </summary>
        public IBallEventSink? BallEventSink { get; set; }
    }
}
