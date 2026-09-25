using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>
        /// The engine v10 brain — the decisions and the positioning of engine phases 1 to 8, moved
        /// out of the simulator unchanged, draw for draw, so the golden master still pins it.
        ///
        /// It is nested so that it reads the simulator's pitch without the rest of the assembly
        /// seeing it. The per-player arrays and the derived units are the simulator's own, bound
        /// by reference at <see cref="Begin"/>, when a match has built them; what only the brain
        /// writes — the shape, the duties, the supporting run — lives here.
        /// </summary>
        private sealed partial class V10Brain : IMatchBrain
        {
            private const int MaxSupporters = 3;

            /// <summary>
            /// "He cannot do this at all". Every option a man weighs answers with what it is worth in
            /// decimetres of forward progress, and one that is not open to him has to answer with
            /// something no real option can beat — but the comparison then has to EXCLUDE it, or a man
            /// with nothing open does the first thing on the list. That is not hypothetical: it put a
            /// goalkeeper who had just won the ball in his own six-yard box through
            /// <see cref="TakeShot"/>, and the scoresheet counted his hoof upfield as a shot.
            /// </summary>
            private const int NoOption = int.MinValue / 4;

            // One duty per man per brain tick, for the side without the ball (engine phase 3).
            // Going to the ball is decided every tick and is not stored here; these are the three
            // standing jobs: cover the presser, pick a man up, or hold your place in the block.
            private const int DutyZone = 0;
            private const int DutyCover = 1;
            private const int DutyMark = 2;
            private int[] _duty = System.Array.Empty<int>();
            private int[] _mark = System.Array.Empty<int>();

            /// <summary>Scratch for <see cref="AssignDuties"/>, so a hot loop allocates nothing.</summary>
            private bool[] _markTaken = System.Array.Empty<bool>();

            private readonly bool[] _attacking = new bool[SideCount];

            // The block: one shape per side, worked out once a tick and read by everything that
            // asks where a man belongs. Absolute, already mirrored for the away side.
            private readonly int[] _blockLineU = new int[SideCount];      // the back line
            private readonly int[] _blockSpacingU = new int[SideCount];   // step to the next line, signed
            private readonly int[] _blockWidthPercent = new int[SideCount];
            private readonly int[] _blockY = new int[SideCount];          // the block's centre across the pitch

            /// <summary>
            /// How far into its attacking shape a side is, in permille. A shape cannot teleport: a
            /// team that has just won the ball takes a few seconds to open up and a few more to
            /// close down again, so this walks toward its target instead of snapping to it. Without
            /// it every one of the several hundred turnovers a match moved twenty-two men six metres
            /// sideways and back, which is neither watchable nor payable in kilometres.
            /// </summary>
            private readonly int[] _expansion = new int[SideCount];

            /// <summary>The height the line is currently holding, so it steps instead of shuffling.</summary>
            private readonly int[] _blockLine = new int[SideCount];
            private readonly bool[] _blockSet = new bool[SideCount];
            private readonly int[] _chaser = new int[SideCount];
            private readonly int[] _second = new int[SideCount];
            private readonly bool[] _driving = new bool[SideCount];
            private readonly int[] _supportX = new int[SideCount];
            private readonly int[] _supportY = new int[SideCount];
            private readonly int[] _supporters = new int[SideCount * MaxSupporters];

            // The simulator's, for the life of the simulator.
            private readonly MatchSimulator _sim;
            private readonly MatchBalance _cfg;
            private readonly MovementTactics[] _tactics;
            private readonly int[] _lineCount;
            private readonly int[] _receiver;
            private readonly int[] _pressUntil;

            // The simulator's, for one match: bound at Begin.
            private MatchBall _ball = null!;
            private MatchContext _ctx = null!;
            private Offside _offside = null!;
            private FreeKickWall _freeKickWall = null!;
            private int _n;
            private int[] _px = System.Array.Empty<int>();
            private int[] _py = System.Array.Empty<int>();
            private bool[] _keeper = System.Array.Empty<bool>();
            private bool[] _sentOff = System.Array.Empty<bool>();
            private int[] _hold = System.Array.Empty<int>();
            private int[] _vision = System.Array.Empty<int>();
            private int[] _skPassing = System.Array.Empty<int>();
            private int[] _skTechnique = System.Array.Empty<int>();
            private int[] _maxSpeed = System.Array.Empty<int>();
            private int[] _lineRank = System.Array.Empty<int>();
            private int[] _offsetXDm = System.Array.Empty<int>();
            private int[] _baseY = System.Array.Empty<int>();
            private int[] _forward = System.Array.Empty<int>();
            private int[] _stepFromX = System.Array.Empty<int>();
            private int[] _stepToX = System.Array.Empty<int>();
            private int[] _stepToY = System.Array.Empty<int>();
            private int _kickU, _controlU, _interceptU, _approachU, _recoveryU, _pressureU;
            private int _maxPassForce, _maxShootForce, _maxPassRange, _shootRangeU, _arrivalStepU;

            public V10Brain(MatchSimulator sim)
            {
                _sim = sim;
                _cfg = sim._cfg;
                _tactics = sim._tactics;
                _lineCount = sim._lineCount;
                _receiver = sim._receiver;
                _pressUntil = sim._pressUntil;
            }

            public void Begin()
            {
                Bind();

                int total = _n * SideCount;
                _duty = new int[total];
                _mark = new int[total];
                _markTaken = new bool[_n];

                // Kickoff: the home side takes it, so it is the side in possession. Both are needed
                // before the block can be built, and the block is needed before anybody can be
                // placed — hence the two passes below.
                _attacking[0] = true;
                _attacking[1] = false;
                _expansion[0] = 1000;
                _expansion[1] = 0;

                for (int side = 0; side < SideCount; side++)
                {
                    for (int i = 0; i < _n; i++) _mark[side * _n + i] = -1;
                    _chaser[side] = -1;
                    for (int s = 0; s < MaxSupporters; s++) _supporters[side * MaxSupporters + s] = -1;
                }

                for (int side = 0; side < SideCount; side++)
                {
                    UpdateBlock(side);
                    for (int i = 0; i < _n; i++)
                    {
                        int k = side * _n + i;
                        HomeSpot(side, i, out int hx, out int hy);
                        _px[k] = hx;
                        _py[k] = hy;
                    }
                }
            }

            private void Bind()
            {
                MatchSimulator s = _sim;
                _ball = s._ball;
                _ctx = s._ctx;
                _offside = s._offside;
                _freeKickWall = s._freeKickWall;
                _n = s._n;
                _px = s._px;
                _py = s._py;
                _keeper = s._keeper;
                _sentOff = s._sentOff;
                _hold = s._hold;
                _vision = s._vision;
                _skPassing = s._skPassing;
                _skTechnique = s._skTechnique;
                _maxSpeed = s._maxSpeed;
                _lineRank = s._lineRank;
                _offsetXDm = s._offsetXDm;
                _baseY = s._baseY;
                _forward = s._forward;
                _stepFromX = s._stepFromX;
                _stepToX = s._stepToX;
                _stepToY = s._stepToY;
                _kickU = s._kickU;
                _controlU = s._controlU;
                _interceptU = s._interceptU;
                _approachU = s._approachU;
                _recoveryU = s._recoveryU;
                _pressureU = s._pressureU;
                _maxPassForce = s._maxPassForce;
                _maxShootForce = s._maxShootForce;
                _maxPassRange = s._maxPassRange;
                _shootRangeU = s._shootRangeU;
                _arrivalStepU = s._arrivalStepU;
            }
        }
    }
}
