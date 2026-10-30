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
    /// Free-ball-real-movement spec R2: the ball-event sink behind the cause trace of free-ball
    /// turns. Attached, it hears the ball's touches, deflections and landings; attached or not, the
    /// match is the same match.
    /// </summary>
    [TestFixture]
    public class BallEventSinkTests
    {
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20261024));
        }

        [Test]
        public void AttachingTheSink_DoesNotMoveTheMatch_AndItHearsTheBall()
        {
            MatchReport plain = Play(null);
            var log = new BallEventLog();
            MatchReport heard = Play(log);

            Assert.That(MatchReportHasher.Hash(heard), Is.EqualTo(MatchReportHasher.Hash(plain)),
                "the sink only reads: the match hash cannot move");
            Assert.That(heard.Positions!.BallZ, Is.EqualTo(plain.Positions!.BallZ), "nor the ball's height, which is outside the hash");

            int lastTick = 90 * new BalanceConfig().Match.TicksPerMinute;
            Assert.That(log.Records.Count(e => e.Kind == BallEventKind.Touch), Is.GreaterThan(1000));
            Assert.That(log.Records.Any(e => e.Kind == BallEventKind.Deflection), Is.True);
            Assert.That(log.Records.Any(e => e.Kind == BallEventKind.Bounce), Is.True, "lofted balls come down");
            Assert.That(log.Records.Any(e => e.Kind == BallEventKind.None), Is.False, "none is a turn's cause, never an event");

            int previous = 0;
            foreach (BallEvent e in log.Records)
            {
                Assert.That(e.Tick, Is.InRange(previous, lastTick), "events come in tick order, inside the match");
                previous = e.Tick;
                if (e.Kind == BallEventKind.Bounce) continue;
                Assert.That(e.Side, Is.InRange(0, 1));
                Assert.That(e.Slot, Is.InRange(0, Lineup.Size - 1));
            }
        }

        private static MatchReport Play(IBallEventSink? sink)
        {
            var engine = new MatchEngine(new BalanceConfig()) { BallEventSink = sink };
            return engine.Simulate(
                LineupSelector.BestEleven(_league.Clubs[2]), LineupSelector.BestEleven(_league.Clubs[7]), new Pcg32(4141));
        }
    }
}
