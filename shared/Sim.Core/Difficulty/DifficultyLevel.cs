namespace Sim.Core.Difficulty
{
    /// <summary>
    /// Single-player difficulty (task 5.7, ARCHITECTURE.md §7). Chosen at career creation and
    /// fixed for the save. Difficulty NEVER cheats: it changes how well the AI plays (lineup
    /// competence, market aggressiveness), how patient the board is, and how much money the user
    /// starts with — never a hidden multiplier on AI strength or a secret penalty on the user.
    ///
    /// Stable numeric values so they double as save keys. Normal is the intended default.
    /// </summary>
    public enum DifficultyLevel
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }
}
