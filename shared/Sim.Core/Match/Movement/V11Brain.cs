namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>
        /// The engine v11 brain (watchable-match spec). It takes over one decision at a time in the
        /// tasks that follow; until a decision is its own it is v10's, so a match played on it is
        /// the v10 match draw for draw and the golden master stays on v10 until v11 replaces it.
        /// </summary>
        private sealed class V11Brain : IMatchBrain
        {
            private readonly V10Brain _v10;

            public V11Brain(MatchSimulator sim)
            {
                _v10 = new V10Brain(sim);
            }

            public void Begin() => _v10.Begin();

            public void UpdateTeams(int tick) => _v10.UpdateTeams(tick);

            public void Act(int tick, int side, int slot) => _v10.Act(tick, side, slot);

            public void Move(int tick, int side, int slot) => _v10.Move(tick, side, slot);
        }
    }
}
