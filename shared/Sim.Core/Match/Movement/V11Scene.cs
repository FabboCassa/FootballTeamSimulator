using Sim.Core.Match.Movement.Models;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// What the man on the ball sees when the V11 brain asks him to choose (R3): the ball, his
    /// team-mates and the opponents as pitch control reads them, in pitch decimetres. The brain
    /// fills one per decision from the simulator; a test fills one by hand. Reusable: the arrays
    /// are sized once and <see cref="Reset"/> empties it without allocating.
    /// </summary>
    public sealed class V11Scene
    {
        public V11Scene(int capacity = 11)
        {
            Mates = new PitchActor[capacity];
            MateIsKeeper = new bool[capacity];
            MateShooting = new int[capacity];
            MateTechnique = new int[capacity];
            Foes = new PitchActor[capacity];
        }

        /// <summary>True when the attacked goal is at X = <see cref="Pitch.LengthDm"/>.</summary>
        public bool AttacksHighX { get; set; } = true;

        public int BallXDm { get; set; }
        public int BallYDm { get; set; }

        /// <summary>
        /// Where the ball itself lies when it runs a stride ahead of him (free-ball spec R3): an open
        /// goal is the ball's lane to the posts, read off this point, while the rest of the scene is
        /// read where he stands (<see cref="BallXDm"/>). Unless <see cref="SetLane"/> moved it, it is that point.
        /// </summary>
        public int LaneXDm => _laneSet ? _laneXDm : BallXDm;
        public int LaneYDm => _laneSet ? _laneYDm : BallYDm;

        private bool _laneSet;
        private int _laneXDm, _laneYDm;

        public void SetLane(int xDm, int yDm)
        {
            _laneSet = true;
            _laneXDm = xDm;
            _laneYDm = yDm;
        }
        public bool CarrierIsKeeper { get; set; }

        /// <summary>The carrier's attributes, 1-100.</summary>
        public int Shooting { get; set; } = 50;
        public int Technique { get; set; } = 50;
        public int Passing { get; set; } = 50;

        /// <summary>How much of the risk of losing the ball he sees, in percent (his Positioning).</summary>
        public int VisionPercent { get; set; } = 100;

        /// <summary>How pressed he is, 0 (free) to 1000 (closed down).</summary>
        public int PressurePermille { get; set; }

        /// <summary>The odds he keeps it through one touch past the nearest man.</summary>
        public int CarryKeepPermille { get; set; } = 1000;

        /// <summary>How far one touch takes him.</summary>
        public int CarryTouchDm { get; set; } = 100;

        /// <summary>A man goal-side of him stops his run (issue #206): he looks for a pass, and running on is no option.</summary>
        public bool GoalSideBlocked { get; set; }

        /// <summary>He meets a cross in the air (free-ball spec R5): a strike now is a header.</summary>
        public bool Header { get; set; }

        /// <summary>The ball is up in the air off anything but a team-mate's cross (R5): no strike at goal until it is down.</summary>
        public bool NoStrike { get; set; }

        /// <summary>Where a clearance would land, when there is room to clear it at all.</summary>
        public bool CanClear { get; set; }
        public int ClearXDm { get; set; }
        public int ClearYDm { get; set; }

        /// <summary>How long he has had this open goal in front of him (R4), in ms; 0 the first time he sees it.</summary>
        public int OpenGoalForMs { get; set; }

        /// <summary>The offside line he plays to: a team-mate beyond it (toward the attacked goal) is not an option.</summary>
        public int OffsideLineXDm { get; set; } = Pitch.LengthDm;

        /// <summary>
        /// How far behind a team-mate running at goal he reads him, in ms of that run: he sees the
        /// runner where he was when he looked up, not where he is when the ball is struck.
        /// </summary>
        public int OffsideReadLagMs { get; set; }

        /// <summary>The longest pass he can hit.</summary>
        public int MaxPassDm { get; set; } = 500;

        public TacticInstructions Instructions { get; set; } = TacticInstructions.Neutral;

        /// <summary>What a goal is worth to his side, in percent (Mentality times Tempo).</summary>
        public int ShotAppetitePercent { get; set; } = 100;

        /// <summary>A shout's weight on the ball that can be lost, in percent (KeepBall, Concentrate).</summary>
        public int RiskPercent { get; set; } = 100;

        /// <summary>A shout's weight on the threat a move gains, in percent (KeepBall).</summary>
        public int GainPercent { get; set; } = 100;

        public PitchActor[] Mates { get; }
        public bool[] MateIsKeeper { get; }

        /// <summary>Each team-mate's finishing (1-100): what his shot after a ball to him is worth (R3).</summary>
        public int[] MateShooting { get; }
        public int[] MateTechnique { get; }
        public int MateCount { get; private set; }

        public PitchActor[] Foes { get; }
        public int FoeCount { get; private set; }

        /// <summary>The opponents' keeper among <see cref="Foes"/>, or -1.</summary>
        public int FoeKeeper { get; private set; } = -1;

        public void Reset()
        {
            _laneSet = false;
            GoalSideBlocked = false;
            Header = false;
            NoStrike = false;
            MateCount = 0;
            FoeCount = 0;
            FoeKeeper = -1;
        }

        /// <summary>A team-mate he could play it to (not himself); returns his index.</summary>
        public int AddMate(in PitchActor mate, bool keeper, int shooting = 50, int technique = 50)
        {
            Mates[MateCount] = mate;
            MateIsKeeper[MateCount] = keeper;
            MateShooting[MateCount] = shooting;
            MateTechnique[MateCount] = technique;
            return MateCount++;
        }

        public void AddFoe(in PitchActor foe, bool keeper)
        {
            if (keeper) FoeKeeper = FoeCount;
            Foes[FoeCount++] = foe;
        }
    }
}
