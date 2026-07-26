namespace Fts.Services.Online
{
    /// <summary>
    /// App-scope hand-off telling the ranked replay screen which fixture to fetch + render (Phase 9.2).
    /// The navigator's <c>Push&lt;T&gt;</c> resolves presenters with no arguments, so the ranked season
    /// screen stamps the target here before pushing — the same pattern as <see cref="SeasonReplayTarget"/>
    /// (the private-league one), but ranked fixtures are addressed by fixture id alone (no league id).
    /// </summary>
    public sealed class RankedReplayTarget
    {
        public string FixtureId { get; private set; }
        public string HomeName { get; private set; }
        public string AwayName { get; private set; }

        public void Set(string fixtureId, string homeName, string awayName)
        {
            FixtureId = fixtureId;
            HomeName = homeName;
            AwayName = awayName;
        }
    }
}
