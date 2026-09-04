using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Defending (engine phase 3 — docs/engine/MATCH_ENGINE_PLAN.md §4). Until this phase the team
    /// brain put a marker on every one of the ten opponents, wherever he stood (§1.5), and the
    /// "hold your place in the block" branch of the movement ran 0.0% of the time. Measured, that
    /// meant a defending side's shape WAS the attacking side's shape moved five metres back — the
    /// two rows of the harness agreed to a tenth of a metre — and it is why the defending bands
    /// stayed red through phase 2 while the attacking ones closed.
    ///
    /// Three claims are pinned here, and they are what the phase exists to make true:
    ///
    ///   • a defending side has its OWN shape: narrower, shallower, and with a back line that is
    ///     a line. Every band below is the band real football produces, read off the same
    ///     analyzer the balance harness prints.
    ///   • bodies do not walk through each other. Separation used to push team-mates apart and
    ///     never opponents, so a marker stood literally on top of his man.
    ///   • the Pressing instruction is a TRIGGER, not a decoration: a low block lets the man in
    ///     his own third have it, a high press goes and gets him.
    /// </summary>
    [TestFixture]
    public class DefensiveDutyTests
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

        private static MatchReport Play(ulong seed, MatchTactics? tactics = null) =>
            new MatchEngine().Simulate(
                LineupSelector.BestEleven(_midA), LineupSelector.BestEleven(_midB),
                new Pcg32(seed), tactics);

        private static MatchTactics Both(Pressing pressing) =>
            new MatchTactics(Side(pressing), Side(pressing));

        private static TacticContext Side(Pressing pressing) => new TacticContext(
            new Tactic(Formation.F433,
                new TacticInstructions(Mentality.Balanced, pressing, Tempo.Normal, Width.Normal)),
            new BalanceConfig().Tactics.FamiliarityMax);

        // ------------------------------------------------------------ the shape of a defence

        [Test]
        public void TheDefendingShape_IsItsOwnShape_NotTheAttackingOne()
        {
            var analyzer = new MatchAnalyzer();
            double dw = 0, dd = 0, ds = 0, dgap = 0, dmark = 0, aw = 0, ad = 0;
            int n = 0;

            for (ulong seed = 20; seed < 32; seed++)
            {
                MatchMetrics m = analyzer.Measure(Play(seed))!;
                foreach (SideMetrics side in new[] { m.Home, m.Away })
                {
                    dw += side.Defending.WidthM;
                    dd += side.Defending.DepthM;
                    ds += side.Defending.BackLineSpreadM;
                    dgap += side.Defending.LargestLineGapM;
                    dmark += side.Defending.WithinThreeMetresOfOpponentPercent;
                    aw += side.Attacking.WidthM;
                    ad += side.Attacking.DepthM;
                    n++;
                }
            }

            dw /= n; dd /= n; ds /= n; dgap /= n; dmark /= n; aw /= n; ad /= n;

            TestContext.Out.WriteLine(
                $"[duties] defending {dw:F1} x {dd:F1} m   back line spread {ds:F1} m   " +
                $"biggest hole {dgap:F1} m   an opponent within 3 m {dmark:F1}%");
            TestContext.Out.WriteLine($"[duties] attacking {aw:F1} x {ad:F1} m");

            // The bands real football keeps a block in — the same ones the pitch scenario prints.
            Assert.That(dw, Is.InRange(28.0, 42.0), "a defending block is 28-42 m wide");
            Assert.That(dd, Is.InRange(22.0, 38.0), "a defending block is 22-38 m deep");
            Assert.That(ds, Is.InRange(0.0, 6.0), "a back line is a line: 0-6 m of spread");
            Assert.That(dgap, Is.InRange(0.0, 15.0), "no hole bigger than 15 m between two men");

            // And it is genuinely a different shape from the one the same side attacks in, which
            // is the whole point: before this phase these two rows agreed to a tenth of a metre.
            Assert.That(dd, Is.LessThan(ad - 2.0), "a side defends shallower than it attacks");
            Assert.That(dw, Is.LessThan(aw - 1.0), "a side defends narrower than it attacks");

            // Zonal must not mean passive: somebody is still in an opponent's face.
            Assert.That(dmark, Is.InRange(5.0, 25.0), "the share of the match spent within 3 m of an opponent");
        }

        [Test]
        public void Bodies_DoNotWalkThroughEachOther()
        {
            // A/B on the one knob, so the claim is about the model and not about the calibration:
            // the same match, with the push off and on.
            double loose = OverlapPercent(SeparationStrength(0));
            double kept = OverlapPercent(null);

            TestContext.Out.WriteLine(
                $"[bodies] time spent inside a metre of an opponent: " +
                $"{loose:F3}% with nothing keeping them apart, {kept:F3}% with it");

            Assert.That(kept, Is.LessThan(loose * 0.85),
                "pushing opponents apart has to make a visible difference");
            Assert.That(kept, Is.LessThan(0.10),
                "and men are inside a metre of an opponent for well under a percent of the match");
        }

        private static BalanceConfig SeparationStrength(int percent)
        {
            var cfg = new BalanceConfig();
            cfg.Match.OpponentSeparationStrengthPercent = percent;
            return cfg;
        }

        /// <summary>Share of all opponent pairs, over a whole match, standing inside a metre.</summary>
        private static double OverlapPercent(BalanceConfig? cfg)
        {
            MatchReport report = cfg == null
                ? Play(23)
                : new MatchEngine(cfg).Simulate(
                    LineupSelector.BestEleven(_midA), LineupSelector.BestEleven(_midB), new Pcg32(23));

            PositionStream stream = report.Positions!;
            long pairs = 0, overlapping = 0;

            for (int t = 0; t < stream.TickCount; t++)
            {
                for (int i = 0; i < stream.PlayerCount; i++)
                {
                    PitchPoint mine = stream.HomeAt(t, i);
                    for (int j = 0; j < stream.PlayerCount; j++)
                    {
                        PitchPoint his = stream.AwayAt(t, j);
                        double dx = mine.X - his.X, dy = mine.Y - his.Y;
                        if (Math.Sqrt(dx * dx + dy * dy) / 10.0 < 1.0) overlapping++;
                        pairs++;
                    }
                }
            }

            return 100.0 * overlapping / pairs;
        }

        // ------------------------------------------------------------ the trigger

        [Test]
        public void ALowBlock_LetsHimHaveIt_AndAHighPressGoesAndGetsHim()
        {
            double low = SpaceInOwnThird(Pressing.Low);
            double medium = SpaceInOwnThird(Pressing.Medium);
            double high = SpaceInOwnThird(Pressing.High);

            TestContext.Out.WriteLine(
                $"[press] space left to a man on the ball in his own third: " +
                $"low {low:F2} m   medium {medium:F2} m   high {high:F2} m");

            Assert.That(low, Is.GreaterThan(high + 0.3),
                "a low block leaves the man in his own third alone; a high press does not");
            Assert.That(medium, Is.GreaterThan(high),
                "and the middle setting sits between the two");
        }

        /// <summary>
        /// How much room the man on the ball is given while he is in his own third, averaged over
        /// six matches and both sides. It is the reading the trigger zone exists to move: outside
        /// its zone a side keeps its shape instead of chasing him.
        /// </summary>
        private static double SpaceInOwnThird(Pressing pressing)
        {
            MatchTactics tactics = Both(pressing);
            double total = 0;
            int matches = 0;

            for (ulong seed = 40; seed < 46; seed++)
            {
                PositionStream stream = Play(seed, tactics).Positions!;
                double sum = 0;
                int ticks = 0;

                for (int t = 0; t <= stream.LastTick; t++)
                {
                    if (!stream.TryOwner(stream.Owner[t], out bool home, out int slot)) continue;

                    PitchPoint carrier = home ? stream.HomeAt(t, slot) : stream.AwayAt(t, slot);
                    int depth = home ? carrier.X : Pitch.LengthDm - carrier.X;
                    if (depth > Pitch.LengthDm / 3) continue;

                    double nearest = double.MaxValue;
                    for (int j = 0; j < stream.PlayerCount; j++)
                    {
                        PitchPoint foe = home ? stream.AwayAt(t, j) : stream.HomeAt(t, j);
                        double dx = carrier.X - foe.X, dy = carrier.Y - foe.Y;
                        double metres = Math.Sqrt(dx * dx + dy * dy) / 10.0;
                        if (metres < nearest) nearest = metres;
                    }

                    sum += nearest;
                    ticks++;
                }

                if (ticks == 0) continue;
                total += sum / ticks;
                matches++;
            }

            return matches == 0 ? 0 : total / matches;
        }

        // ------------------------------------------------------------ still a football match

        [Test]
        public void ZonalDoesNotMeanPassive_TheBallIsStillWonBack()
        {
            var analyzer = new MatchAnalyzer();
            double tackles = 0, interceptions = 0;
            int n = 0;

            for (ulong seed = 20; seed < 26; seed++)
            {
                MatchMetrics m = analyzer.Measure(Play(seed))!;
                tackles += m.Home.TacklesWon + m.Away.TacklesWon;
                interceptions += m.Home.Interceptions + m.Away.Interceptions;
                n++;
            }

            TestContext.Out.WriteLine(
                $"[duties] {tackles / n:F0} tackles and {interceptions / n:F0} interceptions a match");

            Assert.That(tackles / n, Is.GreaterThan(40), "a side that never presses never tackles");
            Assert.That(interceptions / n, Is.GreaterThan(40), "nor cuts anything out");
        }

        [Test]
        public void TheDuties_AreDeterministic()
        {
            Assert.That(MatchReportHasher.Hash(Play(77)), Is.EqualTo(MatchReportHasher.Hash(Play(77))));
            Assert.That(MatchReportHasher.Hash(Play(77, Both(Pressing.High))),
                Is.EqualTo(MatchReportHasher.Hash(Play(77, Both(Pressing.High)))));
            Assert.That(MatchReportHasher.Hash(Play(77, Both(Pressing.Low))),
                Is.Not.EqualTo(MatchReportHasher.Hash(Play(77, Both(Pressing.High)))),
                "and the instruction genuinely changes what the picture shows");
        }
    }
}
