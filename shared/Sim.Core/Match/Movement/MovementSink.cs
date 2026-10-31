namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Hears, tick by tick, how the men it watches are moved (free-ball spec R7, issue #177): the
    /// dev trace behind the oscillation write-up. Optional and null in production: attached, it only
    /// reads, so the match is the same.
    /// </summary>
    public interface IMovementSink
    {
        /// <summary>Whether man <paramref name="slot"/> of <paramref name="side"/> is traced. Asked once a tick per man.</summary>
        bool Watches(int side, int slot);

        void Record(in MovementRecord move);
    }

    /// <summary>
    /// What moved a man this tick. The first ten mirror the base brain's jobs; the rest are the
    /// V11 brain's own targets for a man the base brain has no job for (or a supporting run).
    /// </summary>
    public enum MovementJob
    {
        /// <summary>His phase target spot.</summary>
        Spot = 0,
        SentOff = 1,
        Restart = 2,
        Ball = 3,
        Keeper = 4,
        Chase = 5,
        Press = 6,
        Support = 7,
        Cover = 8,
        Mark = 9,

        /// <summary>Placed by the V11 set-piece plan.</summary>
        SetPiece = 10,

        /// <summary>The run in behind.</summary>
        Run = 11,

        /// <summary>The short option (real-match R9).</summary>
        Offer = 12,
        Overlap = 13,

        /// <summary>Held within reach of a throw-in (R11).</summary>
        RestartOffer = 14,

        /// <summary>The press closed down from the goal side.</summary>
        PressGoalSide = 15,

        /// <summary>Out of possession (R7): the near-side full-back steps to the wide carrier.</summary>
        Step = 16,

        /// <summary>Out of possession (R7): the far-side full-back tucks in.</summary>
        Tuck = 17,

        /// <summary>Out of possession (R7): the holding midfielder screens the lane to the most advanced opponent.</summary>
        Screen = 18
    }

    /// <summary>
    /// One traced man in one tick: where he was sent and why, what his legs wanted, and where he
    /// ended up. Positions in dm, velocities in cm/s. Fields the job does not use stay 0.
    /// </summary>
    public struct MovementRecord
    {
        public int Tick;
        public int Side;
        public int Slot;

        /// <summary>What the base brain asked for (as <see cref="MovementJob"/>, Spot for none) and what moved him.</summary>
        public MovementJob BaseJob;
        public MovementJob Job;

        /// <summary>The ball at the top of the tick: where, whose (-1 nobody's) and whether it is dead.</summary>
        public int BallXDm;
        public int BallYDm;
        public int BallOwnerSide;
        public bool BallDead;

        // His side as the brain read it at the top of the tick: the offside line it attacks, its team
        // phase, whether it is in possession and how far into its attacking shape it is (permille).
        public int OffsideLineXDm;
        public TeamPhase Phase;
        public bool InPossession;
        public int ExpansionPermille;

        /// <summary>Whether he re-read the ball and the supporting run this tick (R7's staggered re-reads); his targets are read off what he last saw.</summary>
        public bool Reread;

        /// <summary>His phase target spot, when his move reads it: X before the onside hold, then the spot after it.</summary>
        public int RawSpotXDm;
        public int SpotXDm;
        public int SpotYDm;

        /// <summary>Whether the legs were steered this tick (Steer); false for a placement or a set piece walked to.</summary>
        public bool Steered;
        public bool Sprint;

        /// <summary>The target handed to the legs.</summary>
        public int TargetXDm;
        public int TargetYDm;

        /// <summary>The velocity his legs wanted toward the target, then after team-mates and opponents pushed him off.</summary>
        public int WantXCmPerS;
        public int WantYCmPerS;
        public int PushedXCmPerS;
        public int PushedYCmPerS;

        /// <summary>His velocity and position after the tick.</summary>
        public int VelXCmPerS;
        public int VelYCmPerS;
        public int XDm;
        public int YDm;
    }
}
