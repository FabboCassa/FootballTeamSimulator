namespace Sim.Core.Difficulty
{
    /// <summary>
    /// The resolved difficulty knobs for the chosen <see cref="DifficultyLevel"/> (task 5.7),
    /// produced by <see cref="DifficultyModel.Resolve"/> from the per-level rows in
    /// <see cref="Config.DifficultyBalance"/>. A plain value object the host carries for the
    /// life of the career and feeds into the per-club budget seeding, the per-match AI lineup
    /// context and the board-patience config transform.
    /// </summary>
    public readonly struct DifficultySettings
    {
        public readonly DifficultyLevel Level;

        /// <summary>Chance (percent) an AI club fields the best player per slot (100 = always best XI).</summary>
        public readonly int AiLineupCompetence;

        /// <summary>Most ranks a slot can slip below best when an AI competence roll misses.</summary>
        public readonly int AiLineupMaxSlips;

        /// <summary>Multiplier (1/1000) on the user club's seeded transfer budget.</summary>
        public readonly int UserBudgetPermille;

        /// <summary>Multiplier (1/1000) on every AI club's seeded transfer budget (market aggressiveness).</summary>
        public readonly int AiBudgetPermille;

        /// <summary>Multiplier (1/1000) on the board's confidence swing per position vs objective.</summary>
        public readonly int BoardReactivityPermille;

        public DifficultySettings(
            DifficultyLevel level,
            int aiLineupCompetence,
            int aiLineupMaxSlips,
            int userBudgetPermille,
            int aiBudgetPermille,
            int boardReactivityPermille)
        {
            Level = level;
            AiLineupCompetence = aiLineupCompetence;
            AiLineupMaxSlips = aiLineupMaxSlips;
            UserBudgetPermille = userBudgetPermille;
            AiBudgetPermille = aiBudgetPermille;
            BoardReactivityPermille = boardReactivityPermille;
        }
    }

    /// <summary>
    /// The slice of difficulty the match path needs (task 5.7): which club is the human (never
    /// degraded) and how competent the AI managers are at picking their XI. Passed into
    /// <see cref="Career.SeasonProgressor"/>; a null context = the byte-identical pre-5.7 path
    /// (every club fields its best XI).
    /// </summary>
    public readonly struct DifficultyContext
    {
        public readonly int HumanClubId;
        public readonly int AiLineupCompetence;
        public readonly int AiLineupMaxSlips;

        public DifficultyContext(int humanClubId, int aiLineupCompetence, int aiLineupMaxSlips)
        {
            HumanClubId = humanClubId;
            AiLineupCompetence = aiLineupCompetence;
            AiLineupMaxSlips = aiLineupMaxSlips;
        }
    }
}
