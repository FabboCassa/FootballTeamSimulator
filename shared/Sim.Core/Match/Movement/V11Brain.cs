using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>
        /// Ticks the given side spent in <paramref name="phase"/> in the last match played — the
        /// harness's view of R1. Only the V11 brain runs the phase machine; on V10 it is zero.
        /// </summary>
        public int PhaseTicks(int side, TeamPhase phase) =>
            _brain is V11Brain v11 ? v11.PhaseTicks(side, phase) : 0;

        /// <summary>
        /// R2's off-target ticks for this man in the last match played: ticks spent further than
        /// <see cref="MatchBalance.V11OffTargetDm"/> (25 m) from his phase target spot, with no job
        /// (not on the ball, chasing, pressing, covering, marking, keeping goal or placed for a
        /// restart), beyond the first <see cref="MatchBalance.V11OffTargetGraceMs"/> (5 s) of each
        /// unbroken spell. V11 only; on V10 it is zero.
        /// </summary>
        public int OffTargetTicks(int side, int slot) =>
            _brain is V11Brain v11 ? v11.OffTargetTicks(side * _n + slot) : 0;

        /// <summary>As <see cref="OffTargetTicks"/> but with no grace: every tick of every spell.</summary>
        public int FarFromTargetTicks(int side, int slot) =>
            _brain is V11Brain v11 ? v11.FarTicks(side * _n + slot) : 0;

        /// <summary>Runs in behind the given side started in the last match played (V11 only).</summary>
        public int RunsInBehind(int side) => _brain is V11Brain v11 ? v11.Runs(side) : 0;

        /// <summary>Player-ticks the given side's full-backs spent on the overlap in the last match (V11 only).</summary>
        public int OverlapTicks(int side) => _brain is V11Brain v11 ? v11.Overlaps(side) : 0;

        /// <summary>
        /// The engine v11 brain (watchable-match spec). It takes over one decision at a time in the
        /// tasks that follow; until a decision is its own it is v10's. The team phase (R1) is read
        /// here once a tick, off the ball alone. Since task 6 (R2) it positions every man v10 has no
        /// job for — his phase target spot, a run in behind, an overlap — and hands every other man
        /// (on the ball, chasing, pressing, supporting, covering, marking, placed for a restart) to
        /// v10's Move, so the golden master stays on v10 and v11's match is its own.
        /// </summary>
        private sealed partial class V11Brain : IMatchBrain
        {
            private readonly MatchSimulator _sim;
            private readonly V10Brain _v10;
            private readonly TeamPhaseMachine _phases;
            private readonly V11Positioning _positioning;
            private readonly int[] _phaseTicks = new int[SideCount * TeamPhaseMachine.PhaseCount];

            // Who is running in behind for each side, until when, and to where (units).
            private readonly int[] _runner = new int[SideCount];
            private readonly int[] _runUntil = new int[SideCount];
            private readonly int[] _runX = new int[SideCount];
            private readonly int[] _runY = new int[SideCount];
            private readonly int[] _runs = new int[SideCount];
            private readonly int[] _overlaps = new int[SideCount];

            // Read once a tick for each side: the offside line it attacks (dm) and the opponents (dm).
            private readonly int[] _lineX = new int[SideCount];
            private readonly int[] _foeCount = new int[SideCount];
            private readonly int[][] _foeX = new int[SideCount][];
            private readonly int[][] _foeY = new int[SideCount][];

            // R2 per man: ticks of the current off-target spell, ticks far from target, ticks off target.
            private int[] _spell = System.Array.Empty<int>();
            private int[] _farTicks = System.Array.Empty<int>();
            private int[] _offTarget = System.Array.Empty<int>();

            /// <summary>How far into its attacking shape each side is, in permille (see V11Positioning).</summary>
            private readonly int[] _expansion = new int[SideCount];

            public V11Brain(MatchSimulator sim)
            {
                _sim = sim;
                _v10 = new V10Brain(sim);
                _phases = new TeamPhaseMachine(sim._cfg);
                _positioning = new V11Positioning(sim._cfg);
            }

            public int PhaseTicks(int side, TeamPhase phase) =>
                _phaseTicks[side * TeamPhaseMachine.PhaseCount + (int)phase];

            public int OffTargetTicks(int k) => k < _offTarget.Length ? _offTarget[k] : 0;

            public int FarTicks(int k) => k < _farTicks.Length ? _farTicks[k] : 0;

            public int Runs(int side) => _runs[side];

            public int Overlaps(int side) => _overlaps[side];

            public void Begin()
            {
                _phases.Reset();
                System.Array.Clear(_phaseTicks, 0, _phaseTicks.Length);
                _spell = new int[_sim._n * SideCount];
                _farTicks = new int[_sim._n * SideCount];
                _offTarget = new int[_sim._n * SideCount];
                for (int side = 0; side < SideCount; side++)
                {
                    _runner[side] = -1;
                    _expansion[side] = 0;
                    _runs[side] = 0;
                    _overlaps[side] = 0;
                    _foeX[side] = new int[_sim._n];
                    _foeY[side] = new int[_sim._n];
                }

                _v10.Begin();
            }

            public void UpdateTeams(int tick)
            {
                MatchBall ball = _sim._ball;
                _phases.Update(tick, U.Dm(ball.X), ball.OwnerSide, ball.Dead, _sim._ctx.DeadSide);
                for (int side = 0; side < SideCount; side++)
                    _phaseTicks[side * TeamPhaseMachine.PhaseCount + (int)_phases.PhaseOf(side)]++;

                _v10.UpdateTeams(tick);

                for (int side = 0; side < SideCount; side++)
                {
                    Expand(side);
                    ReadOpponents(side);
                }
            }

            private void Expand(int side)
            {
                MatchBalance cfg = _sim._cfg;
                int e = _expansion[side];
                if (_phases.InPossession(side)) e += 1000 / cfg.V11ShapeExpandTicks;
                else e -= 1000 / cfg.V11ShapeCollapseTicks;
                _expansion[side] = MovementGeometry.Clamp(e, 0, 1000);
            }

            public void Act(int tick, int side, int slot) => _v10.Act(tick, side, slot);

            private void ReadOpponents(int side)
            {
                int n = _sim._n;
                int opponent = 1 - side;
                int count = 0;
                for (int j = 0; j < n; j++)
                {
                    int k = opponent * n + j;
                    if (_sim._sentOff[k]) continue;
                    _foeX[side][count] = U.Dm(_sim._px[k]);
                    _foeY[side][count] = U.Dm(_sim._py[k]);
                    count++;
                }

                _foeCount[side] = count;
                _lineX[side] = U.Dm(MovementGeometry.Direction(side == 0) * _sim._offside.OffsideLineDepth(side));
            }
        }
    }
}
