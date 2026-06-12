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
    [TestFixture]
    public class PositionStreamTests
    {
        private static League _league = null!;
        private static Club _midA = null!;
        private static Club _midB = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _midA = _league.Clubs[9];
            _midB = _league.Clubs[10];
        }

        private static MatchReport Play(ulong seed) => new MatchEngine().Simulate(
            LineupSelector.BestEleven(_midA), LineupSelector.BestEleven(_midB), new Pcg32(seed));

        private static int SlotIndexOf(Lineup lineup, int playerId) =>
            lineup.Slots.FindIndex(s => s.Player.Id == playerId);

        // ------------------------------------------------------------ determinism

        [Test]
        public void Stream_IsDeterministic_PerSeed()
        {
            string a = JsonSerializer.Serialize(Play(42).Positions);
            string b = JsonSerializer.Serialize(Play(42).Positions);

            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentStreams()
        {
            string a = JsonSerializer.Serialize(Play(42).Positions);
            string b = JsonSerializer.Serialize(Play(43).Positions);

            Assert.That(b, Is.Not.EqualTo(a));
        }

        // ------------------------------------------------------------ event consistency

        [Test]
        public void Ball_CoincidesWithShooter_AtEveryEventTick()
        {
            Lineup home = LineupSelector.BestEleven(_midA);
            Lineup away = LineupSelector.BestEleven(_midB);
            int goalsChecked = 0;

            for (ulong seed = 100; seed < 120; seed++)
            {
                MatchReport r = Play(seed);
                PositionStream stream = r.Positions!;

                foreach (MatchEvent e in r.Events)
                {
                    int tick = stream.TickOfMinute(e.Minute);
                    PositionFrame frame = stream.Frames[tick];
                    Assert.That(frame.Tick, Is.EqualTo(tick), "Frame index must equal tick");

                    bool homeShoots = e.ClubId == r.HomeClubId;
                    int slot = SlotIndexOf(homeShoots ? home : away, e.PlayerId);
                    Assert.That(slot, Is.GreaterThanOrEqualTo(0), "Shooter must be in the lineup");

                    PitchPoint shooter = homeShoots ? frame.Home[slot] : frame.Away[slot];
                    Assert.That(shooter.X, Is.EqualTo(frame.Ball.X), $"seed {seed} minute {e.Minute}: shooter X != ball X");
                    Assert.That(shooter.Y, Is.EqualTo(frame.Ball.Y), $"seed {seed} minute {e.Minute}: shooter Y != ball Y");

                    if (e.Type == MatchEventType.Goal) goalsChecked++;
                }
            }

            Assert.That(goalsChecked, Is.GreaterThan(20), "Sanity: enough goals sampled");
        }

        [Test]
        public void Shots_HappenNearTheAttackedGoal()
        {
            var cfg = new BalanceConfig().Match;

            for (ulong seed = 200; seed < 210; seed++)
            {
                MatchReport r = Play(seed);

                foreach (MatchEvent e in r.Events)
                {
                    PitchPoint ball = r.Positions!.Frames[r.Positions.TickOfMinute(e.Minute)].Ball;
                    int expectedX = e.ClubId == r.HomeClubId
                        ? Pitch.LengthDm - cfg.ShotSpotGoalDistanceDm
                        : cfg.ShotSpotGoalDistanceDm;

                    Assert.That(ball.X, Is.EqualTo(expectedX), "Home attacks toward X=LengthDm, away toward X=0");
                }
            }
        }

        // ------------------------------------------------------------ structure & bounds

        [Test]
        public void Frames_CoverFullMatch_InOrder()
        {
            PositionStream stream = Play(7).Positions!;
            int expected = 90 * stream.TicksPerMinute + 1; // tick 0 (kickoff) .. tick 90*tpm

            Assert.That(stream.TicksPerMinute, Is.GreaterThan(0));
            Assert.That(stream.Frames.Count, Is.EqualTo(expected));

            for (int i = 0; i < stream.Frames.Count; i++)
            {
                Assert.That(stream.Frames[i].Tick, Is.EqualTo(i));
                Assert.That(stream.Frames[i].Home.Length, Is.EqualTo(Lineup.Size));
                Assert.That(stream.Frames[i].Away.Length, Is.EqualTo(Lineup.Size));
            }
        }

        [Test]
        public void AllPositions_StayOnPitch()
        {
            for (ulong seed = 300; seed < 305; seed++)
            {
                foreach (PositionFrame f in Play(seed).Positions!.Frames)
                {
                    AssertOnPitch(f.Ball);
                    foreach (PitchPoint p in f.Home) AssertOnPitch(p);
                    foreach (PitchPoint p in f.Away) AssertOnPitch(p);
                }
            }
        }

        private static void AssertOnPitch(PitchPoint p)
        {
            Assert.That(p.X, Is.InRange(0, Pitch.LengthDm));
            Assert.That(p.Y, Is.InRange(0, Pitch.WidthDm));
        }

        [Test]
        public void KickoffFrame_HasBallAtCentre()
        {
            PositionFrame first = Play(11).Positions!.Frames[0];

            Assert.That(first.Ball.X, Is.EqualTo(Pitch.CenterX));
            Assert.That(first.Ball.Y, Is.EqualTo(Pitch.CenterY));
        }

        [Test]
        public void Teams_FaceEachOther_AtKickoff()
        {
            Lineup home = LineupSelector.BestEleven(_midA);
            Lineup away = LineupSelector.BestEleven(_midB);
            PositionFrame first = Play(13).Positions!.Frames[0];

            // Goalkeepers guard opposite goal lines; formations mirror each other.
            int homeGk = home.Slots.FindIndex(s => s.Role == PositionRole.Goalkeeper);
            int awayGk = away.Slots.FindIndex(s => s.Role == PositionRole.Goalkeeper);
            Assert.That(first.Home[homeGk].X, Is.LessThan(Pitch.LengthDm / 4), "Home GK near X=0");
            Assert.That(first.Away[awayGk].X, Is.GreaterThan(Pitch.LengthDm * 3 / 4), "Away GK near X=LengthDm");

            Assert.That(first.Home.Average(p => p.X), Is.LessThan(first.Away.Average(p => p.X)),
                "Home side sits left of the away side overall");
        }
    }
}
