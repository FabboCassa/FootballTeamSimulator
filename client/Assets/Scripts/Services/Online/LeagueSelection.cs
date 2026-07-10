namespace Fts.Services.Online
{
    /// <summary>
    /// App-scope hand-off carrying which league the lobby screen should open (Phase 8.1b). The
    /// navigator's <c>Push&lt;T&gt;</c> resolves presenters with no arguments, so the opener stamps the
    /// selection here first (the same pattern as the SP <c>PlayerProfileTarget</c>). An optional
    /// <see cref="Preloaded"/> detail lets create/join skip a redundant GET — the lobby consumes it once.
    /// </summary>
    public sealed class LeagueSelection
    {
        public string LeagueId { get; private set; }

        /// <summary>A detail already fetched by create/join, used once by the lobby then cleared.</summary>
        public LeagueDetailDto Preloaded { get; private set; }

        /// <summary>Open a league by id (the lobby will GET its detail).</summary>
        public void Select(string leagueId)
        {
            LeagueId = leagueId;
            Preloaded = null;
        }

        /// <summary>Open a league from an already-fetched detail (no extra GET).</summary>
        public void Select(LeagueDetailDto detail)
        {
            LeagueId = detail?.league?.id;
            Preloaded = detail;
        }

        /// <summary>Consumes the preloaded detail (returns it once, then forgets it).</summary>
        public LeagueDetailDto TakePreloaded()
        {
            var d = Preloaded;
            Preloaded = null;
            return d;
        }
    }
}
