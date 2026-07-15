namespace Fts.Services.Online
{
    /// <summary>
    /// App-scope hand-off telling the online replay screen which fixture to fetch and render (Phase 8.3b).
    /// The navigator's <c>Push&lt;T&gt;</c> resolves presenters with no arguments, so the Season screen
    /// stamps the target here before pushing — the same pattern as <see cref="LeagueSelection"/> / the SP
    /// PlayerProfileTarget. Club names are carried so the replay HUD can label the scoreboard without an
    /// extra lookup.
    /// </summary>
    public sealed class SeasonReplayTarget
    {
        public string LeagueId { get; private set; }
        public string FixtureId { get; private set; }
        public string HomeName { get; private set; }
        public string AwayName { get; private set; }

        public void Set(string leagueId, string fixtureId, string homeName, string awayName)
        {
            LeagueId = leagueId;
            FixtureId = fixtureId;
            HomeName = homeName;
            AwayName = awayName;
        }
    }
}
