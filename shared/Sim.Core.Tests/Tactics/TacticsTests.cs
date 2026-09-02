using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Tactics
{
    /// <summary>
    /// Task 3.2 acceptance: tactics matter (counter edge, familiarity), determinism
    /// is preserved, and no tactic dominates the field (&gt; 55% win rate).
    /// </summary>
    [TestFixture]
    public class TacticsTests
    {
        private static League _league = null!;
        private static Club _club = null!;     // equal-squads tests use one club on both sides
        private static readonly BalanceConfig _cfg = new BalanceConfig();
        private static int FamMax => _cfg.Tactics.FamiliarityMax;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260615));
            _club = _league.Clubs[9]; // a mid club; exact strength is irrelevant for equal-squad tests
        }

        // Plays A vs B with equal squads (same club), returning A's and B's goals.
        // A alternates home/away across seeds so home advantage cancels out.
        private static void PlayTactic(
            Tactic ta, int famA, Tactic tb, int famB, ulong seed, bool aHome,
            out int aGoals, out int bGoals)
        {
            Lineup la = LineupSelector.BestEleven(_club, ta.Formation);
            Lineup lb = LineupSelector.BestEleven(_club, tb.Formation);
            var ca = new TacticContext(ta, famA);
            var cb = new TacticContext(tb, famB);
            // The instruction/formation field is 81 tactics x 80 games: thousands of matches of
            // which only the SCORE is read. Generating the movement stream for each of them cost
            // ~96ms a match in Debug and was almost the whole runtime of the Sim.Core suite. The
            // stream is derived after every outcome roll and each match here gets its own seed,
            // so turning it off is result-identical (verified over 1296 matches).
            var engine = new MatchEngine(_cfg, generatePositions: false);

            if (aHome)
            {
                MatchReport r = engine.Simulate(la, lb, new Pcg32(seed), new MatchTactics(ca, cb));
                aGoals = r.HomeGoals; bGoals = r.AwayGoals;
            }
            else
            {
                MatchReport r = engine.Simulate(lb, la, new Pcg32(seed), new MatchTactics(cb, ca));
                aGoals = r.AwayGoals; bGoals = r.HomeGoals;
            }
        }

        // ------------------------------------------------------------ identity / determinism

        [Test]
        public void NeutralTactic_IsEngineIdentity()
        {
            TacticModifiers.Multipliers m = TacticModifiers.Compute(
                TacticInstructions.Neutral, TacticInstructions.Neutral, FamMax, _cfg.Tactics);
            Assert.That(m.Attack, Is.EqualTo(1.0));
            Assert.That(m.Midfield, Is.EqualTo(1.0));
            Assert.That(m.Defense, Is.EqualTo(1.0));

            Lineup l = LineupSelector.BestEleven(_club);
            string noTactics = JsonSerializer.Serialize(
                new MatchEngine(_cfg).Simulate(l, l, new Pcg32(123)));
            var neutral = new MatchTactics(TacticContext.Neutral(FamMax), TacticContext.Neutral(FamMax));
            string withNeutral = JsonSerializer.Serialize(
                new MatchEngine(_cfg).Simulate(l, l, new Pcg32(123), neutral));

            Assert.That(withNeutral, Is.EqualTo(noTactics),
                "A neutral, fully-familiar tactic must not change the simulation at all");
        }

        [Test]
        public void SameSeedAndTactics_IdenticalReport()
        {
            var t = new MatchTactics(
                new TacticContext(new Tactic(Formation.F4231, new TacticInstructions(
                    Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Wide)), 40),
                new TacticContext(new Tactic(Formation.F532, new TacticInstructions(
                    Mentality.Defensive, Pressing.Low, Tempo.Slow, Width.Narrow)), 80));
            Lineup l = LineupSelector.BestEleven(_club);

            string a = JsonSerializer.Serialize(new MatchEngine(_cfg).Simulate(l, l, new Pcg32(99), t));
            string b = JsonSerializer.Serialize(new MatchEngine(_cfg).Simulate(l, l, new Pcg32(99), t));
            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void AllFormations_BuildValidElevens()
        {
            foreach (Formation f in Formations.All)
            {
                Lineup l = LineupSelector.BestEleven(_club, f);
                Assert.That(l.Slots.Count, Is.EqualTo(11), $"{f} must field 11");
                Assert.DoesNotThrow(() => l.Validate(), $"{f} must be a legal lineup");
            }
        }

        // ------------------------------------------------------------ modifier mechanics (deterministic)

        [Test]
        public void SelfEffects_AreSymmetricTradeoffs()
        {
            TacticsBalance c = _cfg.Tactics;
            // Attacking trades defense for attack, by equal magnitude; midfield untouched.
            TacticModifiers.Multipliers att = TacticModifiers.Compute(
                new TacticInstructions(Mentality.Attacking, Pressing.Medium, Tempo.Normal, Width.Normal),
                TacticInstructions.Neutral, FamMax, c);
            Assert.That(att.Attack, Is.EqualTo((100 + c.MentalitySwingPercent) / 100.0).Within(1e-9));
            Assert.That(att.Defense, Is.EqualTo((100 - c.MentalitySwingPercent) / 100.0).Within(1e-9));
            Assert.That(att.Midfield, Is.EqualTo(1.0).Within(1e-9));

            // Defensive is the mirror image.
            TacticModifiers.Multipliers def = TacticModifiers.Compute(
                new TacticInstructions(Mentality.Defensive, Pressing.Medium, Tempo.Normal, Width.Normal),
                TacticInstructions.Neutral, FamMax, c);
            Assert.That(def.Attack, Is.EqualTo(att.Defense).Within(1e-9));
            Assert.That(def.Defense, Is.EqualTo(att.Attack).Within(1e-9));
        }

        [Test]
        public void Counter_HasCorrectSign_AndIsZeroSumOverTheField()
        {
            TacticsBalance c = _cfg.Tactics;
            var press = new TacticInstructions(Mentality.Balanced, Pressing.High, Tempo.Normal, Width.Normal);
            var fastOpp = new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Fast, Width.Normal);
            var slowOpp = new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Slow, Width.Normal);

            // The press is exposed at the BACK against fast tempo (solid vs slow).
            double defVsFast = TacticModifiers.Compute(press, fastOpp, FamMax, c).Defense;
            double defVsSlow = TacticModifiers.Compute(press, slowOpp, FamMax, c).Defense;
            Assert.That(defVsFast, Is.LessThan(defVsSlow),
                "A high press is exposed defensively by fast tempo and solid against slow build-up");

            // And the fast side gains attack playing through a high press.
            var fast = new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Fast, Width.Normal);
            var highPressOpp = new TacticInstructions(Mentality.Balanced, Pressing.High, Tempo.Normal, Width.Normal);
            var lowBlockOpp = new TacticInstructions(Mentality.Balanced, Pressing.Low, Tempo.Normal, Width.Normal);
            double attVsPress = TacticModifiers.Compute(fast, highPressOpp, FamMax, c).Attack;
            double attVsBlock = TacticModifiers.Compute(fast, lowBlockOpp, FamMax, c).Attack;
            Assert.That(attVsPress, Is.GreaterThan(attVsBlock),
                "Fast tempo gains attacking through a high press, absorbed by a low block");

            // Width counter is cyclic: summed over a uniform field of opponent widths it cancels.
            var wide = new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Wide);
            double vN = TacticModifiers.Compute(wide, WithWidth(Width.Narrow), FamMax, c).Attack;
            double vM = TacticModifiers.Compute(wide, WithWidth(Width.Normal), FamMax, c).Attack;
            double vW = TacticModifiers.Compute(wide, WithWidth(Width.Wide), FamMax, c).Attack;
            // The mirror (Wide vs Wide) carries no counter, so it is the self-only
            // baseline; the field average must land exactly on it (counter nets to zero).
            Assert.That((vN + vM + vW) / 3.0, Is.EqualTo(vW).Within(1e-9),
                "Cyclic width counter must average to zero over the field (no net gain)");
        }

        private static TacticInstructions WithWidth(Width w) =>
            new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Normal, w);

        // ------------------------------------------------------------ counter edge (match harness, informational)

        [Test]
        public void Counter_FastTempoVsHighPress_Harness()
        {
            // Match-level readout of the canonical counter. The exact edge depends on
            // how the counter trades off against the possession swing, so this prints
            // the number for tuning and only guards against a broken blowout; the hard
            // mechanical guarantee is in Counter_HasCorrectSign_AndIsZeroSumOverTheField.
            Tactic faster = new Tactic(Formation.F433, new TacticInstructions(
                Mentality.Balanced, Pressing.Medium, Tempo.Fast, Width.Normal));
            Tactic presser = new Tactic(Formation.F433, new TacticInstructions(
                Mentality.Balanced, Pressing.High, Tempo.Normal, Width.Normal));

            int fasterWins = 0, presserWins = 0, draws = 0;
            for (ulong i = 0; i < 1000; i++)
            {
                PlayTactic(faster, FamMax, presser, FamMax, 1000 + i, i % 2 == 0, out int fg, out int pg);
                if (fg > pg) fasterWins++; else if (fg < pg) presserWins++; else draws++;
            }

            int decisive = fasterWins + presserWins;
            double share = decisive > 0 ? 100.0 * fasterWins / decisive : 50.0;
            TestContext.Out.WriteLine(
                $"[counter] fast-tempo {fasterWins / 10.0}% vs high-press {presserWins / 10.0}% (draws {draws / 10.0}%) | fast share of decided games: {share:F1}% (target ~55-60%)");
            Assert.That(share, Is.InRange(35.0, 65.0),
                "Counter must not produce a blowout either way; tune magnitudes toward the 55-60% target");
        }

        // ------------------------------------------------------------ familiarity edge

        [Test]
        public void Familiarity_FamiliarBeatsFreshlySwitched()
        {
            // Identical tactic on both sides: the only difference is familiarity.
            Tactic shared = new Tactic(Formation.F442, new TacticInstructions(
                Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Normal));

            int familiarWins = 0, freshWins = 0, draws = 0;
            for (ulong i = 0; i < 1000; i++)
            {
                PlayTactic(shared, FamMax, shared, 0, 2000 + i, i % 2 == 0, out int famG, out int freshG);
                if (famG > freshG) familiarWins++;
                else if (famG == freshG) draws++;
                else freshWins++;
            }

            TestContext.Out.WriteLine($"[familiarity] familiar wins: {familiarWins / 10.0}% | fresh wins: {freshWins / 10.0}% | draws: {draws / 10.0}%");
            Assert.That(familiarWins, Is.GreaterThan(freshWins),
                "A familiar tactic must beat the same tactic freshly switched-to");
        }

        // ------------------------------------------------------------ no dominant tactic

        [Test]
        public void NoTactic_DominatesTheField()
        {
            // Sweep all 81 instruction combos on a fixed shape (4-3-3) with equal
            // squads and full familiarity, so only the self-effects + counter-matrix
            // decide. Each tactic plays the whole field, both venues; with zero-sum
            // counters and trade-off self-effects no tactic should clear ~55%.
            var tactics = new List<TacticInstructions>();
            foreach (Mentality me in new[] { Mentality.Defensive, Mentality.Balanced, Mentality.Attacking })
                foreach (Pressing pr in new[] { Pressing.Low, Pressing.Medium, Pressing.High })
                    foreach (Tempo te in new[] { Tempo.Slow, Tempo.Normal, Tempo.Fast })
                        foreach (Width wi in new[] { Width.Narrow, Width.Normal, Width.Wide })
                            tactics.Add(new TacticInstructions(me, pr, te, wi));

            int n = tactics.Count;
            var wins = new int[n];
            var draws = new int[n];
            var games = new int[n];

            for (int a = 0; a < n; a++)
            {
                Tactic ta = new Tactic(Formation.F433, tactics[a]);
                for (int b = 0; b < n; b++)
                {
                    if (a == b) continue;
                    Tactic tb = new Tactic(Formation.F433, tactics[b]);
                    ulong seed = (ulong)(a * 1000 + b) + 50000;
                    PlayTactic(ta, FamMax, tb, FamMax, seed, (a + b) % 2 == 0, out int ag, out int bg);
                    games[a]++;
                    if (ag > bg) wins[a]++;
                    else if (ag == bg) draws[a]++;
                }
            }

            // Acceptance metric is win rate (strict wins / games), per the ✅.
            // Points share = (win + half-draw)/games is reported as a sanity floor.
            double bestWin = 0, worstPoints = 1;
            int bestIdx = 0, worstIdx = 0;
            for (int a = 0; a < n; a++)
            {
                double winRate = games[a] > 0 ? (double)wins[a] / games[a] : 0.0;
                double points = games[a] > 0 ? (wins[a] + 0.5 * draws[a]) / games[a] : 0.5;
                if (winRate > bestWin) { bestWin = winRate; bestIdx = a; }
                if (points < worstPoints) { worstPoints = points; worstIdx = a; }
            }

            TestContext.Out.WriteLine(
                $"[sweep] {n} tactics, {games[0]} games each. Top win rate {bestWin * 100:F1}% ({Describe(tactics[bestIdx])}); lowest points share {worstPoints * 100:F1}% ({Describe(tactics[worstIdx])})");

            // Acceptance criterion: the cap. No tactic may win > ~55% vs the field.
            Assert.That(bestWin, Is.LessThan(0.56),
                "No tactic may win > ~55% vs the whole field (tune counter/self magnitudes if it does)");
            // Sanity tripwire only (NOT an acceptance criterion): a genuinely bad tactic
            // is *meant* to lose - this just catches a totally broken/degenerate one.
            Assert.That(worstPoints, Is.GreaterThan(0.35),
                "No tactic should be utterly broken (points share tripwire)");
        }

        private static string Describe(TacticInstructions t) => $"{t.Mentality}/{t.Pressing}/{t.Tempo}/{t.Width}";
    }
}
