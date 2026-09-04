using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The packed form of the movement stream (engine phase 2).
    ///
    /// It exists because of a measurement: a ninety-minute report weighs 2 KB WITHOUT the stream
    /// and 2077 KB with it — half a million small integers written as decimal ASCII into a Postgres
    /// text column, which for a ten-club league season is 180 MB. The film itself was never the
    /// waste (the client draws it; watching your own match is the feature), the encoding was.
    ///
    /// What is pinned here is the CONTRACT, not the ratio: the round trip is lossless, the report
    /// still hashes the same (so the packed form cannot move a golden master), a replay stored in
    /// the OLD shape still reads, and a stream that arrives packed draws itself even if nobody
    /// remembered to unpack it. The size is printed for the record rather than asserted tightly,
    /// because it is calibration.
    /// </summary>
    [TestFixture]
    public class ReplayCodecTests
    {
        private static MatchReport Play()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            return new MatchEngine(new BalanceConfig(), applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: false, generatePositions: true)
                .Simulate(LineupSelector.BestEleven(league.Clubs[9]),
                          LineupSelector.BestEleven(league.Clubs[10]), new Pcg32(4242));
        }

        [Test]
        public void PackAndUnpack_AreLossless_AndTheReportStillHashesTheSame()
        {
            MatchReport report = Play();
            PositionStream stream = report.Positions!;

            int[] ball = stream.BallXY.ToArray();
            int[] home = stream.HomeXY.ToArray();
            int[] away = stream.AwayXY.ToArray();
            int[] owner = stream.Owner.ToArray();
            int actions = stream.Actions.Count;
            ulong hash = MatchReportHasher.Hash(report);

            string plain = JsonSerializer.Serialize(report);
            report.Positions!.Pack();
            string packed = JsonSerializer.Serialize(report);

            MatchReport back = JsonSerializer.Deserialize<MatchReport>(packed)!;
            back.Positions!.Unpack();

            Assert.That(back.Positions.BallXY, Is.EqualTo(ball), "the ball track");
            Assert.That(back.Positions.HomeXY, Is.EqualTo(home), "the home track");
            Assert.That(back.Positions.AwayXY, Is.EqualTo(away), "the away track");
            Assert.That(back.Positions.Owner, Is.EqualTo(owner), "the owner track");
            Assert.That(back.Positions.Actions.Count, Is.EqualTo(actions), "the action list is left alone");
            Assert.That(MatchReportHasher.Hash(back), Is.EqualTo(hash),
                "the packed form must be invisible to the hash, or it would move a golden master");

            TestContext.Out.WriteLine(
                $"[replay-size] stored report {plain.Length / 1024} KB -> {packed.Length / 1024} KB " +
                $"({(double)plain.Length / packed.Length:F2}x)");
            Assert.That(packed.Length, Is.LessThan(plain.Length / 2),
                "packing that does not at least halve the stored replay is not worth its contract");
        }

        [Test]
        public void AReplayStoredBeforeThePackedForm_StillReads()
        {
            MatchReport report = Play();
            ulong hash = MatchReportHasher.Hash(report);
            int[] ball = report.Positions!.BallXY.ToArray();

            // Exactly what is sitting in the database today: the arrays, written out longhand.
            string old = JsonSerializer.Serialize(report);

            MatchReport back = JsonSerializer.Deserialize<MatchReport>(old)!;
            back.Positions!.Unpack();   // must be a no-op

            Assert.That(back.Positions.BallXY, Is.EqualTo(ball));
            Assert.That(MatchReportHasher.Hash(back), Is.EqualTo(hash));
        }

        [Test]
        public void AStreamThatArrivesPacked_DrawsItselfAnyway()
        {
            // The renderer's entry points unpack on demand, so a caller who forgets leaves a
            // slow first frame behind — not a blank pitch.
            MatchReport report = Play();
            int frames = report.Positions!.TickCount;
            PitchPoint man = report.Positions.HomeAt(500, 3);
            PitchPoint ball = report.Positions.BallAt(500);

            report.Positions.Pack();
            MatchReport back = JsonSerializer.Deserialize<MatchReport>(JsonSerializer.Serialize(report))!;

            Assert.That(back.Positions!.TickCount, Is.EqualTo(frames), "TickCount unpacks on demand");
            Assert.That(back.Positions.HomeAt(500, 3).X, Is.EqualTo(man.X), "HomeAt unpacks on demand");
            Assert.That(back.Positions.BallAt(500).Y, Is.EqualTo(ball.Y), "BallAt unpacks on demand");
        }

        [Test]
        public void PackingIsIdempotent_AndAnEmptyStreamSurvivesBoth()
        {
            var empty = new PositionStream { PlayerCount = 11, TicksPerMinute = 120 };
            Assert.DoesNotThrow(() => empty.Pack().Unpack().Pack().Unpack());
            Assert.That(empty.BallXY, Is.Empty);
            Assert.That(empty.TickCount, Is.Zero);

            MatchReport report = Play();
            int[] ball = report.Positions!.BallXY.ToArray();
            report.Positions.Pack();
            report.Positions.Pack();       // already packed: nothing to do
            report.Positions.Unpack();
            report.Positions.Unpack();     // already unpacked: nothing to do
            Assert.That(report.Positions.BallXY, Is.EqualTo(ball));
        }
    }
}
