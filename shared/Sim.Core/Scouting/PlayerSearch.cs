using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>How a page of search results is ordered (task 11.3). Every order is TOTAL — ties break on player id — so paging is stable and a page boundary never loses or repeats a name.</summary>
    public enum PlayerSearchSort
    {
        /// <summary>Most famous first: the default, because it is the order in which a manager would ever have heard of them.</summary>
        Fame = 0,

        /// <summary>Best first ON THE ESTIMATE the club currently believes — never on the truth.</summary>
        Ability = 1,

        /// <summary>Highest believed potential first (same caveat: the estimate, at the club's knowledge).</summary>
        Potential = 2,

        /// <summary>Youngest first.</summary>
        Age = 3,

        /// <summary>Most expensive first.</summary>
        Value = 4,

        /// <summary>Alphabetical by surname (ordinal, so the order is identical on every platform).</summary>
        Name = 5
    }

    /// <summary>
    /// One world-search request (task 11.3). Everything is optional: an empty query over a Large
    /// database is "show me all 26,000 players, most famous first", which is exactly what the screen
    /// opens on — and it costs one pass plus one sort, not a screenful of UI per player, because the
    /// result is PAGED.
    ///
    /// Ability and potential filters run on the club's ESTIMATE, at its current knowledge, like every
    /// other scouting read since task 5.4 — so a search can never leak a hidden value.
    /// </summary>
    public sealed class PlayerSearchQuery
    {
        /// <summary>Restrict to a club, a nation or a continent; null searches the whole world.</summary>
        public ScoutingArea? Area { get; set; }

        /// <summary>The same filter object the scouts' briefs use, so a promising search converts into a brief unchanged.</summary>
        public ScoutingFilters Filters { get; set; } = new ScoutingFilters();

        /// <summary>Free text matched against first or last name, case-insensitive and ordinal.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Only players at least this famous (0 = everyone, including the anonymous).</summary>
        public int MinFame { get; set; }

        /// <summary>Only players the club has actually watched — its own scouting, not public reputation.</summary>
        public bool ScoutedOnly { get; set; }

        public PlayerSearchSort Sort { get; set; } = PlayerSearchSort.Fame;

        /// <summary>Zero-based page index; clamped into range by the search.</summary>
        public int Page { get; set; }

        public int PageSize { get; set; } = 20;

        /// <summary>The club doing the looking: its bias, its knowledge, and (below) its own squad.</summary>
        public int ObserverClubId { get; set; }

        /// <summary>Leave the observer's own players out — you do not scout your own squad.</summary>
        public bool ExcludeOwnClub { get; set; } = true;
    }

    /// <summary>One row of a search page: who he is and what this club currently believes about him.</summary>
    public sealed class PlayerSearchHit
    {
        public Player Player { get; set; } = null!;
        public Club? Club { get; set; }
        public League? League { get; set; }

        public int PlayerId { get; set; }
        public int ClubId { get; set; }

        /// <summary>0..100. How publicly known he is (see <see cref="PublicKnowledge"/>).</summary>
        public int Fame { get; set; }

        /// <summary>Coarse fame band 0..3 for the label.</summary>
        public int FameTier { get; set; }

        /// <summary>The knowledge the bands below were built at: the better of what the club learned and what fame gives away.</summary>
        public int Knowledge { get; set; }

        /// <summary>What the club has actually scouted, ignoring fame — what the "scouted" bar means.</summary>
        public int ScoutedKnowledge { get; set; }

        public ScoutedRange Overall { get; set; }
        public ScoutedRange Potential { get; set; }
    }

    /// <summary>One page of results, plus how many there are in total (the count the UI prints).</summary>
    public sealed class PlayerSearchPage
    {
        public List<PlayerSearchHit> Hits { get; set; } = new List<PlayerSearchHit>();

        /// <summary>Matches in the WHOLE world, not on this page.</summary>
        public int Total { get; set; }

        /// <summary>Zero-based, already clamped into [0, PageCount - 1].</summary>
        public int Page { get; set; }

        public int PageCount { get; set; }

        /// <summary>How many index entries the query had to touch — the number the performance check watches.</summary>
        public int Scanned { get; set; }
    }
}
