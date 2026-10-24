namespace Sim.Core.Match.Broadcast
{
    /// <summary>The director's tunables, in whole seconds of match time or of playback.</summary>
    public sealed class BroadcastSettings
    {
        /// <summary>Playback length of each half at 1x (R12: 5 minutes, so 10 for the match).</summary>
        public int TargetSecondsPerHalf { get; set; } = 300;

        /// <summary>What is always shown after a shot, goal, card, penalty or substitution.</summary>
        public int KeyAftermathSeconds { get; set; } = 3;

        /// <summary>
        /// How much of a dead ball can be shown: the set-up before the touch that restarts play.
        /// The wait before it (a long celebration, a stoppage) is always cut.
        /// </summary>
        public int DeadTimeShownSeconds { get; set; } = 6;
    }
}
