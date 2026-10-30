using System.Linq;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The oscillation reading of the free-ball spec (R2): per player, reversals of more than 120°
    /// between consecutive 1 s steps (the heading-reversal definition of doc §9.2), per
    /// player-minute. Streams run at the engine's 300 frames a minute, so a 1 s step is 5 frames.
    /// </summary>
    [TestFixture]
    public class OscillationAnalyzerTests
    {
        private const int EngineTpm = 300;
        private const int CY = Pitch.CenterY;

        private static PositionStream Stream(int ticks)
        {
            PositionStream s = NewStream(ticks);
            s.TicksPerMinute = EngineTpm;
            return s;
        }

        private static OscillationMetrics Measure(PositionStream s) =>
            OscillationAnalyzer.Analyze(new MatchReport { Positions = s })!;

        /// <summary>Man <paramref name="slot"/> shuttles along x, <paramref name="dmPerFrame"/> a frame, turning every 1 s step.</summary>
        private static void Shuttle(PositionStream s, bool home, int slot, int dmPerFrame)
        {
            int x0 = home ? HomeStackX : AwayStackX;
            for (int t = 0; t <= s.LastTick; t++)
            {
                int phase = t % 10;
                int offset = dmPerFrame * (phase <= 5 ? phase : 10 - phase);
                Put(s, home, t, slot, x0 + offset, CY + slot * 10);
            }
        }

        private static PlayerOscillation Man(OscillationMetrics m, bool home, int slot) =>
            m.Players.Single(p => p.Home == home && p.Slot == slot);

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(OscillationAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        [Test]
        public void AShuttlingMan_CountsAReversalEveryStep_OthersNone()
        {
            PositionStream s = Stream(21);                    // four 1 s steps: +1.5, -1.5, +1.5, -1.5 m
            Shuttle(s, true, 3, 3);

            OscillationMetrics m = Measure(s);

            Assert.That(Man(m, true, 3).Reversals, Is.EqualTo(3));
            Assert.That(Man(m, true, 3).Minutes, Is.EqualTo(19.0 / EngineTpm).Within(1e-12), "frames 1-19");
            Assert.That(m.Players.Where(p => !(p.Home && p.Slot == 3)).Sum(p => p.Reversals), Is.EqualTo(0));
            Assert.That(m.Players.Count, Is.EqualTo(20), "ten outfielders a side, keepers left out");
            Assert.That(m.Reversals, Is.EqualTo(3));
        }

        [Test]
        public void ARightAngleTurn_CountsNone()
        {
            PositionStream s = Stream(21);
            for (int t = 0; t <= 20; t++)
                Put(s, true, t, 3, HomeStackX + 3 * System.Math.Min(t, 10), CY + 3 * System.Math.Max(0, t - 10));

            Assert.That(Measure(s).Reversals, Is.EqualTo(0));
        }

        [TestCase(1, 0)]                                       // 0.5 m a step: not over 0.5 m
        [TestCase(2, 3)]                                       // 1.0 m a step
        public void StepsOfHalfAMetreOrLess_DoNotCount(int dmPerFrame, int reversals)
        {
            PositionStream s = Stream(21);
            Shuttle(s, true, 3, dmPerFrame);

            Assert.That(Measure(s).Reversals, Is.EqualTo(reversals));
        }

        [Test]
        public void TheKeeper_IsNotCounted()
        {
            PositionStream s = Stream(21);
            for (int t = 0; t <= 20; t++)
                Put(s, true, t, Keeper, 40 + 3 * (t % 10 <= 5 ? t % 10 : 10 - t % 10), CY);

            OscillationMetrics m = Measure(s);

            Assert.That(m.Reversals, Is.EqualTo(0));
            Assert.That(m.Players.Any(p => p.Home && p.Slot == Keeper), Is.False);
        }

        [Test]
        public void ADeadBall_BreaksTheStepPair()
        {
            PositionStream s = Stream(21);
            Shuttle(s, true, 3, 3);
            Act(s, 7, BallActionKind.ThrowIn, true);          // frame 7 to 11 dead: steps 2 and 3 void
            Hold(s, 12, 20, true, 1, HomeStackX, CY);         // taken at frame 12

            Assert.That(Measure(s).Reversals, Is.EqualTo(0), "no two consecutive steps are open play");
        }

        [Test]
        public void PooledReversals_MatchTheMovementFeelReading()
        {
            PositionStream s = Stream(61);
            Shuttle(s, true, 3, 3);
            Shuttle(s, false, 6, 4);
            for (int t = 0; t <= 60; t++) Put(s, true, t, 7, HomeStackX + 2 * t, CY - 50);
            Act(s, 23, BallActionKind.ThrowIn, true);
            Hold(s, 30, 60, false, 2, AwayStackX, CY);

            OscillationMetrics m = Measure(s);
            MovementFeelMetrics feel = MovementFeelAnalyzer.Analyze(new MatchReport { Positions = s })!;

            Assert.That(m.Reversals, Is.GreaterThan(0));
            Assert.That(m.Reversals, Is.EqualTo(feel.Reversals));
            Assert.That(m.PlayerMinutes, Is.EqualTo(feel.PlayerMinutes).Within(1e-9));
            Assert.That(m.ReversalsPerPlayerMinute, Is.EqualTo(feel.ReversalsPerPlayerMinute).Within(1e-9));
        }

        [Test]
        public void TheSpread_ReadsPerPlayerRates_AndLeavesShortStintsOut()
        {
            var m = new OscillationMetrics();
            m.Add(new PlayerOscillation(true, 1, 10, 20));    // 0.5 a minute
            m.Add(new PlayerOscillation(true, 2, 20, 20));    // 1.0
            m.Add(new PlayerOscillation(true, 3, 60, 20));    // 3.0
            m.Add(new PlayerOscillation(true, 4, 40, 20));    // 2.0
            m.Add(new PlayerOscillation(true, 5, 9, 3));      // 3.0, but 3 minutes: out of the spread

            Assert.That(m.SpreadPlayers, Is.EqualTo(4));
            Assert.That(m.PlayerQuantile(0.5), Is.EqualTo(1.0));
            Assert.That(m.PlayerQuantile(0.9), Is.EqualTo(3.0));
            Assert.That(m.PlayerQuantile(1.0), Is.EqualTo(3.0));
            Assert.That(m.PlayersAbovePercent(1.529), Is.EqualTo(50.0));
            Assert.That(m.ReversalsPerPlayerMinute, Is.EqualTo(139.0 / 83).Within(1e-12), "the pool keeps every man");
        }

        [Test]
        public void Tally_PoolsEveryMan_AgainstTheReversalBand()
        {
            PositionStream s = Stream(21);
            Shuttle(s, true, 3, 3);
            var tally = new StrikerOscillationTally();
            tally.Add(Measure(s));
            tally.Add(Measure(s));

            RealismRow row = tally.Rows()[2];

            Assert.That(tally.Oscillation.Matches, Is.EqualTo(2));
            Assert.That(tally.Oscillation.Players.Count, Is.EqualTo(40));
            Assert.That(row.Band.Name, Is.EqualTo(RealismReference.Tracking.ReversalsPerPlayerMinute.Name));
            Assert.That(row.Value, Is.EqualTo(6 / (40 * 19.0 / EngineTpm)).Within(1e-9));
            Assert.That(tally.Format("scripted"), Does.Contain("oscillation per player"));
        }
    }
}
