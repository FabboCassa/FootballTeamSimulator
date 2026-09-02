namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// What a team's SHAPE looked like over the ticks it was sampled on (phase 0 of the match
    /// engine rework, docs/engine/MATCH_ENGINE_PLAN.md).
    ///
    /// Every figure is an average over the sampled ticks and covers the ten outfielders only —
    /// a goalkeeper standing on his line would flatter the depth and ruin the back-line reading.
    /// Distances are in METRES, because the numbers are meant to be read against real football
    /// (a defending block is 30-40 m wide and 25-35 m deep) rather than against the engine's
    /// internal decimetres.
    /// </summary>
    public struct ShapeMetrics
    {
        /// <summary>Ticks this reading averages. Zero means the side was never in this phase.</summary>
        public int Samples { get; set; }

        /// <summary>Touchline-to-touchline extent of the ten outfielders.</summary>
        public double WidthM { get; set; }

        /// <summary>Goal-to-goal extent of the ten outfielders: how compact the block is.</summary>
        public double DepthM { get; set; }

        /// <summary>
        /// Spread in X of the four DEEPEST outfielders — how far the back four is from being a
        /// line. A real back four holds within about 3 m of each other; a big number here means
        /// there is no line at all.
        /// </summary>
        public double BackLineSpreadM { get; set; }

        /// <summary>
        /// Largest gap in X between two outfielders adjacent in the sorted order: the hole
        /// between the lines. Real blocks keep this near 10-14 m.
        /// </summary>
        public double LargestLineGapM { get; set; }

        /// <summary>Average distance from each outfielder to his NEAREST team-mate.</summary>
        public double NearestTeammateM { get; set; }

        /// <summary>
        /// Share of sampled (tick, outfielder) pairs whose nearest opponent is within three
        /// metres. This is the man-marking probe: blanket man-marking pushes it far above the
        /// 10-20% a zonal side produces.
        /// </summary>
        public double WithinThreeMetresOfOpponentPercent { get; set; }

        /// <summary>Team centroid, in metres from the defended goal line (X) and from the near touchline (Y).</summary>
        public double CentroidXM { get; set; }
        public double CentroidYM { get; set; }
    }

    /// <summary>One side's match, counted off the position stream. See <see cref="MatchAnalyzer"/>.</summary>
    public struct SideMetrics
    {
        public int Goals { get; set; }
        public int Shots { get; set; }
        public int ShotsOnTarget { get; set; }

        /// <summary>Every ball deliberately played to a team-mate: short, long and crosses together.</summary>
        public int PassesAttempted { get; set; }

        /// <summary>Of those, the ones the next player to touch the ball received for the same side.</summary>
        public int PassesCompleted { get; set; }

        public int LongBalls { get; set; }
        public int Crosses { get; set; }
        public int Dribbles { get; set; }
        public int Clearances { get; set; }
        public int TacklesWon { get; set; }
        public int Interceptions { get; set; }

        /// <summary>Restarts AWARDED to this side.</summary>
        public int ThrowIns { get; set; }
        public int Corners { get; set; }
        public int GoalKicks { get; set; }

        /// <summary>
        /// Offsides and fouls conceded. The engine has neither today — a zero here is the
        /// measurement, not a missing reading.
        /// </summary>
        public int Offsides { get; set; }
        public int Fouls { get; set; }

        /// <summary>Ticks on which a player of this side held the ball.</summary>
        public int PossessionTicks { get; set; }

        /// <summary>Ground covered by the whole eleven, summed, in kilometres.</summary>
        public double TeamDistanceKm { get; set; }

        /// <summary>Ground covered by the busiest and the laziest outfielder, in kilometres.</summary>
        public double MaxPlayerDistanceKm { get; set; }
        public double MinPlayerDistanceKm { get; set; }

        /// <summary>Shape while an opponent held the ball, and while this side held it.</summary>
        public ShapeMetrics Defending { get; set; }
        public ShapeMetrics Attacking { get; set; }

        /// <summary>Share of attempted passes that reached a team-mate.</summary>
        public double PassAccuracyPercent =>
            PassesAttempted <= 0 ? 0 : 100.0 * PassesCompleted / PassesAttempted;

        /// <summary>Average ground covered per outfielder plus keeper, in kilometres.</summary>
        public double DistancePerPlayerKm(int playerCount) =>
            playerCount <= 0 ? 0 : TeamDistanceKm / playerCount;
    }

    /// <summary>
    /// Everything phase 0 measures about one simulated match. Read-only: it never touches the
    /// engine, so adding a reading here can never move a result.
    /// </summary>
    public sealed class MatchMetrics
    {
        public SideMetrics Home { get; set; }
        public SideMetrics Away { get; set; }

        /// <summary>Frames in the stream, and how many of them make a match minute.</summary>
        public int Ticks { get; set; }
        public int TicksPerMinute { get; set; }
        public int PlayerCount { get; set; }

        /// <summary>Ticks on which nobody held the ball: in flight, running loose, or dead.</summary>
        public int LooseTicks { get; set; }

        /// <summary>Ticks with the ball in each third, counted from the HOME side's point of view.</summary>
        public int HomeThirdTicks { get; set; }
        public int MiddleThirdTicks { get; set; }
        public int AwayThirdTicks { get; set; }

        /// <summary>
        /// Ticks on which the ball was recorded on a boundary of the pitch WHILE a player held
        /// it. The laws say that is a throw-in or a goal kick; today the engine only tests a
        /// free ball, so a carrier is silently clamped back inside instead. A non-zero reading
        /// is that bug, counted.
        /// </summary>
        public int BallOnLineWhileHeldTicks { get; set; }

        /// <summary>
        /// Goals in the stream against goals in the report. They must agree: a difference means
        /// the picture and the result told the viewer two different stories.
        /// </summary>
        public int ReportGoals { get; set; }
        public int StreamGoals { get; set; }
        public bool GoalsAgree => ReportGoals == StreamGoals;

        public int TotalGoals => Home.Goals + Away.Goals;
        public int TotalShots => Home.Shots + Away.Shots;
        public int TotalPasses => Home.PassesAttempted + Away.PassesAttempted;
        public int TotalThrowIns => Home.ThrowIns + Away.ThrowIns;
        public int TotalCorners => Home.Corners + Away.Corners;
        public int TotalOffsides => Home.Offsides + Away.Offsides;
        public int TotalFouls => Home.Fouls + Away.Fouls;

        public double TotalPassAccuracyPercent
        {
            get
            {
                int attempted = TotalPasses;
                return attempted <= 0 ? 0 : 100.0 * (Home.PassesCompleted + Away.PassesCompleted) / attempted;
            }
        }

        /// <summary>Home possession as a share of the ticks somebody actually held the ball.</summary>
        public double HomePossessionPercent
        {
            get
            {
                int held = Home.PossessionTicks + Away.PossessionTicks;
                return held <= 0 ? 0 : 100.0 * Home.PossessionTicks / held;
            }
        }

        /// <summary>Share of frames on which nobody held the ball.</summary>
        public double LoosePercent => Ticks <= 0 ? 0 : 100.0 * LooseTicks / Ticks;
    }
}
