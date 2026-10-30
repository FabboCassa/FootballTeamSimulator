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
    /// Free-ball spec R7, issue #177: the dev movement trace behind docs/engine/oscillation-trace.md.
    /// Attached, it records every tick of the men it watches - the target, the job, the velocity and
    /// the inputs of the target; attached or not, the match is the same match.
    /// </summary>
    [TestFixture]
    public class MovementSinkTests
    {
        private static League _league = null!;
        private static Lineup _home = null!;
        private static Lineup _away = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20261024));
            _home = LineupSelector.BestEleven(_league.Clubs[2]);
            _away = LineupSelector.BestEleven(_league.Clubs[7]);
        }

        private static int LastTick => 90 * new BalanceConfig().Match.TicksPerMinute;

        private static int StrikerSlot(Lineup xi)
        {
            for (int slot = 0; slot < xi.Slots.Count; slot++)
                if (xi.Slots[slot].Role == PositionRole.Striker) return slot;
            return xi.Slots.Count - 1;
        }

        [Test]
        public void AttachingTheTrace_DoesNotMoveTheMatch()
        {
            ulong without = MatchReportHasher.Hash(Play(null));

            var some = new MovementLog().Watch(0, StrikerSlot(_home)).Watch(1, StrikerSlot(_away)).Watch(0, 4);
            ulong withSome = MatchReportHasher.Hash(Play(some));

            var everybody = new MovementLog();
            for (int side = 0; side < 2; side++)
            for (int slot = 0; slot < _home.Slots.Count; slot++)
                everybody.Watch(side, slot);
            ulong withEverybody = MatchReportHasher.Hash(Play(everybody));

            Assert.That(withSome, Is.EqualTo(without), "the trace only reads: the match hash cannot move");
            Assert.That(withEverybody, Is.EqualTo(without), "not even with every man traced");
            Assert.That(everybody.Records.Count, Is.EqualTo(LastTick * 2 * _home.Slots.Count));
        }

        [Test]
        public void Trace_HasOneRecordATick_ForEachWatchedMan_AndNobodyElse()
        {
            int striker = StrikerSlot(_away);
            var log = new MovementLog().Watch(1, striker).Watch(0, 3);
            Play(log);

            Assert.That(log.Records.Count, Is.EqualTo(2 * LastTick));
            var men = log.Records.Select(r => (r.Side, r.Slot)).Distinct().OrderBy(m => m.Side).ToList();
            Assert.That(men, Is.EqualTo(new List<(int, int)> { (0, 3), (1, striker) }));

            List<MovementRecord> his = log.Records.Where(r => r.Side == 1).ToList();
            for (int i = 0; i < his.Count; i++)
                Assert.That(his[i].Tick, Is.EqualTo(i + 1), "every tick of the match, in order");
        }

        [Test]
        public void Trace_PositionsAreTheStreamPositions()
        {
            int striker = StrikerSlot(_home);
            var log = new MovementLog().Watch(0, striker);
            MatchReport report = Play(log);
            PositionStream stream = report.Positions!;
            int stride = new BalanceConfig().Match.StreamTicksPerFrame;

            int frames = 0, same = 0;
            foreach (MovementRecord r in log.Records)
            {
                if (r.Tick % stride != 0) continue;
                frames++;
                int frame = r.Tick / stride;
                if (r.XDm == stream.PlayerX(stream.HomeXY, frame, striker) && r.YDm == stream.PlayerY(stream.HomeXY, frame, striker))
                    same++;
            }

            Assert.That(frames, Is.EqualTo(LastTick / stride));
            Assert.That(same, Is.EqualTo(frames), "the man the trace follows is the man the replay shows");
        }

        [Test]
        public void Trace_ExplainsTheTarget_AManOnHisSpotIsSentToIt_AndHisLegsFollowIt()
        {
            var log = new MovementLog().Watch(0, StrikerSlot(_home)).Watch(1, StrikerSlot(_away));
            Play(log);

            var jobs = new HashSet<MovementJob>(log.Records.Select(r => r.Job));
            Assert.That(jobs, Is.SupersetOf(new[] { MovementJob.Spot, MovementJob.Support, MovementJob.Chase, MovementJob.Ball }),
                "a striker's match has his place, the supporting run, the chase and the ball");

            int spotTicks = 0, held = 0;
            foreach (MovementRecord r in log.Records)
            {
                if (r.Job != MovementJob.Spot || !r.Steered) continue;
                spotTicks++;
                Assert.That((r.TargetXDm, r.TargetYDm), Is.EqualTo((r.SpotXDm, r.SpotYDm)), $"tick {r.Tick}: a man with no job goes to his spot");
                if (!r.InPossession) Assert.That(r.SpotXDm, Is.EqualTo(r.RawSpotXDm), "out of possession nothing holds his spot onside");
                if (r.SpotXDm != r.RawSpotXDm) held++;

                // His legs turn toward where they were sent: the velocity wanted points at the target.
                long dx = r.TargetXDm - r.XDm, dy = r.TargetYDm - r.YDm;
                if (dx * dx + dy * dy > 50 * 50)
                    Assert.That(dx * r.WantXCmPerS + dy * r.WantYCmPerS, Is.GreaterThan(0), $"tick {r.Tick}: the wanted velocity points at the target");
            }

            Assert.That(spotTicks, Is.GreaterThan(1000));
            Assert.That(held, Is.GreaterThan(0), "in possession the onside hold moves a striker's spot, and the trace shows by how much");
        }

        private static MatchReport Play(IMovementSink? sink)
        {
            var engine = new MatchEngine(new BalanceConfig()) { MovementSink = sink };
            return engine.Simulate(_home, _away, new Pcg32(4141));
        }
    }
}
