using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match.Movement;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The odds of a block on their own (issue #80): from the defender's distance to the ball's
    /// line and from where he stands relative to the strike point, every part of it read off the
    /// config. Distances are engine units; the reach is arbitrary because only the ratio counts.
    /// </summary>
    [TestFixture]
    public class ShotBlockOddsTests
    {
        private const int Reach = 1_000;

        /// <summary>The engine's units per decimetre (MatchUnits.Scale, internal to Sim.Core).</summary>
        private const int UnitsPerDm = 16;

        /// <summary>Far beyond any close range a config sets: the man is well down the lane.</summary>
        private const int FarFromStrike = int.MaxValue;

        private static MatchBalance Cfg(int onLine = 600, int closePercent = 150, int closeRangeDm = 40, int fullOddsDm = 0) =>
            new MatchBalance
            {
                BlockPermilleOnLine = onLine,
                BlockClosePercent = closePercent,
                BlockCloseRangeDm = closeRangeDm,
                BlockFullOddsDm = fullOddsDm,
            };

        [Test]
        public void OnTheLine_BeatsHalfReach_WhichBeatsTheEdgeOfReach()
        {
            MatchBalance cfg = Cfg();
            int onLine = ShotBlock.Permille(0, Reach, FarFromStrike, cfg);
            int halfReach = ShotBlock.Permille(Reach / 2, Reach, FarFromStrike, cfg);

            Assert.That(onLine, Is.EqualTo(600), "a man standing on the line has the configured odds");
            Assert.That(halfReach, Is.EqualTo(300), "half way to the edge of his reach, half of them");
            Assert.That(ShotBlock.Permille(Reach, Reach, FarFromStrike, cfg), Is.Zero, "at the edge of his reach, none");
            Assert.That(ShotBlock.Permille(Reach + 1, Reach, FarFromStrike, cfg), Is.Zero, "beyond it, none");
        }

        /// <summary>
        /// A stride off the line the ball is still struck at him: the full odds, and only beyond
        /// that do they fall, reaching none at the edge of his reach.
        /// </summary>
        [Test]
        public void WithinAStrideOfTheLine_TheOddsAreFull_ThenFallToTheReach()
        {
            const int strideDm = 20;
            int stride = strideDm * UnitsPerDm;
            MatchBalance cfg = Cfg(fullOddsDm: strideDm);
            int reach = 3 * stride;

            Assert.That(ShotBlock.Permille(0, reach, FarFromStrike, cfg), Is.EqualTo(600));
            Assert.That(ShotBlock.Permille(stride, reach, FarFromStrike, cfg), Is.EqualTo(600), "a stride off it, still full");
            Assert.That(ShotBlock.Permille(2 * stride, reach, FarFromStrike, cfg), Is.EqualTo(300),
                "half way from the stride to the edge of his reach, half of them");
            Assert.That(ShotBlock.Permille(reach, reach, FarFromStrike, cfg), Is.Zero, "at the edge of his reach, none");
            Assert.That(ShotBlock.Permille(reach - 1, reach, FarFromStrike, Cfg(fullOddsDm: 10 * strideDm)), Is.EqualTo(600),
                "a full-odds zone wider than his reach covers all of it");
        }

        [Test]
        public void CloseToTheStrike_BeatsFurtherDownTheLane_AtTheSameDistanceFromTheLine()
        {
            MatchBalance cfg = Cfg();
            foreach (int lane in new[] { 0, Reach / 4, Reach / 2 })
            {
                int close = ShotBlock.Permille(lane, Reach, 0, cfg);
                int far = ShotBlock.Permille(lane, Reach, FarFromStrike, cfg);

                Assert.That(close, Is.GreaterThan(far), $"lane {lane}: the man closing the striker down charges it down more often");
                Assert.That(close, Is.EqualTo(far * 150 / 100), $"lane {lane}: by the configured share");
            }
        }

        [Test]
        public void TheCloseRange_IsReadFromTheConfig()
        {
            const int fromStrike = 1_000;
            int inside = ShotBlock.Permille(0, Reach, fromStrike, Cfg(closeRangeDm: 10_000));
            int outside = ShotBlock.Permille(0, Reach, fromStrike, Cfg(closeRangeDm: 0));

            Assert.That(inside, Is.EqualTo(900), "inside a wide close range the bonus applies");
            Assert.That(outside, Is.EqualTo(600), "outside a narrow one it does not");
        }

        // ------------------------------------------------------------------ who is offered a go

        /// <summary>A strike from the origin along +x, the ball 300 units on and moving 300 a tick.</summary>
        private const int BallX = 300, Step = 300;

        [Test]
        public void AManBehindTheStrikePoint_IsNeverOffered_EvenOnTheLine()
        {
            foreach (int ballX in new[] { 0, BallX, 10 * BallX })
            {
                Assert.That(ShotBlock.IsOffered(-50, 0, 0, 0, ballX, 0, Step, 0), Is.False,
                    $"ball at {ballX}: a marker behind the striker, on the line, cannot block a ball moving away from him");
                Assert.That(ShotBlock.IsOffered(-200, 30, 0, 0, ballX, 0, Step, 0), Is.False,
                    $"ball at {ballX}: nor one behind him and beside it");
            }
        }

        [Test]
        public void AManPastTheStrikePoint_IsOffered_OnceTheBallIsLevelWithHim()
        {
            Assert.That(ShotBlock.IsOffered(200, 0, 0, 0, BallX, 0, Step, 0), Is.True, "on the line, the ball just past him");
            Assert.That(ShotBlock.IsOffered(BallX, 40, 0, 0, BallX, 0, Step, 0), Is.True, "beside the line, the ball level with him");
            Assert.That(ShotBlock.IsOffered(500, 0, 0, 0, BallX, 0, Step, 0), Is.False, "not yet: the ball has not reached him");
        }

        [Test]
        public void TheOddsOnTheLine_AreReadFromTheConfig()
        {
            Assert.That(ShotBlock.Permille(0, Reach, FarFromStrike, Cfg(onLine: 400)), Is.EqualTo(400));
            Assert.That(ShotBlock.Permille(Reach / 2, Reach, FarFromStrike, Cfg(onLine: 400)), Is.EqualTo(200));
            Assert.That(ShotBlock.Permille(0, Reach, 0, Cfg(onLine: 800, closePercent: 200)), Is.EqualTo(1000),
                "never better than certain");
        }
    }
}
