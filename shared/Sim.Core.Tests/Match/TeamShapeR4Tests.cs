using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Real-match spec R4, team shape: a compact block out of possession, width in possession,
    /// and men who stay near their place in the shape. The first two are scripted off the V11
    /// phase target spots (no match is played); the third plays forty matches. The bands
    /// themselves are read over 1,000 matches by <see cref="RealismHarnessTests"/>.
    /// Home (side 0) attacks toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class TeamShapeR4Tests
    {
        private const int TouchlineZoneDm = 80;
        private const int Home = 0;
        private const int Away = 1;

        private static readonly Formation[] Shapes = { Formation.F433, Formation.F442 };

        private MatchBalance _cfg = null!;
        private V11Positioning _positioning = null!;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _positioning = new V11Positioning(_cfg);
        }

        /// <summary>The home side's outfield extents, every man on his phase target spot.</summary>
        private (int Length, int Width, int MinY, int MaxY) Extents(
            Formation formation, bool inPossession, int ballXDm, int ballYDm)
        {
            var phases = new TeamPhaseMachine(_cfg);
            phases.Update(0, ballXDm, inPossession ? Home : Away, deadBall: false, restartSide: -1);
            TeamPhase phase = phases.PhaseOf(Home);

            var roles = Formations.Roles(formation);
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < roles.Length; i++)
            {
                if (roles[i] == PositionRole.Goalkeeper) continue;
                _positioning.PhaseSpot(V11Slot.InFormation(roles, i, _cfg), true, phase, inPossession,
                    TacticInstructions.Neutral, ballXDm, ballYDm, out int x, out int y);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            return (maxX - minX, maxY - minY, minY, maxY);
        }

        [Test]
        public void ScriptedDefensivePhase_TheBlockIsCompact_InItsOwnHalfAndAnywhere()
        {
            foreach (Formation formation in Shapes)
            {
                // The opponents have the ball in our half: on the edge of our box, in our third, at halfway.
                foreach (int ballX in new[] { 200, 300, 400, 500 })
                foreach (int ballY in new[] { 100, 200, Pitch.CenterY, 480, 580 })
                {
                    var block = Extents(formation, inPossession: false, ballX, ballY);
                    string at = $"{formation}, ball at ({ballX}, {ballY})";
                    Assert.That(block.Length, Is.InRange(250, 400), $"{at}: block length in its own half");

                    // Squeezed against the touchline by a ball out on the flank the block is
                    // narrower still; R4's 30-45 m is the width of the block across the middle.
                    if (ballY < 200 || ballY > 480) Assert.That(block.Width, Is.LessThanOrEqualTo(450), $"{at}: block width");
                    else Assert.That(block.Width, Is.InRange(300, 450), $"{at}: block width");
                }

                // Wherever the ball is, the block is never stretched past 45 m.
                for (int ballX = 0; ballX <= Pitch.LengthDm; ballX += 50)
                    Assert.That(Extents(formation, false, ballX, Pitch.CenterY).Length, Is.LessThanOrEqualTo(450),
                        $"{formation}, ball at x {ballX}: block length anywhere");
            }
        }

        [Test]
        public void InPossession_InTheOppositionHalf_TheShapeIsWide_AndMansBothTouchlineZones()
        {
            foreach (Formation formation in Shapes)
            foreach (int ballX in new[] { 600, 750, 900 })
            foreach (int ballY in new[] { Pitch.CenterY - 50, Pitch.CenterY, Pitch.CenterY + 50 })
            {
                // The ball in the central channel: the shape slides toward it and still holds both touchlines.
                var shape = Extents(formation, inPossession: true, ballX, ballY);
                string at = $"{formation}, ball at ({ballX}, {ballY})";
                Assert.That(shape.Width, Is.GreaterThanOrEqualTo(450), $"{at}: width");
                Assert.That(shape.MinY, Is.LessThanOrEqualTo(TouchlineZoneDm), $"{at}: a man within 8 m of one touchline");
                Assert.That(shape.MaxY, Is.GreaterThanOrEqualTo(Pitch.WidthDm - TouchlineZoneDm),
                    $"{at}: a man within 8 m of the other");
            }
        }

        /// <summary>
        /// R4's off-target time (OffTargetMeter, every open-play frame more than 25 m off target):
        /// the reading the realism harness takes, the median over matches of each match's median
        /// outfielder, over forty matches. Fragility: it reads 4.5 s here against the 5 s band;
        /// forty-match windows of a 200-match run read 4.15-4.50 and sixteen-match ones up to
        /// 4.85, per-match medians run 3.2-5.6 s (p10-p90). The margin is half a second, so a
        /// change to the V11 brain can trip it; the 1,000-match figure is the harness's.
        /// </summary>
        [Test]
        public void OffTarget_TheMedianMatch_IsWithinTheFiveSecondBand()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Lineup a = LineupSelector.BestEleven(league.Clubs[9]), b = LineupSelector.BestEleven(league.Clubs[10]);
            var engine = new MatchEngine(new BalanceConfig(), applyCondition: true, applyMatchFatigue: true,
                applyPositioning: true, generatePositions: true);
            var analyzer = new ShapeMovementAnalyzer();

            const int matches = 40;
            var medians = new double[matches];
            for (int i = 0; i < matches; i++)
            {
                MatchReport r = i % 2 == 0
                    ? engine.Simulate(a, b, new Pcg32(30_000UL + (ulong)i))
                    : engine.Simulate(b, a, new Pcg32(30_000UL + (ulong)i));
                ShapeMovementMetrics? m = analyzer.Measure(r);
                Assert.That(m, Is.Not.Null, "a watched match carries its stream");
                medians[i] = m!.MedianOffTargetSeconds;
            }

            Array.Sort(medians);
            double median = (medians[matches / 2 - 1] + medians[matches / 2]) / 2;
            Assert.That(median, Is.LessThanOrEqualTo(ShapeMovementTargets.MedianOffTargetSeconds.Max),
                "off-target seconds of the median outfielder, median match");
        }
    }
}
