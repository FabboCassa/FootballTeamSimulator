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
        /// The engine v11 brain (watchable-match spec). It takes over one decision at a time in the
        /// tasks that follow; until a decision is its own it is v10's, so a match played on it is
        /// the v10 match draw for draw and the golden master stays on v10 until v11 replaces it.
        /// The team phase (R1) is read here once a tick; it only reads the ball, so it cannot
        /// move a draw.
        /// </summary>
        private sealed class V11Brain : IMatchBrain
        {
            private readonly MatchSimulator _sim;
            private readonly V10Brain _v10;
            private readonly TeamPhaseMachine _phases;
            private readonly int[] _phaseTicks = new int[SideCount * TeamPhaseMachine.PhaseCount];

            public V11Brain(MatchSimulator sim)
            {
                _sim = sim;
                _v10 = new V10Brain(sim);
                _phases = new TeamPhaseMachine(sim._cfg);
            }

            public int PhaseTicks(int side, TeamPhase phase) =>
                _phaseTicks[side * TeamPhaseMachine.PhaseCount + (int)phase];

            public void Begin()
            {
                _phases.Reset();
                System.Array.Clear(_phaseTicks, 0, _phaseTicks.Length);
                _v10.Begin();
            }

            public void UpdateTeams(int tick)
            {
                MatchBall ball = _sim._ball;
                _phases.Update(tick, U.Dm(ball.X), ball.OwnerSide, ball.Dead, _sim._ctx.DeadSide);
                for (int side = 0; side < SideCount; side++)
                    _phaseTicks[side * TeamPhaseMachine.PhaseCount + (int)_phases.PhaseOf(side)]++;

                _v10.UpdateTeams(tick);
            }

            public void Act(int tick, int side, int slot) => _v10.Act(tick, side, slot);

            public void Move(int tick, int side, int slot) => _v10.Move(tick, side, slot);
        }
    }
}
