using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using Sim.Core.Match.Movement.Models;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R3 (real-match spec), the mechanism behind the quality-tier gap: a man with less passing
    /// skill trusts his short game less and goes long more often, and misplaces more of what he
    /// plays into the other half without knowing it, from his ratings alone. The valuation tests
    /// use the same scripted positions and instructions, only Passing changes; home attacks
    /// toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11SkillDirectnessTests
    {
        private const int TopSpeed = 75;   // dm/s
        private const int Mid = Pitch.CenterY;
        private const int BallX = 350;

        private MatchBalance _cfg = null!;
        private V11ActionValuation _valuation = null!;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _valuation = new V11ActionValuation(_cfg);
        }

        private static PitchActor At(int x, int y) => new PitchActor(x, y, 0, 0, TopSpeed);

        /// <summary>
        /// A safe ball to a man 15 m away, or a long one to a man 33-45 m upfield whose marker
        /// stands <paramref name="markerDm"/> off him. Mate 0 is the long option.
        /// </summary>
        private static V11Scene Scene(int passing, int longDx, int markerDm)
        {
            var s = new V11Scene
            {
                BallXDm = BallX,
                BallYDm = Mid,
                Shooting = 60,
                Technique = 60,
                Passing = passing,
                CarryTouchDm = 100,
                CarryKeepPermille = 500,
                Instructions = TacticInstructions.Neutral
            };
            s.AddFoe(At(Pitch.LengthDm - 5, Mid), keeper: true);
            s.AddMate(At(BallX + longDx, Mid), keeper: false);
            s.AddMate(At(BallX + 60, Mid + 140), keeper: false);
            s.AddFoe(At(BallX + longDx + 20, Mid + markerDm), keeper: false);
            return s;
        }

        /// <summary>How many of the scripted situations he answers with the long ball.</summary>
        private int LongChoices(int passing)
        {
            int count = 0;
            for (int longDx = 330; longDx <= 450; longDx += 20)
            for (int markerDm = 10; markerDm <= 300; markerDm += 5)
            {
                V11Choice c = _valuation.Choose(Scene(passing, longDx, markerDm), holding: false);
                if (c.Kind == V11ActionKind.Pass && c.Mate == 0) count++;
            }
            return count;
        }

        [Test]
        public void EqualSituation_LowerPassing_PicksTheLongOptionMoreOften()
        {
            int weak = LongChoices(passing: 35);
            int strong = LongChoices(passing: 90);
            TestContext.Out.WriteLine($"long ball chosen: passing 35 {weak}, 90 {strong}");

            // His poorer foot makes the long ball less likely to come off, yet he still goes long more.
            Assert.That(weak, Is.GreaterThan(strong), "a poorer passer goes long more often");
        }

        /// <summary>
        /// The odds he gives a 45 m ball to a free man (nobody near the lane or the receiver, so
        /// only his own foot can miss it), by a passer whose Passing and Technique are both
        /// <paramref name="skill"/>.
        /// </summary>
        private int LongBallOdds(int skill)
        {
            var s = new V11Scene
            {
                BallXDm = BallX,
                BallYDm = Mid,
                Shooting = 60,
                Technique = skill,
                Passing = skill,
                CarryTouchDm = 100,
                CarryKeepPermille = 500,
                Instructions = TacticInstructions.Neutral
            };
            s.AddFoe(At(Pitch.LengthDm - 5, Mid), keeper: true);
            s.AddMate(At(BallX + 450, Mid), keeper: false);
            _valuation.PassValue(s, out V11Choice pass);
            Assert.That(pass.Mate, Is.EqualTo(0), "the long ball is priced");
            return pass.SafetyPermille;
        }

        [Test]
        public void UnderTheLowSkillMark_HeDoesNotPriceTheExtraMiss_SoHeDoesNotPlaySafer()
        {
            int poor = _cfg.PassErrorLowSkillFrom - 24;
            int withExtra = LongBallOdds(poor);

            _cfg.PassErrorLowSkillPermillePerPoint = 0;
            _valuation = new V11ActionValuation(_cfg);
            int withoutExtra = LongBallOdds(poor);
            TestContext.Out.WriteLine($"long ball odds at skill {poor}: {withExtra} (linear error alone {withoutExtra})");

            Assert.That(withExtra, Is.LessThan(1000), "his foot is priced at all");
            Assert.That(withExtra, Is.EqualTo(withoutExtra), "the low-skill extra is not in his own reading");
        }

        /// <summary>
        /// The extra miss itself, over a few watched matches between two mid-table sides (most of
        /// their outfielders sit under the mark): the same seeds with the extra on and off.
        /// STATISTICALLY FRAGILE: eight matches. The gap at these settings is several points
        /// (the mark is well above most of these men); only a lost effect should trip it.
        /// </summary>
        [Test]
        public void UnderTheLowSkillMark_PassesIntoTheirHalf_GoAstrayMoreOften()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Lineup a = LineupSelector.BestEleven(league.Clubs[9]), b = LineupSelector.BestEleven(league.Clubs[10]);

            double on = PassAccuracy(new BalanceConfig(), a, b);
            var off = new BalanceConfig();
            off.Match.PassErrorLowSkillPermillePerPoint = 0;
            double without = PassAccuracy(off, a, b);
            TestContext.Out.WriteLine($"pass accuracy: {on:F2}% with the low-skill miss, {without:F2}% without");

            Assert.That(on, Is.LessThan(without - 1.0), "the poorer passers misplace more");
        }

        private static double PassAccuracy(BalanceConfig cfg, Lineup a, Lineup b)
        {
            const int Matches = 8;
            ShotPassMetrics? sum = null;
            for (int i = 0; i < Matches; i++)
            {
                var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: true, generatePositions: true);
                MatchReport r = engine.Simulate(a, b, new Pcg32(83_000 + (ulong)i));
                ShotPassMetrics m = ShotPassAnalyzer.Analyze(r)!;
                sum = sum == null ? m : sum.Plus(m);
            }
            return sum!.PassAccuracyPercent;
        }
    }
}
