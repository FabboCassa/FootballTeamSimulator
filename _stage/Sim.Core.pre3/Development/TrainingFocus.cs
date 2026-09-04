namespace Sim.Core.Development
{
    /// <summary>
    /// The weekly squad-wide training focus (task 4.3). Biases which group of skills
    /// the whole squad works on; <see cref="Tactical"/> trades attribute growth for
    /// faster tactic familiarity. Numeric values are stable (save/serialization keys).
    /// </summary>
    public enum TeamTrainingFocus
    {
        /// <summary>Even, modest growth across all skills. The AI default and a safe choice.</summary>
        Balanced = 0,
        /// <summary>Finishing &amp; forward play: shooting, dribbling, positioning.</summary>
        Attacking = 1,
        /// <summary>Defending, positioning, strength.</summary>
        Defending = 2,
        /// <summary>Pace, strength, stamina.</summary>
        Physical = 3,
        /// <summary>Technique, passing, dribbling.</summary>
        Technical = 4,
        /// <summary>Drills the current tactic: little attribute growth, faster familiarity.</summary>
        Tactical = 5
    }

    /// <summary>
    /// An optional per-player training focus layered on top of the team focus
    /// (task 4.3). Adds extra growth pressure to one attribute group for that player.
    /// <see cref="None"/> = follow the team focus only.
    /// </summary>
    public enum IndividualTrainingFocus
    {
        None = 0,
        Attacking = 1,
        Defending = 2,
        Physical = 3,
        Technical = 4
    }
}
