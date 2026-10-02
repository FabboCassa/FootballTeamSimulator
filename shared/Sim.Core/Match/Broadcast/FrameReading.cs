namespace Sim.Core.Match.Broadcast
{
    /// <summary>What a frame is worth to the broadcast before the budget is spent.</summary>
    internal enum FrameClass : byte
    {
        /// <summary>Open play: shown live when the budget reaches it.</summary>
        Open = 0,

        /// <summary>A possession spell that never left its own half: cut unless the budget is starved.</summary>
        Sterile = 1,

        /// <summary>The set-up of a dead ball: shown at dead time when the budget reaches it.</summary>
        DeadTime = 2,

        /// <summary>A dead ball before its set-up (the rest of a celebration, a stoppage): always cut.</summary>
        Idle = 3
    }

    /// <summary>The per-frame reading the director spends its budget on.</summary>
    internal sealed class FrameReading
    {
        public FrameReading(int frames, int secondHalfStart)
        {
            Classes = new FrameClass[frames];
            Key = new bool[frames];
            Anchor = new bool[frames];
            SecondHalfStart = secondHalfStart;
        }

        public FrameClass[] Classes { get; }

        /// <summary>Frames that must be shown whatever the budget says.</summary>
        public bool[] Key { get; }

        /// <summary>The first frame of each key event: the lead-ins grow backwards from these.</summary>
        public bool[] Anchor { get; }

        public int SecondHalfStart { get; }

        public int HalfEnd(int frame) => frame < SecondHalfStart ? SecondHalfStart : Classes.Length;

        /// <summary>The rate frame <paramref name="f"/> plays at when it is shown.</summary>
        public PlaybackRate ShownRate(int f) =>
            Classes[f] >= FrameClass.DeadTime ? PlaybackRate.DeadTime : PlaybackRate.Live;
    }
}
