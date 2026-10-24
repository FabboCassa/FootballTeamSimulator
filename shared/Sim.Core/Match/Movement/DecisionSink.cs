namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Hears every decision the man on the ball makes (believable-match spec R13), for the explained
    /// replay. Optional and null in production: attached, it only reads, so the match is the same.
    /// </summary>
    public interface IDecisionSink
    {
        void Record(in DecisionRecord decision);
    }

    /// <summary>One option as the man on the ball priced it.</summary>
    public struct DecisionOption
    {
        public V11ActionKind Kind;

        /// <summary>The lineup slot the ball is played to, -1 for anything but a pass or a cross.</summary>
        public int MateSlot;

        /// <summary>Where the ball is played or taken, in dm.</summary>
        public int XDm;
        public int YDm;

        /// <summary>The odds it comes off, in permille.</summary>
        public int CompletionPermille;

        /// <summary>Ten-thousandths of a goal.</summary>
        public int ValuePer10k;
    }

    /// <summary>One carrier decision: who, when, where, his three dearest options and what he chose.</summary>
    public struct DecisionRecord
    {
        public int Tick;
        public int Side;
        public int Slot;
        public int BallXDm;
        public int BallYDm;
        public DecisionOption Chosen;

        /// <summary>How many of <see cref="First"/>, <see cref="Second"/> and <see cref="Third"/> are set.</summary>
        public int OptionCount;
        public DecisionOption First;
        public DecisionOption Second;
        public DecisionOption Third;

        public DecisionOption Option(int i) => i == 0 ? First : i == 1 ? Second : Third;
    }
}
