using System.Linq;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The R1 movement-feel readings of the believable-match spec (direction agreement, heading
    /// reversals, speed bands, accelerations), pinned against hand-built streams. Every stream runs
    /// at the engine's 300 frames a minute (5 a second), so a 1 s step is 5 frames and a step of
    /// d dm a frame is d ÷ 2 m/s. Expected values are worked out on paper from the definitions in
    /// <see cref="MovementFeelAnalyzer"/>.
    /// </summary>
    [TestFixture]
    public class MovementFeelAnalyzerTests
    {
        private const int EngineTpm = 300;
        private const int CY = Pitch.CenterY;

        private static PositionStream Stream(int ticks)
        {
            PositionStream s = NewStream(ticks);
            s.TicksPerMinute = EngineTpm;
            return s;
        }

        private static MovementFeelMetrics Measure(PositionStream s) =>
            MovementFeelAnalyzer.Analyze(new MatchReport { Positions = s })!;

        /// <summary>Home outfielder <paramref name="slot"/> runs along x at <paramref name="dmPerFrame"/>, from frame <paramref name="from"/> on.</summary>
        private static void Run(PositionStream s, int slot, int dmPerFrame, int from = 0)
        {
            int x = HomeStackX;
            for (int t = from; t <= s.LastTick; t++)
            {
                Put(s, true, t, slot, x, CY + (slot - 5) * 30);
                x += dmPerFrame;
            }
        }

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(MovementFeelAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        // ------------------------------------------------------------------ direction agreement

        [Test]
        public void Agreement_ARigidBlockTranslation_ReadsOne()
        {
            PositionStream s = Stream(51);            // 10 whole 1 s steps
            for (int i = 1; i < Players; i++) Run(s, i, 3);   // 1.5 m/s, 1.5 m a step

            MovementFeelMetrics m = Measure(s);

            Assert.That(m.AgreementWindows, Is.EqualTo(10), "away stands still: only home's steps count");
            Assert.That(m.AgreementMedian, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(m.AgreementAbove09Percent, Is.EqualTo(100.0));
        }

        [Test]
        public void Agreement_OppositeRuns_ReadZero()
        {
            PositionStream s = Stream(51);
            for (int i = 1; i < Players; i++) Run(s, i, i <= 5 ? 3 : -3);

            MovementFeelMetrics m = Measure(s);

            Assert.That(m.AgreementWindows, Is.EqualTo(10));
            Assert.That(m.AgreementMedian, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(m.AgreementAbove09Percent, Is.EqualTo(0.0));
        }

        [Test]
        public void Agreement_FewerThanSixMovers_ReadsNoWindow()
        {
            PositionStream s = Stream(51);
            for (int i = 1; i <= 5; i++) Run(s, i, 3);
            Run(s, 6, 1);                             // 0.5 m a step: not a mover

            Assert.That(Measure(s).AgreementWindows, Is.EqualTo(0));
        }

        [Test]
        public void Agreement_DeadBall_ReadsNoWindowAndNoFrame()
        {
            PositionStream s = Stream(51);
            for (int i = 1; i < Players; i++) Run(s, i, 3);
            Act(s, 0, BallActionKind.ThrowIn, true);  // nobody ever takes it: dead all match

            MovementFeelMetrics m = Measure(s);

            Assert.That(m.AgreementWindows, Is.EqualTo(0));
            Assert.That(m.PlayerFrames, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ heading reversals

        [Test]
        public void Reversals_A180DegreeTurn_CountsOne()
        {
            PositionStream s = Stream(21);            // four 1 s steps: +1.5, +1.5, -1.5, -1.5 m
            for (int t = 0; t <= 20; t++)
                Put(s, true, t, 3, HomeStackX + 3 * (t <= 10 ? t : 20 - t), CY);

            Assert.That(Measure(s).Reversals, Is.EqualTo(1));
        }

        [Test]
        public void Reversals_ARightAngleTurn_CountsNone()
        {
            PositionStream s = Stream(21);
            for (int t = 0; t <= 20; t++)
                Put(s, true, t, 3, HomeStackX + 3 * System.Math.Min(t, 10), CY + 3 * System.Math.Max(0, t - 10));

            Assert.That(Measure(s).Reversals, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ speed bands

        [Test]
        public void SpeedBands_ConstantRuns_LandInTheirBands()
        {
            PositionStream s = Stream(12);            // speed read on frames 1-10: 10 a man
            Run(s, 1, 3);                             // 1.5 m/s
            Run(s, 2, 4);                             // 2.0 m/s: the lower bound is inclusive
            Run(s, 3, 10);                            // 5.0 m/s
            Run(s, 4, -15);                           // 7.5 m/s

            MovementFeelMetrics m = Measure(s);

            Assert.That(m.PlayerFrames, Is.EqualTo(200));
            Assert.That(m.PlayerMinutes, Is.EqualTo(200.0 / EngineTpm).Within(1e-12));
            Assert.That(m.SpeedPercent(0), Is.EqualTo(80.0), "< 0.2: the 16 men standing");
            Assert.That(m.SpeedPercent(1), Is.EqualTo(5.0), "0.2-2");
            Assert.That(m.SpeedPercent(2), Is.EqualTo(5.0), "2-4");
            Assert.That(m.SpeedPercent(3), Is.EqualTo(5.0), "4-5.5");
            Assert.That(m.SpeedPercent(4), Is.EqualTo(0.0), "5.5-7");
            Assert.That(m.SpeedPercent(5), Is.EqualTo(5.0), "> 7");
        }

        // ------------------------------------------------------------------ accelerations

        /// <summary>
        /// One man's step grows by 1 dm a frame (2.5 m/s²) for <paramref name="rampFrames"/> frames,
        /// then holds. The 2-frame differences read > 2 m/s² on rampFrames - 3 frames; an effort
        /// needs 3 (0.6 s, the 0.5 s of the reference at 5 frames a second).
        /// </summary>
        [TestCase(5, 0)]
        [TestCase(6, 1)]
        [TestCase(12, 1)]
        public void Accelerations_ARamp_CountsOneEffortWhenLongEnough(int rampFrames, int efforts)
        {
            PositionStream s = Stream(30);
            int x = 200;
            for (int t = 0; t < 30; t++)
            {
                x += System.Math.Min(t, rampFrames);
                Put(s, true, t, 3, x, CY);
            }

            Assert.That(Measure(s).Accelerations, Is.EqualTo(efforts));
        }

        [Test]
        public void Accelerations_SlowingDown_CountsNone()
        {
            PositionStream s = Stream(30);
            int x = 200;
            for (int t = 0; t < 30; t++)
            {
                x += System.Math.Max(0, 15 - t);
                Put(s, true, t, 3, x, CY);
            }

            Assert.That(Measure(s).Accelerations, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ the report

        [Test]
        public void Tally_PrintsTheMovementBandsInOrder_ReportOnly()
        {
            PositionStream s = Stream(51);
            for (int i = 1; i < Players; i++) Run(s, i, 3);
            var tally = new MovementFeelTally();
            tally.Add(Measure(s));
            tally.Add(Measure(s));

            var rows = tally.Rows();

            Assert.That(tally.Matches, Is.EqualTo(2));
            Assert.That(rows.Select(r => r.Band.Name), Is.EqualTo(new[]
            {
                RealismReference.Movement.AgreementMedian.Name, RealismReference.Movement.AgreementAbove09Percent.Name,
                RealismReference.Movement.ReversalsPerPlayerMinute.Name, RealismReference.Movement.SpeedBelow02Percent.Name,
                RealismReference.Movement.Speed02To2Percent.Name, RealismReference.Movement.Speed2To4Percent.Name,
                RealismReference.Movement.Speed4To55Percent.Name, RealismReference.Movement.Speed55To7Percent.Name,
                RealismReference.Movement.SpeedAbove7Percent.Name, RealismReference.Movement.AccelerationsPerPlayerMinute.Name,
            }));
            Assert.That(rows[0].Value, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(rows[0].InBand, Is.False);
            Assert.That(rows[4].Value, Is.EqualTo(50.0), "10 of 20 outfielders run at 1.5 m/s");

            string text = tally.Format("scripted");
            Assert.That(text, Does.Contain("report only"));
            Assert.That(text, Does.Contain("OUT"));
            Assert.That(text, Does.Contain("scripted (2 matches)"));
        }
    }
}
