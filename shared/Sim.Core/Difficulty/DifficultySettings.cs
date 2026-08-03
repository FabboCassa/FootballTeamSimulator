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

        /// <summary>How willing an AI manager is (percent) to rest tired players — the task-10.1 rotation
        /// lever, independent of competence so a weaker AI can no longer profit from fresher legs.</summary>
        public readonly int AiRotationPercent;

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
            int boardReactivityPermille,
            int aiRotationPercent = 0)
        {
            Level = level;
            AiLineupCompetence = aiLineupCompetence;
            AiLineupMaxSlips = aiLineupMaxSlips;
            AiRotationPercent = aiRotationPercent;
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

        /// <summary>How this level's AI managers handle tired players (task 10.1). Default
        /// <see cref="Match.RotationPolicy.None"/> = the pre-10.1 behaviour, so an omitted policy changes
        /// nothing.</summary>
        public readonly Match.RotationPolicy Rotation;

        public DifficultyContext(
            int humanClubId, int aiLineupCompetence, int aiLineupMaxSlips,
            Match.RotationPolicy rotation = default)
        {
            HumanClubId = humanClubId;
            AiLineupCompetence = aiLineupCompetence;
            AiLineupMaxSlips = aiLineupMaxSlips;
            Rotation = rotation;
        }

        /// <summary>True when this level asks an AI manager to do anything other than field his best XI.</summary>
        public bool DegradesAiLineups => AiLineupCompetence < 100 || Rotation.IsActive;
    }
}
