using System;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The R19 bench (issue #94) without the 500-match harness: the per-side split of
    /// <see cref="ShotPassAnalyzer"/>, the fingerprint table and its verdicts.
    /// </summary>
    [TestFixture]
    public class StyleFingerprintsTests
    {
        [Test]
        public void MeasureSide_SplitsTheMatch_TheTwoSidesAddUpToBoth()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            var cfg = new BalanceConfig();
            MatchReport report = new MatchEngine().Simulate(
                LineupSelector.BestEleven(league.Clubs[9]), LineupSelector.BestEleven(league.Clubs[10]),
                new Pcg32(41_000),
                new MatchTactics(
                    new TacticContext(new Tactic(Formation.F433, PlayingStyles.Get(PlayingStyle.Gegenpress).Instructions),
                        cfg.Tactics.FamiliarityMax),
                    TacticContext.Neutral(cfg.Tactics.FamiliarityMax)));

            var analyzer = new ShotPassAnalyzer();
            ShotPassMetrics both = analyzer.Measure(report)!;
            ShotPassMetrics home = analyzer.MeasureSide(report, home: true)!;
            ShotPassMetrics away = analyzer.MeasureSide(report, home: false)!;

            Assert.That(home.PassesAttempted, Is.GreaterThan(0));
            Assert.That(away.PassesAttempted, Is.GreaterThan(0));
            Assert.That(home.PassesAttempted + away.PassesAttempted, Is.EqualTo(both.PassesAttempted));
            Assert.That(home.PassesCompleted + away.PassesCompleted, Is.EqualTo(both.PassesCompleted));
            Assert.That(home.LongBalls + away.LongBalls, Is.EqualTo(both.LongBalls));
            Assert.That(home.Crosses + away.Crosses, Is.EqualTo(both.Crosses));
            Assert.That(home.Shots + away.Shots, Is.EqualTo(both.Shots));
            Assert.That(home.OpenPlaySequences + away.OpenPlaySequences, Is.EqualTo(both.OpenPlaySequences));
            Assert.That(home.OpenPlaySequencePasses + away.OpenPlaySequencePasses, Is.EqualTo(both.OpenPlaySequencePasses));
            Assert.That(home.TenPlusSequences + away.TenPlusSequences, Is.EqualTo(both.TenPlusSequences));
            Assert.That(home.DirectSpeedSequences + away.DirectSpeedSequences, Is.EqualTo(both.DirectSpeedSequences));
            Assert.That(home.HighTurnovers + away.HighTurnovers, Is.EqualTo(both.HighTurnovers));
            Assert.That(home.PpdaPasses + away.PpdaPasses, Is.EqualTo(both.PpdaPasses));
            Assert.That(home.PpdaActions + away.PpdaActions, Is.EqualTo(both.PpdaActions));
            Assert.That(home.OpenPlayShots + away.OpenPlayShots, Is.EqualTo(both.OpenPlayShots));
            Assert.That(home.BallInPlayMinutes, Is.EqualTo(both.BallInPlayMinutes), "ball in play is the match's");
        }

        [Test]
        public void MeasureSide_PpdaIsThePressingSides_OpponentPassesOverItsOwnActions()
        {
            PositionStream s = ScriptedStream.NewStream(40);
            // The away side passes twice in its own 60% (it defends the far goal); the home side then wins it there.
            ScriptedStream.Hold(s, 0, 1, false, 6, 800, Pitch.CenterY);
            ScriptedStream.Act(s, 2, BallActionKind.Pass, false, 6, 7);
            ScriptedStream.Hold(s, 4, 5, false, 7, 780, Pitch.CenterY);
            ScriptedStream.Act(s, 6, BallActionKind.Pass, false, 7, 8);
            ScriptedStream.Act(s, 8, BallActionKind.Interception, true, 8);
            ScriptedStream.Hold(s, 8, 9, true, 8, 760, Pitch.CenterY);
            var report = new MatchReport { Positions = s };

            ShotPassMetrics home = new ShotPassAnalyzer().MeasureSide(report, home: true)!;
            ShotPassMetrics away = new ShotPassAnalyzer().MeasureSide(report, home: false)!;

            Assert.That(home.PpdaPasses, Is.EqualTo(2), "the away side's passes are the home side's to press");
            Assert.That(home.PpdaActions, Is.EqualTo(1));
            Assert.That(home.PassesAttempted, Is.EqualTo(0), "the home side made no pass");
            Assert.That(away.PpdaPasses, Is.EqualTo(0));
            Assert.That(away.PpdaActions, Is.EqualTo(0));
            Assert.That(away.PassesAttempted, Is.EqualTo(2));
        }

        [Test]
        public void EveryStyleButBalanced_HasOneFingerprintOfSevenMetrics()
        {
            PlayingStyle[] styles = PlayingStyles.All.Select(d => d.Style).Where(st => st != PlayingStyle.Balanced).ToArray();

            Assert.That(StyleFingerprints.All.Select(f => f.Style), Is.EquivalentTo(styles));
            foreach (StyleFingerprint f in StyleFingerprints.All)
            {
                Assert.That(f.Metrics, Has.Count.EqualTo(7));
                Assert.That(f.Metrics.Distinct().Count(), Is.EqualTo(7), f.Style.ToString());
                Assert.That(Enumerable.Range(0, 7).Count(i => f.Direction(i) != 0),
                    Is.GreaterThanOrEqualTo(StyleFingerprints.RequiredMoves),
                    $"{f.Style} must have at least {StyleFingerprints.RequiredMoves} metrics with a direction");
            }
        }

        [Test]
        public void TheSeventhMetric_IsCrossesLongBallsOrTheLine()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StyleFingerprint(PlayingStyle.TikiTaka,
                1, 1, 1, 1, -1, -1, FingerprintMetric.Ppda, 1));
        }

        [Test]
        public void Compare_CountsAMetric_OnlyWhenItMovesItsWayByTheMinimum()
        {
            var fingerprint = new StyleFingerprint(PlayingStyle.RouteOne, -1, -1, -1, -1, +1, 0,
                FingerprintMetric.LongBallSharePercent, +1);
            var balanced = new FingerprintReading
            {
                PassesPerTeam = 600, PossessionPercent = 50, PassesPerSequence = 4.0, TenPlusSequencesPerTeam = 10,
                DirectSpeed = 3.0, Ppda = 12, LongBallSharePercent = 6,
            };
            var style = new FingerprintReading
            {
                PassesPerTeam = 570,             // -5%: exactly the minimum, moved
                PossessionPercent = 48.5,        // -1.5 pp: short of 2 pp
                PassesPerSequence = 4.5,         // the wrong way
                TenPlusSequencesPerTeam = 8,     // -20%: moved
                DirectSpeed = 3.3,               // +10%: moved
                Ppda = 20,                       // no direction: never counts
                LongBallSharePercent = 9,        // +3 pp: moved
            };

            var rows = StyleFingerprints.Compare(fingerprint, style, balanced);

            Assert.That(rows.Select(r => r.Moved), Is.EqualTo(new[] { true, false, false, true, true, false, true }));
            Assert.That(StyleFingerprints.MovedCount(rows), Is.EqualTo(4));
        }
    }
}
