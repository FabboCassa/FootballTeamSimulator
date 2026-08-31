namespace Fts.Services.Online
{
    /// <summary>
    /// App-scope hand-off telling the ranked LIVE screen which fixture to attend (task 12.3). The navigator's
    /// <c>Push&lt;T&gt;</c> resolves presenters with no arguments, so the ranked season screen (or the daily
    /// digest) stamps the target here before pushing — the same pattern as <see cref="RankedReplayTarget"/>,
    /// and deliberately a SEPARATE object from it: a replay and an appointment are opposite things, and one
    /// field holding "the fixture you are about to play" or "the fixture you already missed" depending on who
    /// wrote it last is a bug waiting for a race.
    /// </summary>
    public sealed class RankedLiveTarget
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
