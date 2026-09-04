namespace Sim.Core.Scouting
{
    /// <summary>
    /// One scout, sent somewhere, with a brief (task 11.2). This is the unit the whole scouting
    /// department is made of: the club's limiter is simply how many of these it can hold open at
    /// once, which is one per employed scout (the user's decision — no travel budget in 11.2).
    ///
    /// The assignment is INTENT and it is continuous: the scout keeps working the area week after
    /// week, deepening what the club knows about the players he has already reported and adding a
    /// few new names each week (<see cref="ScoutingProgressor.EvolveAreaWeek"/>). Knowledge itself
    /// lives in <see cref="KnowledgeStore"/> / <see cref="AreaKnowledgeStore"/> and survives the
    /// assignment being cancelled — you do not forget a country because you called your scout home.
    /// </summary>
    public sealed class ScoutingAssignment
    {
        /// <summary>
        /// The scout doing the work (<see cref="Domain.Scout.Id"/>). 0 means "the department", which
        /// is what a pre-11.2 direct watch (task 5.4b) rehydrates as: it still works, it just uses
        /// the club's baseline judging rather than a named man's attributes.
        /// </summary>
        public int ScoutId { get; set; }

        /// <summary>Where he is: a named player, a club, a nation or a continent.</summary>
        public ScoutingArea Area { get; set; } = ScoutingArea.ForPlayer(0);

        /// <summary>What we asked him to look for. An empty brief means "anyone".</summary>
        public ScoutingFilters Filters { get; set; } = new ScoutingFilters();

        /// <summary>
        /// Weeks this assignment has been running. Metadata for the UI ("3 weeks in") and a
        /// tiebreak-free way to show newest-first reports; the precision itself comes from the
        /// accumulated knowledge, not from this counter.
        /// </summary>
        public int WeeksElapsed { get; set; }

        public ScoutingAssignment Clone() => new ScoutingAssignment
        {
            ScoutId = ScoutId,
            Area = Area.Clone(),
            Filters = Filters.Clone(),
            WeeksElapsed = WeeksElapsed
        };
    }
}
