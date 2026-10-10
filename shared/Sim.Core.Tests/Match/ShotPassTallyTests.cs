using System.Linq;
using NUnit.Framework;
using Sim.Core.Match.Analysis;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The shot, keeper and passing block of the realism harness: pooled over matches and printed
    /// next to its <see cref="RealismReference"/> band, or the spec band of R7, R9 and R10.
    /// </summary>
    [TestFixture]
    public class ShotPassTallyTests
    {
        private static ShotPassMetrics MatchA() => new ShotPassMetrics
        {
            Shots = 20, ShotsOnTarget = 7, ShotsBlocked = 5, ShotsInsideBox = 13, HeadedShots = 4,
            Saves = 5, ShotGoals = 2, SavesInsideBox = 3, ShotGoalsInsideBox = 2,
            SavesMeasured = 4, SavesReached = 3, KeeperToCrossingSumM = 4.0,
            PassesAttempted = 900, PassesCompleted = 720, LongBalls = 90, Crosses = 30,
            OpenPlaySequences = 200, OpenPlaySequencePasses = 700, TenPlusSequences = 20,
            DirectSpeedSequences = 150, DirectSpeedSumMps = 225,
            PpdaPasses = 100, PpdaActions = 10, HighTurnovers = 12, BallInPlayMinutes = 50,
            OpenPlayShots = 16, OpenPlayShotsZeroToOne = 4, OpenPlayShotsZeroToOneReboundOrHighTurnover = 2,
            OpenPlayShotsRebound = 1, OpenPlayShotsHighTurnover = 1,
            OpenPlayShotsTwo = 2, OpenPlayShotsThreePlus = 10, OpenGoalShortcutShots = 2,
        };

        private static ShotPassMetrics MatchB() => new ShotPassMetrics
        {
            Shots = 30, ShotsOnTarget = 10, ShotsBlocked = 7, ShotsInsideBox = 20, HeadedShots = 4,
            Saves = 7, ShotGoals = 1, SavesInsideBox = 4, ShotGoalsInsideBox = 1,
            SavesMeasured = 6, SavesReached = 4, KeeperToCrossingSumM = 6.0,
            PassesAttempted = 1000, PassesCompleted = 800, LongBalls = 110, Crosses = 34,
            OpenPlaySequences = 200, OpenPlaySequencePasses = 900, TenPlusSequences = 24,
            DirectSpeedSequences = 150, DirectSpeedSumMps = 255,
            PpdaPasses = 200, PpdaActions = 10, HighTurnovers = 16, BallInPlayMinutes = 60,
            OpenPlayShots = 24, OpenPlayShotsZeroToOne = 6, OpenPlayShotsZeroToOneReboundOrHighTurnover = 3,
            OpenPlayShotsRebound = 2, OpenPlayShotsHighTurnover = 1,
            OpenPlayShotsTwo = 4, OpenPlayShotsThreePlus = 14, OpenGoalShortcutShots = 3,
        };

        private static ShotPassTally Tally()
        {
            var tally = new ShotPassTally();
            tally.Add(MatchA());
            tally.Add(MatchB());
            return tally;
        }

        [Test]
        public void Rows_PoolEveryRatio_AndScaleCountsPerMatchOrPerTeam()
        {
            ShotPassTally tally = Tally();
            var rows = tally.Rows().ToDictionary(r => r.Name);

            Assert.That(tally.Matches, Is.EqualTo(2));
            AssertRow(rows[RealismReference.Shots.Name], 25.0, true);
            AssertRow(rows[RealismReference.OnTargetPercent.Name], 34.0, true);          // 17 / 50
            AssertRow(rows[RealismReference.BlockedPercent.Name], 24.0, true);           // 12 / 50
            AssertRow(rows[RealismReference.InsideBoxPercent.Name], 66.0, true);         // 33 / 50
            AssertRow(rows[RealismReference.HeadedShots.Name], 4.0, true);
            AssertRow(rows[RealismReference.SaveRatePercent.Name], 80.0, false);         // 12 / 15
            AssertRow(rows[RealismReference.SaveRateInsideBoxPercent.Name], 70.0, false); // 7 / 10
            AssertRow(rows[RealismReference.SaveRateOutsideBoxPercent.Name], 100.0, false); // 5 / 5
            AssertRow(rows[ShotPassBands.KeeperReachPercent.Name], 70.0, true);         // 7 / 10
            AssertRow(rows[RealismReference.PassesPerTeam.Name], 475.0, false);          // 1900 / 4, under the 81-minute 550-750 (user decision 2026-10-10, #82)
            AssertRow(rows[RealismReference.PassAccuracyPercent.Name], 80.0, true);
            AssertRow(rows[RealismReference.PassesPerSequence.Name], 4.0, true);         // 1600 / 400
            AssertRow(rows[RealismReference.TenPlusSequencesPerTeam.Name], 11.0, true);  // 44 / 4
            AssertRow(rows[RealismReference.DirectSpeed.Name], 1.6, true);               // 480 / 300
            AssertRow(rows[RealismReference.Ppda.Name], 15.0, false);                    // 300 / 20
            AssertRow(rows[RealismReference.CrossesPerTeam.Name], 16.0, true);           // 64 / 4
            AssertRow(rows[RealismReference.BallInPlayMinutes.Name], 55.0, true);
            AssertRow(rows[ShotPassBands.OpenPlayShotsZeroToOnePercent.Name], 25.0, true);      // 10 / 40
            AssertRow(rows[ShotPassBands.OpenPlayShotsZeroToOneExclPercent.Name], 100.0 / 7, true);  // 5 / (40 - 5)
            AssertRow(rows[ShotPassBands.OpenPlayShotsThreePlusPercent.Name], 60.0, true);      // 24 / 40
            AssertRow(rows[ShotPassBands.OpenGoalShortcutPercent.Name], 10.0, true);            // 5 / 50

            Assert.That(rows[ShotPassTally.KeeperToCrossingName].Value, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(rows[ShotPassTally.KeeperToCrossingName].Band, Is.Null);
            Assert.That(rows[ShotPassTally.LongBallShareName].Value, Is.EqualTo(200.0 / 19).Within(1e-9));
            Assert.That(rows[ShotPassTally.HighTurnoversName].Value, Is.EqualTo(14.0).Within(1e-9));
            Assert.That(rows[ShotPassTally.OpenPlayShotsTwoName].Value, Is.EqualTo(15.0).Within(1e-9));
        }

        [Test]
        public void Rows_CoverEveryShotAndPassingBandOfRealismReference()
        {
            var names = Tally().Rows().Select(r => r.Name).ToHashSet();
            RealismBand[] owned =
            {
                RealismReference.Shots, RealismReference.OnTargetPercent, RealismReference.BlockedPercent,
                RealismReference.InsideBoxPercent, RealismReference.HeadedShots, RealismReference.SaveRatePercent,
                RealismReference.SaveRateInsideBoxPercent, RealismReference.SaveRateOutsideBoxPercent,
                RealismReference.PassesPerTeam, RealismReference.PassAccuracyPercent,
                RealismReference.PassesPerSequence, RealismReference.TenPlusSequencesPerTeam,
                RealismReference.DirectSpeed, RealismReference.Ppda, RealismReference.CrossesPerTeam,
                RealismReference.BallInPlayMinutes,
            };

            foreach (RealismBand band in owned)
                Assert.That(names, Does.Contain(band.Name), band.Name);
        }

        [Test]
        public void Format_PrintsEachReadingNextToItsBand()
        {
            string text = Tally().Format("V11");

            Assert.That(text, Does.Contain("V11"));
            Assert.That(text, Does.Contain(RealismReference.Ppda.Name));
            Assert.That(text, Does.Contain(RealismReference.Ppda.Describe()));
            Assert.That(text, Does.Contain(ShotPassBands.OpenGoalShortcutPercent.Describe()));
            Assert.That(text, Does.Contain("OUT"));
            Assert.That(text, Does.Contain("IN"));
            Assert.That(text, Does.Contain(ShotPassTally.LongBallShareName));
        }

        private static void AssertRow(ShotPassRow row, double value, bool inBand)
        {
            Assert.That(row.Value, Is.EqualTo(value).Within(1e-9), row.Name);
            Assert.That(row.InBand, Is.EqualTo(inBand), row.Name);
        }
    }
}
