namespace Sim.Core.Domain
{
    /// <summary>
    /// A coach - the user or an AI opponent. Coach-career data (task 5.6) grows here.
    /// </summary>
    public sealed class Coach
    {
        private int _reputation = 50;
        private int _boardConfidence = 50;

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsHuman { get; set; }

        /// <summary>
        /// Standing in the football world, in [0, 100]. Slow-moving: it grows when a coach
        /// beats his board objective season after season and slips when he falls short.
        /// Drives the quality of job offers (task 5.6) — a big club only approaches a
        /// high-reputation coach. Distinct from <see cref="BoardConfidence"/> (which is the
        /// "how am I doing right now at THIS club" meter that drives sackings).
        /// </summary>
        public int Reputation { get => _reputation; set => _reputation = AttributeScale.ClampCondition(value); }

        /// <summary>
        /// The board's faith in the coach at his current club, in [0, 100] (task 5.6). It moves
        /// toward the board's expectation each evaluation: finishing below the objective drains it,
        /// finishing above replenishes it. A low value is a WARNING; a very low value triggers a
        /// sacking (see <see cref="Config.CareerBalance"/>). The per-evaluation change is capped so
        /// confidence can never fall from a healthy level straight into the sack band in one step —
        /// a warning season always precedes a sacking (the 5.6 acceptance).
        /// Additive — defaults to the neutral 50, so it rides existing Coach serialization with no
        /// save bump.
        /// </summary>
        public int BoardConfidence { get => _boardConfidence; set => _boardConfidence = AttributeScale.ClampCondition(value); }

        /// <summary>
        /// The league position (1-based) the board expects the coach to reach this season, set by
        /// <see cref="Career.BoardModel"/> from squad strength, club history and last year's finish.
        /// 0 = unset (no objective yet). Additive — defaults 0, no save bump.
        /// </summary>
        public int ObjectiveExpectedPosition { get; set; }

        /// <summary>
        /// Where the coach's club actually finished last season (1-based), used to set the next
        /// objective and to show career history. 0 = no prior season. Additive — defaults 0, no save bump.
        /// </summary>
        public int LastFinishPosition { get; set; }
    }
}
