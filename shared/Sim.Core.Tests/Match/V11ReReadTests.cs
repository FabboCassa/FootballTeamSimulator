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
    /// Free-ball spec R7, issue #186: each outfielder re-reads his target on his own schedule. The
    /// schedule is scripted first; then a real match, every man traced, shows the ticks are
    /// staggered and that between his re-reads a man's place does not move with the ball.
    /// </summary>
    [TestFixture]
    public class V11ReReadTests
    {
        private const int Players = 11;

        private static readonly HashSet<MovementJob> OffHisPlace = new HashSet<MovementJob>
        {
            MovementJob.SentOff, MovementJob.Restart, MovementJob.Ball, MovementJob.Keeper, MovementJob.Chase,
            MovementJob.SetPiece
        };

        private static MatchBalance _cfg = null!;
        private static List<MovementRecord> _records = null!;

        [OneTimeSetUp]
        public void PlayOneMatch()
        {
            _cfg = new BalanceConfig().Match;
            League league = new LeagueGenerator().Generate(new Pcg32(20261030));
            Lineup home = LineupSelector.BestEleven(league.Clubs[3]);
            Lineup away = LineupSelector.BestEleven(league.Clubs[8]);
            var log = new MovementLog();
            for (int side = 0; side < 2; side++)
            for (int slot = 0; slot < Players; slot++)
                log.Watch(side, slot);
            new MatchEngine(new BalanceConfig()) { MovementSink = log }.Simulate(home, away, new Pcg32(186));
            _records = log.Records.ToList();
        }

        [Test]
        public void ThePeriod_FollowsPositioning_FromTheSlowToTheFastClock()
        {
            Assert.That(V11ReRead.PeriodTicks(1, _cfg), Is.EqualTo(_cfg.TicksOfMs(_cfg.V11ReReadSlowMs)));
            Assert.That(V11ReRead.PeriodTicks(100, _cfg), Is.EqualTo(_cfg.TicksOfMs(_cfg.V11ReReadFastMs)));
            for (int p = 2; p <= 100; p++)
                Assert.That(V11ReRead.PeriodTicks(p, _cfg), Is.LessThanOrEqualTo(V11ReRead.PeriodTicks(p - 1, _cfg)));
            Assert.That(V11ReRead.PeriodTicks(1, _cfg), Is.GreaterThan(V11ReRead.PeriodTicks(100, _cfg)));
        }

        [Test]
        public void EveryMan_ReReadsOncePerPeriod()
        {
            for (int period = 1; period <= 6; period++)
            for (int k = 0; k < 2 * Players; k++)
                Assert.That(Enumerable.Range(100, period).Count(tick => V11ReRead.Due(k, tick, period)), Is.EqualTo(1),
                    $"man {k}, period {period}");
        }

        [Test]
        public void TheSchedule_IsStaggered_NeighbouringSlotsNeverShareATick()
        {
            for (int period = 2; period <= 6; period++)
            {
                for (int k = 0; k + 1 < 2 * Players; k++)
                for (int tick = 0; tick < period; tick++)
                    Assert.That(V11ReRead.Due(k, tick, period) && V11ReRead.Due(k + 1, tick, period), Is.False,
                        $"slots {k} and {k + 1} on tick {tick}, period {period}");

                int most = Enumerable.Range(0, period).Max(tick => Enumerable.Range(0, Players).Count(k => V11ReRead.Due(k, tick, period)));
                Assert.That(most, Is.LessThanOrEqualTo((Players + period - 1) / period), $"period {period}: spread evenly");
            }
        }

        [Test]
        public void InAMatch_TheReReadsAreStaggered_NeverAWholeSideOnOneTick()
        {
            int ticks = 0, wholeSide = 0;
            long reads = 0;
            Dictionary<(int, int, int), MovementRecord> byTick = _records.ToDictionary(r => (r.Tick, r.Side, r.Slot));
            foreach (IGrouping<(int, int), MovementRecord> group in _records.GroupBy(r => (r.Tick, r.Side)))
            {
                // A man back from a chase or a set piece reads afresh, off the schedule: left out.
                List<MovementRecord> onSchedule = group
                    .Where(r => OnHisPlace(r) && byTick.TryGetValue((r.Tick - 1, r.Side, r.Slot), out MovementRecord b) && OnHisPlace(b))
                    .ToList();
                if (onSchedule.Count < Players - 1) continue;
                ticks++;
                int n = onSchedule.Count(r => r.Reread);
                reads += n;
                if (n == onSchedule.Count) wholeSide++;
            }

            TestContext.Out.WriteLine($"[reread] {ticks} side-ticks, {reads / (double)ticks:F2} re-reads a tick, whole side {wholeSide}");
            Assert.That(ticks, Is.GreaterThan(10_000), "most of the match the ten outfielders are on their places or jobs");
            Assert.That(wholeSide, Is.EqualTo(0), "the outfielders never all re-read on one tick");
            Assert.That(reads / (double)ticks, Is.LessThan((Players - 1) / 2.0), "a man re-reads every two to five ticks, not every tick");
        }

        [Test]
        public void InAMatch_BetweenHisReReads_HisPlaceDoesNotFollowTheBall()
        {
            int held = 0, moved = 0, ballMoved = 0;
            Dictionary<(int, int, int), MovementRecord> byTick = _records.ToDictionary(r => (r.Tick, r.Side, r.Slot));
            foreach (MovementRecord r in _records)
            {
                if (r.Reread || r.Job != MovementJob.Spot || r.InPossession || r.ExpansionPermille != 0) continue;
                if (!byTick.TryGetValue((r.Tick - 1, r.Side, r.Slot), out MovementRecord before)) continue;
                if (before.Job != MovementJob.Spot || before.InPossession || before.ExpansionPermille != 0 || before.Phase != r.Phase)
                    continue;

                held++;
                if (r.SpotXDm != before.SpotXDm || r.SpotYDm != before.SpotYDm) moved++;
                if (r.BallXDm != before.BallXDm || r.BallYDm != before.BallYDm) ballMoved++;
            }

            TestContext.Out.WriteLine($"[reread] {held} ticks between re-reads, the ball moved in {ballMoved}, his place in {moved}");
            Assert.That(held, Is.GreaterThan(5_000));
            Assert.That(ballMoved, Is.GreaterThan(held / 2), "the ball was moving");
            Assert.That(moved, Is.EqualTo(0), "his place is read off the ball he last saw");
        }

        /// <summary>An outfielder whose move this tick went through his place (no chase, ball or set piece), ball in play.</summary>
        private static bool OnHisPlace(MovementRecord r) => !OffHisPlace.Contains(r.Job) && !r.BallDead && r.Slot != 0;
    }
}
