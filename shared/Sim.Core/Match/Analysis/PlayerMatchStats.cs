using System.Collections.Generic;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// One man's match (engine phase 7, docs/engine/MATCH_ENGINE_PLAN.md §4).
    ///
    /// Every figure here is COUNTED OFF THE PICTURE — the ball's action list, the per-frame owner
    /// track and the position arrays — and not reported by the simulator as it goes. That is the
    /// whole design: the statistics are a READING of the match, taken after it, so building them
    /// cannot consume a random draw, cannot reorder one, and cannot move a result. It is the same
    /// contract <see cref="MatchAnalyzer"/> has held since phase 0, applied one player at a time
    /// instead of one side at a time.
    ///
    /// A player who came on as a substitute has his own entry: a slot's occupancy changes at a
    /// minute boundary (<see cref="SlotChange"/>) and everything that happens in that slot is
    /// credited to whoever was standing in it at the time.
    ///
    /// Serializable POCO: it travels inside a <see cref="MatchReport"/>, is written by
    /// System.Text.Json on the server and read by Newtonsoft on the client, so no attributes and
    /// no types beyond int/bool/List.
    /// </summary>
    public sealed class PlayerMatchStats
    {
        public int PlayerId { get; set; }
        public int ClubId { get; set; }

        /// <summary>True when he played for the home side.</summary>
        public bool Home { get; set; }

        /// <summary>The lineup slot he occupied, and his shirt number.</summary>
        public int Slot { get; set; }
        public int Shirt { get; set; }

        /// <summary>True for the man the stream shows keeping goal (inferred, as in <see cref="MatchAnalyzer"/>).</summary>
        public bool Keeper { get; set; }

        /// <summary>Minute he came on and minute he came off (90, or the minute of his red card).</summary>
        public int FromMinute { get; set; }
        public int ToMinute { get; set; }

        public int MinutesPlayed => ToMinute - FromMinute;

        /// <summary>Ground covered, in decimetres — the stream's own unit, summed frame by frame with integer maths.</summary>
        public int DistanceDm { get; set; }

        /// <summary>Average position over the frames he was on the pitch, in decimetres.</summary>
        public int AverageXDm { get; set; }
        public int AverageYDm { get; set; }

        // --- on the ball ---
        public int PassesAttempted { get; set; }
        public int PassesCompleted { get; set; }
        public int LongBalls { get; set; }
        public int Crosses { get; set; }

        /// <summary>A completed pass whose receiver struck the ball before the side lost it.</summary>
        public int KeyPasses { get; set; }

        /// <summary>Runs with the ball long enough for the stream to record one (BallActionKind.Dribble).</summary>
        public int Carries { get; set; }

        /// <summary>Ticks he spent holding the ball, and the share of his side's possession that is.</summary>
        public int PossessionFrames { get; set; }

        public int Shots { get; set; }
        public int ShotsOnTarget { get; set; }

        /// <summary>Expected goals from his strikes, in thousandths. See <see cref="ExpectedGoals"/>.</summary>
        public int XgPermille { get; set; }

        public int Goals { get; set; }
        public int Assists { get; set; }

        // --- off the ball ---
        public int Tackles { get; set; }
        public int Interceptions { get; set; }
        public int Clearances { get; set; }

        /// <summary>Strikes he threw himself in front of.</summary>
        public int Blocks { get; set; }

        /// <summary>Challenges: one he won is a tackle of his, one he lost is a ball taken off him.</summary>
        public int DuelsWon { get; set; }
        public int DuelsLost { get; set; }

        // --- the keeper ---
        public int Saves { get; set; }
        public int GoalsConceded { get; set; }

        // --- the referee ---
        public int Fouls { get; set; }
        public int FoulsSuffered { get; set; }
        public int Offsides { get; set; }
        public int YellowCards { get; set; }
        public int RedCards { get; set; }

        /// <summary>
        /// The mark out of ten, in TENTHS (60 = 6.0), computed by <see cref="MatchRatingModel"/>.
        /// Integer on purpose: it is a number a host may feed back into development, so it has to
        /// reproduce bit for bit on .NET, Mono and IL2CPP.
        /// </summary>
        public int Rating { get; set; }

        /// <summary>The same mark as a reader sees it.</summary>
        public double RatingOutOfTen => Rating / 10.0;

        /// <summary>Ground covered in kilometres.</summary>
        public double DistanceKm => DistanceDm / 10000.0;

        /// <summary>Expected goals as a fraction.</summary>
        public double ExpectedGoals => XgPermille / 1000.0;

        /// <summary>
        /// The work off the ball, in one number: tackles, interceptions and clearances. What the
        /// mark out of ten weighs against the match's own average (see <see cref="MatchRatingModel"/>).
        /// Blocks are left out on purpose — they are rare enough to be worth a flat bonus.
        /// </summary>
        public int DefensiveActions => Tackles + Interceptions + Clearances;

        /// <summary>Share of his attempted passes that reached a team-mate.</summary>
        public double PassAccuracyPercent =>
            PassesAttempted <= 0 ? 0 : 100.0 * PassesCompleted / PassesAttempted;
    }

    /// <summary>
    /// One line of the pass map: how often this slot played the ball to that one, and how often it
    /// arrived. Slots rather than player ids because the map is a picture of the SHAPE — who
    /// combines with whom — and a substitution does not redraw it.
    /// </summary>
    public sealed class PassLink
    {
        public int FromSlot { get; set; }
        public int ToSlot { get; set; }
        public int Attempted { get; set; }
        public int Completed { get; set; }
    }

    /// <summary>
    /// One side's match as a coach reads it (engine phase 7): how much of the ball, where it was
    /// played, what the shape looked like while it was won and while it was lost, and who combined
    /// with whom. Counted off the picture, like everything else here.
    /// </summary>
    public sealed class TeamMatchStats
    {
        public int ClubId { get; set; }
        public bool Home { get; set; }

        public int Goals { get; set; }
        public int Shots { get; set; }
        public int ShotsOnTarget { get; set; }
        public int XgPermille { get; set; }

        public int PassesAttempted { get; set; }
        public int PassesCompleted { get; set; }

        /// <summary>Share of the frames somebody held the ball on which it was this side, in thousandths.</summary>
        public int PossessionPermille { get; set; }

        /// <summary>
        /// Where the ball was, in thousandths of the frames, from THIS side's point of view:
        /// its own third, the middle, and the third it attacks.
        /// </summary>
        public int OwnThirdPermille { get; set; }
        public int MiddleThirdPermille { get; set; }
        public int FinalThirdPermille { get; set; }

        /// <summary>The block's extent in decimetres, averaged over the frames the side spent in each phase.</summary>
        public int DefendingWidthDm { get; set; }
        public int DefendingDepthDm { get; set; }
        public int AttackingWidthDm { get; set; }
        public int AttackingDepthDm { get; set; }

        /// <summary>
        /// How high the side stood, in decimetres from the goal it defends: the average X of the
        /// ten outfielders while the other side had the ball. This is the number the Mentality
        /// instruction is supposed to move (engine phase 8).
        /// </summary>
        public int DefendingHeightDm { get; set; }

        public int Fouls { get; set; }
        public int YellowCards { get; set; }
        public int RedCards { get; set; }
        public int Offsides { get; set; }
        public int Corners { get; set; }

        /// <summary>The pass map, one line per pair of slots that played together at least once.</summary>
        public List<PassLink> PassMap { get; set; } = new List<PassLink>();

        public double PassAccuracyPercent =>
            PassesAttempted <= 0 ? 0 : 100.0 * PassesCompleted / PassesAttempted;

        public double PossessionPercent => PossessionPermille / 10.0;
        public double ExpectedGoals => XgPermille / 1000.0;
    }

    /// <summary>
    /// The performance data of one match: the two tactical reports and one entry per man who set
    /// foot on the pitch (engine phase 7). Built by <see cref="MatchStatsBuilder"/> from a finished
    /// report and its stream, attached to <see cref="MatchReport.Stats"/>, and deliberately NOT
    /// part of <see cref="MatchReportHasher"/>: it is derived from the match rather than part of
    /// it, so a golden master computed before this phase still holds after it.
    /// </summary>
    public sealed class MatchStats
    {
        public TeamMatchStats Home { get; set; } = new TeamMatchStats();
        public TeamMatchStats Away { get; set; } = new TeamMatchStats();

        /// <summary>Home side first, then away; within a side, in the order the slots were filled.</summary>
        public List<PlayerMatchStats> Players { get; set; } = new List<PlayerMatchStats>();

        /// <summary>Frames the reading covers, and how many of them make a minute.</summary>
        public int Frames { get; set; }
        public int FramesPerMinute { get; set; }

        /// <summary>The stats of one player, or null when he was not on the pitch.</summary>
        public PlayerMatchStats? Player(int playerId)
        {
            for (int i = 0; i < Players.Count; i++)
                if (Players[i].PlayerId == playerId) return Players[i];

            return null;
        }
    }
}
