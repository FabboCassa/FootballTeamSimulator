namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The decision layer of <see cref="MatchSimulator"/>: how a side reads the game, what a man
    /// does with the ball, and where he goes without it. The ball's physics, the laws, the
    /// restarts and the execution of a pass, a run or a strike stay with the simulator and are the
    /// same whichever brain plays. Every call is made in a fixed order, and a brain draws from the
    /// match's one seeded source only through what it asks of the simulator.
    /// </summary>
    internal interface IMatchBrain
    {
        /// <summary>A new match: both sides take up their places for the kickoff.</summary>
        void Begin();

        /// <summary>Once a tick, before anybody acts: the shape, who chases, who supports, who marks.</summary>
        void UpdateTeams(int tick);

        /// <summary>What this man does with the ball, if it is his to play.</summary>
        void Act(int tick, int side, int slot);

        /// <summary>Where this man goes this tick, and the step that takes him there.</summary>
        void Move(int tick, int side, int slot);
    }
}
