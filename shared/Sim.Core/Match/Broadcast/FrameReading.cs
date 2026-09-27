namespace Sim.Core.Match.Broadcast
{
    /// <summary>What a frame is worth to the broadcast before the budget is spent.</summary>
    internal enum FrameClass : byte
    {
        /// <summary>Other open play: shown at 2x when the budget reaches it.</summary>
        Warm = 0,

        /// <summary>Final-third possession, a counter, an attacking-half set piece: shown at 1x.</summary>
        Hot = 1,

        /// <summary>A possession spell that never left its own half: cut unless the budget is starved.</summary>
        Sterile = 2,

        /// <summary>A dead ball outside the attacking half, or a celebration: always cut.</summary>
        Dead = 3
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

        /// <summary>Frames that must play at 1x whatever the budget says.</summary>
        public bool[] Key { get; }

        /// <summary>The first frame of each key event: the lead-ins grow backwards from these.</summary>
        public bool[] Anchor { get; }

        public int SecondHalfStart { get; }

        public int HalfEnd(int frame) => frame < SecondHalfStart ? SecondHalfStart : Classes.Length;
    }
}
