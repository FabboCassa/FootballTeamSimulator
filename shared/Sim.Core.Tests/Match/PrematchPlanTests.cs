using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Task 3.5 — conditional pre-match plans. A rule that fires at the first
    /// minute its (minute + scoreline) gate opens is exactly equivalent to a static
    /// <see cref="MatchInputChange"/> injected at that minute (the proven 3.4 path),
    /// so we verify triggers by that equivalence across many scenarios. Empty/never-
    /// firing rule sets are byte-identical to the legacy engine, and the same plan
    /// always replays identically.
    /// </summary>
    [TestFixture]
    public class PrematchPlanTests
    {
        private static readonly int FamMax = new BalanceConfig().Tactics.FamiliarityMax;

        private static League _league = null!;
        private static Club _home = null!;
        private static Club _away = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260618));
            _home = _league.Clubs[3];
            _away = _league.Clubs[4];
        }

        // --- Fixtures of inputs -------------------------------------------------

        private static MatchTactics NeutralTactics() =>
            new MatchTactics(TacticContext.Neutral(FamMax), TacticContext.Neutral(FamMax));

        private static MatchInput Kickoff() =>
            new MatchInput(LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away), NeutralTactics());

        private static MatchPlan KickoffPlan() => new MatchPlan(Kickoff());

        private static RuleAction AttackAction() =>
            new RuleAction(new TacticInstructions(Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Normal), FamMax);

        private static RuleAction DefendAction() =>
            new RuleAction(new TacticInstructions(Mentality.Defensive, Pressing.Low, Tempo.Slow, Width.Narrow), FamMax);

        private static RuleAction SubAction(Club club)
        {
            Lineup xi = LineupSelector.BestEleven(club);
            var ids = new HashSet<int>(xi.Slots.Select(s => s.Player.Id));
            Player bench = club.Squad.Players.First(p => !ids.Contains(p.Id));
            int outId = xi.Slots.First(s => s.Role != PositionRole.Goalkeeper).Player.Id;
            return new RuleAction(sub: new Substitution(outId, bench));
        }

        // --- Identity & determinism --------------------------------------------

        [Test]
        public void NullRules_ReproduceLegacySimulate()
        {
            var engine = new MatchEngine();
            Lineup home = LineupSelector.BestEleven(_home);
            Lineup away = LineupSelector.BestEleven(_away);

            MatchReport legacy = engine.Simulate(home, away, new Pcg32(11));
            MatchReport viaRules = engine.Simulate(new MatchPlan(new MatchInput(home, away)), null, null, new Pcg32(11));

            Assert.That(MatchReportHasher.Hash(viaRules), Is.EqualTo(MatchReportHasher.Hash(legacy)),
                "Null rule lists must consume the RNG exactly like the legacy path.");
        }

        [Test]
        public void EmptyRuleLists_ReproduceLegacySimulate()
        {
            var engine = new MatchEngine();
            MatchReport legacy = engine.Simulate(KickoffPlan(), new Pcg32(12));
            MatchReport viaRules = engine.Simulate(KickoffPlan(), new MatchRule[0], new MatchRule[0], new Pcg32(12));

            Assert.That(MatchReportHasher.Hash(viaRules), Is.EqualTo(MatchReportHasher.Hash(legacy)),
                "Empty rule lists must be byte-identical to the legacy path.");
        }

        [Test]
        public void NeverFiringRule_IsByteIdentical()
        {
            var engine = new MatchEngine();
            // "Winning by >= 5" essentially never holds in a normal match.
            var rule = new MatchRule(1, ScoreSituation.Winning, AttackAction(), margin: 5);

            for (ulong seed = 1; seed <= 50; seed++)
            {
                MatchReport baseline = engine.Simulate(KickoffPlan(), new Pcg32(seed));
                MatchReport withRule = engine.Simulate(KickoffPlan(), new[] { rule }, null, new Pcg32(seed));

                Assert.That(MatchReportHasher.Hash(withRule), Is.EqualTo(MatchReportHasher.Hash(baseline)),
                    $"Seed {seed}: a rule whose gate never opens must not change the report.");
            }
        }

        [Test]
        public void SameRules_ReplayIdentically()
        {
            var engine = new MatchEngine();
            var rules = new[] { new MatchRule(46, ScoreSituation.Always, AttackAction()) };

            MatchReport a = engine.Simulate(KickoffPlan(), rules, null, new Pcg32(77));
            MatchReport b = engine.Simulate(KickoffPlan(), rules, null, new Pcg32(77));

            Assert.That(MatchReportHasher.Hash(b), Is.EqualTo(MatchReportHasher.Hash(a)),
                "Re-running the same plan + rules with the same seed must reproduce the report.");
        }

        [Test]
        public void FiringRule_LeavesPrefixIdentical()
        {
            const int m = 45;
            var engine = new MatchEngine();
            var rules = new[] { new MatchRule(m, ScoreSituation.Always, AttackAction()) };

            for (ulong seed = 1; seed <= 40; seed++)
            {
                MatchReport baseline = engine.Simulate(KickoffPlan(), new Pcg32(seed));
                MatchReport withRule = engine.Simulate(KickoffPlan(), rules, null, new Pcg32(seed));

                Assert.That(Describe(withRule.Events.Where(e => e.Minute < m)),
                    Is.EqualTo(Describe(baseline.Events.Where(e => e.Minute < m))),
                    $"Seed {seed}: events before the firing minute {m} must be unchanged.");
            }
        }

        // --- 10 trigger scenarios ----------------------------------------------

        [Test]
        public void TenScenarios_FireExactlyLikeAStaticChange()
        {
            var scenarios = new (string name, MatchRule rule, bool home)[]
            {
                ("minute-only @1 attack",        new MatchRule(1,  ScoreSituation.Always,     AttackAction()),            true),
                ("minute-only @60 defend",       new MatchRule(60, ScoreSituation.Always,     DefendAction()),            true),
                ("minute-only @75 substitution", new MatchRule(75, ScoreSituation.Always,     SubAction(_home)),          true),
                ("losing @1 -> attack",          new MatchRule(1,  ScoreSituation.Losing,     AttackAction()),            true),
                ("winning @60 -> defend",        new MatchRule(60, ScoreSituation.Winning,    DefendAction()),            true),
                ("drawing @70 -> attack",        new MatchRule(70, ScoreSituation.Drawing,    AttackAction()),            true),
                ("not-losing @80 -> defend",     new MatchRule(80, ScoreSituation.NotLosing,  DefendAction()),            false),
                ("not-winning @65 -> attack",    new MatchRule(65, ScoreSituation.NotWinning, AttackAction()),            false),
                ("losing by 2 @1 -> attack",     new MatchRule(1,  ScoreSituation.Losing,     AttackAction(), margin: 2), true),
                ("winning by 5 @1 -> defend",    new MatchRule(1,  ScoreSituation.Winning,    DefendAction(), margin: 5), true),
            };

            int fired = 0, never = 0;
            foreach ((string name, MatchRule rule, bool home) in scenarios)
            {
                int firesThis = 0;
                for (ulong seed = 1; seed <= 40; seed++)
                {
                    if (AssertConditionalEqualsStatic(seed, rule, home))
                        firesThis++;
                }

                if (firesThis > 0) fired++; else never++;
                TestContext.Out.WriteLine($"[3.5] scenario '{name}': fired on {firesThis}/40 seeds.");
            }

            Assert.That(scenarios.Length, Is.EqualTo(10), "Exactly ten scenarios are exercised.");
            Assert.That(fired, Is.GreaterThanOrEqualTo(8),
                "Most scenarios should fire on at least one seed (sanity that the harness exercises the firing path).");
        }

        /// <summary>
        /// Asserts that a one-rule conditional run equals a static change injected at
        /// the first minute the rule's gate opens (or equals the baseline when it
        /// never opens). Returns whether the rule fired.
        /// </summary>
        private static bool AssertConditionalEqualsStatic(ulong seed, MatchRule rule, bool isHome)
        {
            var engine = new MatchEngine();
            MatchPlan plan = KickoffPlan();
            MatchReport baseline = engine.Simulate(plan, new Pcg32(seed));

            int m = FindFireMinute(baseline, rule, isHome);

            IReadOnlyList<MatchRule>? homeRules = isHome ? new[] { rule } : null;
            IReadOnlyList<MatchRule>? awayRules = isHome ? null : new[] { rule };
            MatchReport actual = engine.Simulate(plan, homeRules, awayRules, new Pcg32(seed));

            if (m < 0)
            {
                Assert.That(MatchReportHasher.Hash(actual), Is.EqualTo(MatchReportHasher.Hash(baseline)),
                    $"Seed {seed}: gate never opens, so the report must be unchanged.");
                return false;
            }

            MatchInput x = MatchRuleApplier.Apply(plan.Initial, rule, isHome, FamMax);
            MatchReport expected = engine.Simulate(plan.WithChange(m, x), new Pcg32(seed));

            Assert.That(MatchReportHasher.Hash(actual), Is.EqualTo(MatchReportHasher.Hash(expected)),
                $"Seed {seed}: conditional fire at minute {m} must equal a static change at minute {m}.");
            return true;
        }

        /// <summary>First minute (&gt;= FromMinute) at which the rule's scoreline gate holds, using the baseline score; -1 if never.</summary>
        private static int FindFireMinute(MatchReport baseline, MatchRule rule, bool isHome)
        {
            int ownClub = isHome ? _home.Id : _away.Id;
            int oppClub = isHome ? _away.Id : _home.Id;

            for (int minute = rule.FromMinute; minute <= 90; minute++)
            {
                int own = GoalsBefore(baseline, minute, ownClub);
                int opp = GoalsBefore(baseline, minute, oppClub);
                if (rule.ConditionMet(own, opp))
                    return minute;
            }

            return -1;
        }

        private static int GoalsBefore(MatchReport report, int minute, int clubId) =>
            report.Events.Count(e => e.Type == MatchEventType.Goal && e.ClubId == clubId && e.Minute < minute);

        // --- Serializable plan resolution --------------------------------------

        [Test]
        public void PrematchPlan_Resolve_KeepsValidRules_DropsInvalid()
        {
            Lineup xi = LineupSelector.BestEleven(_home);
            int starterId = xi.Slots.First(s => s.Role != PositionRole.Goalkeeper).Player.Id;
            var ids = new HashSet<int>(xi.Slots.Select(s => s.Player.Id));
            int benchId = _home.Squad.Players.First(p => !ids.Contains(p.Id)).Id;

            var plan = new PrematchPlan
            {
                Rules =
                {
                    // valid: instruction change
                    new PrematchRule
                    {
                        FromMinute = 60, When = ScoreSituation.Losing, ChangeInstructions = true,
                        Mentality = Mentality.Attacking, Pressing = Pressing.High, Tempo = Tempo.Fast, Width = Width.Normal
                    },
                    // valid: substitution
                    new PrematchRule { FromMinute = 70, SubOutPlayerId = starterId, SubInPlayerId = benchId },
                    // dropped: incoming player not in squad
                    new PrematchRule { FromMinute = 70, SubOutPlayerId = starterId, SubInPlayerId = 999999 },
                    // dropped: empty action
                    new PrematchRule { FromMinute = 80, When = ScoreSituation.Drawing }
                }
            };

            List<MatchRule> resolved = plan.Resolve(_home, _ => FamMax, FamMax);

            Assert.That(resolved.Count, Is.EqualTo(2), "Only the two well-formed rules should resolve.");

            MatchRule instr = resolved[0];
            Assert.That(instr.FromMinute, Is.EqualTo(60));
            Assert.That(instr.When, Is.EqualTo(ScoreSituation.Losing));
            Assert.That(instr.Action.Instructions, Is.Not.Null);
            Assert.That(instr.Action.Instructions!.Value.Mentality, Is.EqualTo(Mentality.Attacking));
            Assert.That(instr.Action.InstructionFamiliarity, Is.EqualTo(FamMax));
            Assert.That(instr.Action.Sub, Is.Null);

            MatchRule sub = resolved[1];
            Assert.That(sub.Action.Sub, Is.Not.Null);
            Assert.That(sub.Action.Sub!.OutPlayerId, Is.EqualTo(starterId));
            Assert.That(sub.Action.Sub!.In.Id, Is.EqualTo(benchId));
        }

        [Test]
        public void PrematchPlan_ResolvedRule_FiresLikeAStaticChange()
        {
            // A resolved plan must drive the engine identically to a hand-built rule:
            // round-trip through the serializable shape, then assert the equivalence.
            var plan = new PrematchPlan
            {
                Rules =
                {
                    new PrematchRule
                    {
                        FromMinute = 1, When = ScoreSituation.Losing, ChangeInstructions = true,
                        Mentality = Mentality.Attacking, Pressing = Pressing.High, Tempo = Tempo.Fast, Width = Width.Normal
                    }
                }
            };

            MatchRule resolved = plan.Resolve(_home, _ => FamMax, FamMax).Single();

            int fired = 0;
            for (ulong seed = 1; seed <= 40; seed++)
                if (AssertConditionalEqualsStatic(seed, resolved, isHome: true))
                    fired++;

            Assert.That(fired, Is.GreaterThan(0), "A 'while losing, attack' rule should fire on at least some seeds.");
        }

        private static string Describe(IEnumerable<MatchEvent> events) =>
            string.Join("|", events.Select(e => $"{e.Minute},{(int)e.Type},{e.ClubId},{e.PlayerId}"));
    }
}
