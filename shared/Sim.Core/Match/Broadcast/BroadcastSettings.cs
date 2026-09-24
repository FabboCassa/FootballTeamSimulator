namespace Sim.Core.Match.Broadcast
{
    /// <summary>The director's tunables, in whole seconds of match time or of playback.</summary>
    public sealed class BroadcastSettings
    {
        /// <summary>Playback length of each half at 1x (R12: 5 minutes, so 10 for the match).</summary>
        public int TargetSecondsPerHalf { get; set; } = 300;

        /// <summary>What stays on screen at real time after a shot, goal, card, penalty or substitution.</summary>
        public int KeyAftermathSeconds { get; set; } = 3;

        /// <summary>A ball won in the own half that reaches the final third within this long is a counter.</summary>
        public int CounterWindowSeconds { get; set; } = 12;
    }
}
