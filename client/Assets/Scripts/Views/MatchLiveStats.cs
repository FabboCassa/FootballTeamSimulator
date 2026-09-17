namespace Fts.Views
{
    /// <summary>
    /// One side's figures as the match is being watched — the live half of what
    /// <see cref="Sim.Core.Match.Analysis.TeamMatchStats"/> reports at full time.
    ///
    /// It lives in Fts.Views rather than in Fts.MatchView because the strip that DRAWS it is a
    /// view and Fts.Views cannot reference Fts.MatchView (the dependency runs the other way).
    /// The renderer, which can see both, is what fills it in.
    ///
    /// Counted as playback CROSSES the stream, never read off the finished report: the figures a
    /// watcher sees at minute 20 are the figures of the first twenty minutes, so the panel cannot
    /// give away a goal that has not happened yet.
    /// </summary>
    public struct TeamLiveStats
    {
        /// <summary>Frames this side spent holding the ball (the possession numerator).</summary>
        public int PossessionFrames;

        public int Shots;
        public int ShotsOnTarget;
        public int Goals;
        public int Corners;
        public int Fouls;
        public int YellowCards;
        public int RedCards;
        public int Offsides;
    }

    /// <summary>Both sides' live figures, plus what the possession share is taken out of.</summary>
    public struct MatchLiveStats
    {
        public TeamLiveStats Home;
        public TeamLiveStats Away;

        /// <summary>
        /// Frames somebody was on the ball. Possession is a share of THIS and not of the clock:
        /// a fifth of a match is the ball in flight or dead, and counting that against a side
        /// would make two honest numbers that never add up to 100%.
        /// </summary>
        public int OwnedFrames;

        /// <summary>Home possession as a percentage, rounded; 50 before anybody has touched it.</summary>
        public int HomePossessionPercent =>
            OwnedFrames <= 0 ? 50 : (Home.PossessionFrames * 200 + OwnedFrames) / (OwnedFrames * 2);

        public int AwayPossessionPercent => 100 - HomePossessionPercent;
    }
}
