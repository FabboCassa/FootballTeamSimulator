using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Free-ball spec R3, issue #178: a defender wins the ball only within a metre of it, so a man
    /// without the ball who is a long way off his place runs back to it (V11RecoverySprintDm)
    /// rather than jogging there as the side with the ball does (V11CatchUpSprintDm). Read off a
    /// real match with every man traced.
    /// </summary>
    [TestFixture]
    public class V11RecoveryRunTests
    {
        private const int Players = 11;

        // Off his target by 11-14 m: past the recovery run's 10 m, short of the catch-up's 15 m, a
        // metre of slack on either side because the record holds where he ended the tick.
        private const int NearDm = 110;
        private const int FarDm = 140;

        // His own place or a role job: the V11 brain's catch-up rule decides the pace there.
        private static readonly HashSet<MovementJob> OwnPlace = new HashSet<MovementJob>
        {
            MovementJob.Spot, MovementJob.Step, MovementJob.Tuck, MovementJob.Screen
        };

        private static MatchBalance _cfg = null!;
        private static List<MovementRecord> _records = null!;

        [OneTimeSetUp]
        public void PlayOneMatch()
        {
            _cfg = new BalanceConfig().Match;
            League league = new LeagueGenerator().Generate(new Pcg32(20261031));
            Lineup home = LineupSelector.BestEleven(league.Clubs[4]);
            Lineup away = LineupSelector.BestEleven(league.Clubs[7]);
            var log = new MovementLog();
            for (int side = 0; side < 2; side++)
            for (int slot = 0; slot < Players; slot++)
                log.Watch(side, slot);
            new MatchEngine(new BalanceConfig()) { MovementSink = log }.Simulate(home, away, new Pcg32(178));
            _records = log.Records.ToList();
        }

        [Test]
        public void TheBand_LiesBetweenTheRecoveryRunAndTheCatchUp()
        {
            Assert.That(_cfg.V11RecoverySprintDm, Is.LessThan(NearDm - 5));
            Assert.That(_cfg.V11CatchUpSprintDm, Is.GreaterThan(FarDm + 5));
        }

        [Test]
        public void WithoutTheBall_AManFarOffHisPlace_SprintsBackToIt()
        {
            List<MovementRecord> outOfPossession = Between(inPossession: false);
            List<MovementRecord> inPossession = Between(inPossession: true);
            int outSprints = outOfPossession.Count(r => r.Sprint);
            int inSprints = inPossession.Count(r => r.Sprint);
            TestContext.Out.WriteLine(
                $"[v11-recovery] {outSprints}/{outOfPossession.Count} sprints without the ball, " +
                $"{inSprints}/{inPossession.Count} with it, {NearDm / 10}-{FarDm / 10} m off the target");

            Assert.That(outOfPossession.Count, Is.GreaterThan(50), "the match has men that far off their place without the ball");
            Assert.That(outSprints, Is.EqualTo(outOfPossession.Count), "every one of them runs back");
            Assert.That(inPossession.Count, Is.GreaterThan(50), "and men that far off it with the ball");
            Assert.That(inSprints, Is.LessThan(inPossession.Count), "who are not all sent at a sprint");
        }

        private static List<MovementRecord> Between(bool inPossession)
        {
            const long near = NearDm, far = FarDm;
            return _records.Where(r =>
            {
                if (!r.Steered || r.InPossession != inPossession || r.BallDead) return false;
                if (r.BaseJob != MovementJob.Spot || !OwnPlace.Contains(r.Job)) return false;
                long dx = r.TargetXDm - r.XDm, dy = r.TargetYDm - r.YDm;
                long sq = dx * dx + dy * dy;
                return sq > near * near && sq < far * far;
            }).ToList();
        }
    }
}
