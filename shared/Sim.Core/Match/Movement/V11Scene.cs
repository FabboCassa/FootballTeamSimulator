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
            Foes = new PitchActor[capacity];
        }

        /// <summary>True when the attacked goal is at X = <see cref="Pitch.LengthDm"/>.</summary>
        public bool AttacksHighX { get; set; } = true;

        public int BallXDm { get; set; }
        public int BallYDm { get; set; }
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

        /// <summary>Where a clearance would land, when there is room to clear it at all.</summary>
        public bool CanClear { get; set; }
        public int ClearXDm { get; set; }
        public int ClearYDm { get; set; }

        /// <summary>How long he has had this open goal in front of him (R4), in ms; 0 the first time he sees it.</summary>
        public int OpenGoalForMs { get; set; }

        /// <summary>The offside line he plays to: a team-mate beyond it (toward the attacked goal) is not an option.</summary>
        public int OffsideLineXDm { get; set; } = Pitch.LengthDm;

        /// <summary>The longest pass he can hit.</summary>
        public int MaxPassDm { get; set; } = 500;

        public TacticInstructions Instructions { get; set; } = TacticInstructions.Neutral;

        /// <summary>What a goal is worth to his side, in percent (Mentality times Tempo).</summary>
        public int ShotAppetitePercent { get; set; } = 100;

        public PitchActor[] Mates { get; }
        public bool[] MateIsKeeper { get; }
        public int MateCount { get; private set; }

        public PitchActor[] Foes { get; }
        public int FoeCount { get; private set; }

        /// <summary>The opponents' keeper among <see cref="Foes"/>, or -1.</summary>
        public int FoeKeeper { get; private set; } = -1;

        public void Reset()
        {
            MateCount = 0;
            FoeCount = 0;
            FoeKeeper = -1;
        }

        /// <summary>A team-mate he could play it to (not himself); returns his index.</summary>
        public int AddMate(in PitchActor mate, bool keeper)
        {
            Mates[MateCount] = mate;
            MateIsKeeper[MateCount] = keeper;
            return MateCount++;
        }

        public void AddFoe(in PitchActor foe, bool keeper)
        {
            if (keeper) FoeKeeper = FoeCount;
            Foes[FoeCount++] = foe;
        }
    }
}
