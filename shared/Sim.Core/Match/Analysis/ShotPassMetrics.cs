namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The shot, keeper and passing counts of one match (real-match spec R2, R7, R9, R10), both
    /// sides together. See <see cref="ShotPassAnalyzer"/> for the definitions.
    ///
    /// Every field is a raw count or sum, never a ratio, so that matches pool by adding them
    /// (<see cref="Plus"/>): the ratio properties then read the pooled figure the doc's §1.5 asks
    /// for (total numerator ÷ total denominator), whether this holds one match or a thousand.
    /// </summary>
    public sealed class ShotPassMetrics
    {
        public int Matches { get; set; } = 1;

        // ------------------------------------------------------------------ shots and keepers

        public int Shots { get; set; }
        public int ShotsOnTarget { get; set; }
        public int ShotsBlocked { get; set; }
        public int ShotsInsideBox { get; set; }
        public int HeadedShots { get; set; }

        /// <summary>Strikes the keeper kept out, and strikes that went in, with the inside-the-box share of each.</summary>
        public int Saves { get; set; }
        public int ShotGoals { get; set; }
        public int SavesInsideBox { get; set; }
        public int ShotGoalsInsideBox { get; set; }

        /// <summary>R7: saves whose shot path could be read, those the keeper reached, and his summed distance to the crossing point.</summary>
        public int SavesMeasured { get; set; }
        public int SavesReached { get; set; }
        public double KeeperToCrossingSumM { get; set; }

        // ------------------------------------------------------------------ passing

        public int PassesAttempted { get; set; }
        public int PassesCompleted { get; set; }
        public int LongBalls { get; set; }
        public int Crosses { get; set; }

        public int OpenPlaySequences { get; set; }
        public int OpenPlaySequencePasses { get; set; }
        public int TenPlusSequences { get; set; }

        /// <summary>Open-play sequences of at least a second, and the sum of their progress ÷ duration.</summary>
        public int DirectSpeedSequences { get; set; }
        public double DirectSpeedSumMps { get; set; }

        public int PpdaPasses { get; set; }
        public int PpdaActions { get; set; }

        public int HighTurnovers { get; set; }
        public double BallInPlayMinutes { get; set; }

        // ------------------------------------------------------------------ R9 / R10

        public int OpenPlayShots { get; set; }
        public int OpenPlayShotsZeroToOne { get; set; }

        /// <summary>Open-play shots whose sequence was a rebound, and those whose sequence was a high turnover (never both).</summary>
        public int OpenPlayShotsRebound { get; set; }
        public int OpenPlayShotsHighTurnover { get; set; }

        /// <summary>Of the 0-1 pass open-play shots, those whose sequence was a rebound or a high turnover.</summary>
        public int OpenPlayShotsZeroToOneReboundOrHighTurnover { get; set; }
        public int OpenPlayShotsTwo { get; set; }
        public int OpenPlayShotsThreePlus { get; set; }

        public int OpenGoalShortcutShots { get; set; }

        // ------------------------------------------------------------------ readings

        public double OnTargetPercent => Percent(ShotsOnTarget, Shots);
        public double BlockedPercent => Percent(ShotsBlocked, Shots);
        public double InsideBoxPercent => Percent(ShotsInsideBox, Shots);
        public double SaveRatePercent => Percent(Saves, Saves + ShotGoals);
        public double SaveRateInsideBoxPercent => Percent(SavesInsideBox, SavesInsideBox + ShotGoalsInsideBox);

        public double SaveRateOutsideBoxPercent
        {
            get
            {
                int saves = Saves - SavesInsideBox;
                return Percent(saves, saves + ShotGoals - ShotGoalsInsideBox);
            }
        }

        public double KeeperReachPercent => Percent(SavesReached, SavesMeasured);
        public double MeanKeeperToCrossingM => SavesMeasured == 0 ? 0 : KeeperToCrossingSumM / SavesMeasured;

        public double PassAccuracyPercent => Percent(PassesCompleted, PassesAttempted);
        public double LongBallSharePercent => Percent(LongBalls, PassesAttempted);
        public double PassesPerSequence => Ratio(OpenPlaySequencePasses, OpenPlaySequences);
        public double DirectSpeed => DirectSpeedSequences == 0 ? 0 : DirectSpeedSumMps / DirectSpeedSequences;
        public double Ppda => Ratio(PpdaPasses, PpdaActions);

        public double OpenPlayShotsZeroToOnePercent => Percent(OpenPlayShotsZeroToOne, OpenPlayShots);

        /// <summary>
        /// R9's second reading: rebounds and high turnovers left out of both sides of the share,
        /// (0-1 pass shots that are neither) ÷ (open-play shots that are neither).
        /// </summary>
        public double OpenPlayShotsZeroToOneExclReboundsPercent =>
            Percent(OpenPlayShotsZeroToOne - OpenPlayShotsZeroToOneReboundOrHighTurnover,
                OpenPlayShots - OpenPlayShotsRebound - OpenPlayShotsHighTurnover);

        public double OpenPlayShotsTwoPercent => Percent(OpenPlayShotsTwo, OpenPlayShots);
        public double OpenPlayShotsThreePlusPercent => Percent(OpenPlayShotsThreePlus, OpenPlayShots);
        public double OpenGoalShortcutPercent => Percent(OpenGoalShortcutShots, Shots);

        /// <summary>A new reading holding both: every count and sum added, matches included.</summary>
        public ShotPassMetrics Plus(ShotPassMetrics o) => new ShotPassMetrics
        {
            Matches = Matches + o.Matches,
            Shots = Shots + o.Shots,
            ShotsOnTarget = ShotsOnTarget + o.ShotsOnTarget,
            ShotsBlocked = ShotsBlocked + o.ShotsBlocked,
            ShotsInsideBox = ShotsInsideBox + o.ShotsInsideBox,
            HeadedShots = HeadedShots + o.HeadedShots,
            Saves = Saves + o.Saves,
            ShotGoals = ShotGoals + o.ShotGoals,
            SavesInsideBox = SavesInsideBox + o.SavesInsideBox,
            ShotGoalsInsideBox = ShotGoalsInsideBox + o.ShotGoalsInsideBox,
            SavesMeasured = SavesMeasured + o.SavesMeasured,
            SavesReached = SavesReached + o.SavesReached,
            KeeperToCrossingSumM = KeeperToCrossingSumM + o.KeeperToCrossingSumM,
            PassesAttempted = PassesAttempted + o.PassesAttempted,
            PassesCompleted = PassesCompleted + o.PassesCompleted,
            LongBalls = LongBalls + o.LongBalls,
            Crosses = Crosses + o.Crosses,
            OpenPlaySequences = OpenPlaySequences + o.OpenPlaySequences,
            OpenPlaySequencePasses = OpenPlaySequencePasses + o.OpenPlaySequencePasses,
            TenPlusSequences = TenPlusSequences + o.TenPlusSequences,
            DirectSpeedSequences = DirectSpeedSequences + o.DirectSpeedSequences,
            DirectSpeedSumMps = DirectSpeedSumMps + o.DirectSpeedSumMps,
            PpdaPasses = PpdaPasses + o.PpdaPasses,
            PpdaActions = PpdaActions + o.PpdaActions,
            HighTurnovers = HighTurnovers + o.HighTurnovers,
            BallInPlayMinutes = BallInPlayMinutes + o.BallInPlayMinutes,
            OpenPlayShots = OpenPlayShots + o.OpenPlayShots,
            OpenPlayShotsZeroToOne = OpenPlayShotsZeroToOne + o.OpenPlayShotsZeroToOne,
            OpenPlayShotsRebound = OpenPlayShotsRebound + o.OpenPlayShotsRebound,
            OpenPlayShotsHighTurnover = OpenPlayShotsHighTurnover + o.OpenPlayShotsHighTurnover,
            OpenPlayShotsZeroToOneReboundOrHighTurnover =
                OpenPlayShotsZeroToOneReboundOrHighTurnover + o.OpenPlayShotsZeroToOneReboundOrHighTurnover,
            OpenPlayShotsTwo = OpenPlayShotsTwo + o.OpenPlayShotsTwo,
            OpenPlayShotsThreePlus = OpenPlayShotsThreePlus + o.OpenPlayShotsThreePlus,
            OpenGoalShortcutShots = OpenGoalShortcutShots + o.OpenGoalShortcutShots,
        };

        private static double Percent(int part, int whole) => whole <= 0 ? 0 : 100.0 * part / whole;

        private static double Ratio(int part, int whole) => whole <= 0 ? 0 : (double)part / whole;
    }
}
