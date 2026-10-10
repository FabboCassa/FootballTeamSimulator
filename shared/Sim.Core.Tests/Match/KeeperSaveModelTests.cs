using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R7 of docs/specs/real-match-and-playing-styles.md: the odds a keeper saves a strike on target
    /// depend on its zone, distance, angle, the pressure on the striker and the keeper's rating, and
    /// the keeper reaches the ball (within 1.5 m of the crossing point or the ball on ≥ 70% of saves).
    /// </summary>
    [TestFixture]
    public class KeeperSaveModelTests
    {
        private const int Average = 50;
        private const int Seeds = 2000;

        private static MatchBalance Cfg() => new BalanceConfig().Match;

        private static int Save(MatchBalance cfg, int depthDm, int offCentreDm = 0, int pressing = 0,
            int goalkeeping = Average, int offLineDm = 0, bool penalty = false) =>
            KeeperSaveModel.SavePermille(cfg, depthDm, offCentreDm, pressing, goalkeeping, offLineDm, penalty);

        [Test]
        public void CentralSixMetreShot_IsSavedLessOften_ThanATwentyFiveMetreShotAtTheKeeper()
        {
            MatchBalance cfg = Cfg();
            int close = Save(cfg, depthDm: 60);
            int far = Save(cfg, depthDm: 250);

            int closeSaves = 0, farSaves = 0;
            for (ulong seed = 0; seed < Seeds; seed++)
            {
                var rng = new Pcg32(seed);
                if (KeeperSaveModel.Saves(rng, close)) closeSaves++;
                if (KeeperSaveModel.Saves(rng, far)) farSaves++;
            }

            TestContext.Out.WriteLine($"[saves] 6 m central {closeSaves}/{Seeds}, 25 m at the keeper {farSaves}/{Seeds}");
            Assert.That(farSaves - closeSaves, Is.GreaterThan(Seeds / 5), "a 25 m strike at him is the far easier save");
        }

        [Test]
        public void Zones_RankSixYardBox_BelowBox_BelowOutside_AtTheirEdges()
        {
            MatchBalance cfg = Cfg();
            Assert.That(KeeperSaveModel.ZoneOf(50, 80, false), Is.EqualTo(ShotZone.SixYardBox));
            Assert.That(KeeperSaveModel.ZoneOf(100, 150, false), Is.EqualTo(ShotZone.Box));
            Assert.That(KeeperSaveModel.ZoneOf(170, 0, false), Is.EqualTo(ShotZone.Outside));
            Assert.That(KeeperSaveModel.ZoneOf(120, 0, true), Is.EqualTo(ShotZone.Penalty));

            // Either side of each edge, the distance barely moves: the zone does.
            Assert.That(Save(cfg, 55), Is.LessThan(Save(cfg, 56)), "six-yard box to box");
            Assert.That(Save(cfg, 165), Is.LessThan(Save(cfg, 166)), "box to outside");
        }

        [Test]
        public void EachFactor_MovesTheOdds_TheWayFootballDoes()
        {
            MatchBalance cfg = Cfg();
            int baseline = Save(cfg, depthDm: 120, offCentreDm: 40);

            Assert.That(Save(cfg, 150, 40), Is.GreaterThan(baseline), "further out is easier");
            Assert.That(Save(cfg, 40, 120), Is.GreaterThan(baseline), "a tighter angle, as far out, is easier");
            Assert.That(Save(cfg, 120, 40, pressing: 2), Is.GreaterThan(baseline), "a pressed strike is easier");
            Assert.That(Save(cfg, 120, 40, goalkeeping: 85), Is.GreaterThan(baseline), "a better keeper saves more");
            Assert.That(Save(cfg, 120, 40, goalkeeping: 15), Is.LessThan(baseline), "a worse keeper saves less");
            Assert.That(Save(cfg, 120, 40, offLineDm: 25), Is.LessThan(baseline), "a strike away from him is harder");
        }

        [Test]
        public void Penalty_ReadsTheSpotAndTheKeeper_NotThePitch()
        {
            MatchBalance cfg = Cfg();
            Assert.That(Save(cfg, 110, penalty: true), Is.EqualTo(cfg.SavePenaltyPermille));
            Assert.That(Save(cfg, 110, pressing: 3, penalty: true), Is.EqualTo(cfg.SavePenaltyPermille));
            Assert.That(Save(cfg, 110, goalkeeping: 90, penalty: true), Is.GreaterThan(cfg.SavePenaltyPermille));
        }

        [Test]
        public void Odds_AreTunableInBalanceConfig_AndClamped()
        {
            MatchBalance cfg = Cfg();
            int before = Save(cfg, 120);
            cfg.SaveBoxPermille += 100;
            Assert.That(Save(cfg, 120), Is.EqualTo(before + 100));

            cfg.SaveMaxPermille = 600;
            Assert.That(Save(cfg, 300, goalkeeping: 100), Is.EqualTo(600));
            cfg.SaveMinPermille = 200;
            Assert.That(Save(cfg, 20, goalkeeping: 1, offLineDm: 60), Is.EqualTo(200));
        }

        // ------------------------------------------------------------------ played matches

        private const int Matches = 8;
        private const ulong FirstSeed = 76_000;

        [Test]
        public void PlayedMatches_KeeperIsWithinOneAndAHalfMetres_OnAtLeastSeventyPercentOfSaves()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Lineup a = LineupSelector.BestEleven(league.Clubs[9]), b = LineupSelector.BestEleven(league.Clubs[10]);
            var cfg = new BalanceConfig();

            var metrics = new ShotPassMetrics[Matches];
            Parallel.For(0, Matches, i =>
            {
                var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: true, generatePositions: true);
                MatchReport r = i % 2 == 0
                    ? engine.Simulate(a, b, new Pcg32(FirstSeed + (ulong)i))
                    : engine.Simulate(b, a, new Pcg32(FirstSeed + (ulong)i));
                metrics[i] = new ShotPassAnalyzer().Measure(r)!;
            });

            int measured = 0, reached = 0;
            foreach (ShotPassMetrics m in metrics)
            {
                measured += m.SavesMeasured;
                reached += m.SavesReached;
            }

            double percent = 100.0 * reached / measured;
            TestContext.Out.WriteLine($"[keeper-reach] {reached}/{measured} saves within 1.5 m ({percent:F1}%)");
            Assert.That(measured, Is.GreaterThanOrEqualTo(20), "enough saves to read");
            Assert.That(percent, Is.GreaterThanOrEqualTo(ShotPassBands.KeeperReachPercent.Min));

            // He runs at the strike's line, so nearly every save is made where he stands: without
            // that run (keeper on the ball-goal line only, #75) these seeds read 77.8%.
            Assert.That(percent, Is.GreaterThanOrEqualTo(RunAtTheLinePercent), "he goes to the strike's line");
        }

        private const double RunAtTheLinePercent = 90.0;
    }
}
