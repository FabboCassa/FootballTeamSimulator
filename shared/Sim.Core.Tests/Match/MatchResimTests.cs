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
    /// Task 3.4 — re-simulation from a tick via <see cref="MatchPlan"/>: an empty
    /// plan reproduces the legacy engine exactly; a change injected at minute M
    /// leaves minutes &lt; M byte-identical and only re-rolls the remainder; the
    /// same plan always replays to the same report.
    /// </summary>
    [TestFixture]
    public class MatchResimTests
    {
        private static League _league = null!;
        private static Club _home = null!;
        private static Club _away = null!;
        private static readonly int FamMax = new BalanceConfig().Tactics.FamiliarityMax;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260617));
            _home = _league.Clubs[9];
            _away = _league.Clubs[10];
        }

        private static MatchInput Kickoff() =>
            new MatchInput(LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away));

        private static MatchTactics HomeAttacking() => new MatchTactics(
            new TacticContext(new Tactic(Formation.F433,
                new TacticInstructions(Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Normal)), FamMax),
            TacticContext.Neutral(FamMax));

        [Test]
        public void EmptyPlan_ReproducesLegacySimulate()
        {
            var engine = new MatchEngine();
            Lineup home = LineupSelector.BestEleven(_home);
            Lineup away = LineupSelector.BestEleven(_away);

            MatchReport legacy = engine.Simulate(home, away, new Pcg32(7));
            MatchReport viaPlan = engine.Simulate(new MatchPlan(new MatchInput(home, away)), new Pcg32(7));

            Assert.That(MatchReportHasher.Hash(viaPlan), Is.EqualTo(MatchReportHasher.Hash(legacy)),
                "A plan with no changes must consume the RNG exactly like the legacy path.");
        }

        [Test]
        public void SamePlan_ReplaysIdentically()
        {
            var engine = new MatchEngine();
            MatchPlan plan = new MatchPlan(Kickoff())
                .WithChange(61, new MatchInput(LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away), HomeAttacking()));

            MatchReport a = engine.Simulate(plan, new Pcg32(99));
            MatchReport b = engine.Simulate(plan, new Pcg32(99));

            Assert.That(MatchReportHasher.Hash(b), Is.EqualTo(MatchReportHasher.Hash(a)),
                "Re-running the same plan with the same seed must reproduce the report (replay).");
        }

        [Test]
        public void ChangeAtMinuteM_LeavesPrefixIdentical_AndDivergesAfter()
        {
            const int m = 45;
            var engine = new MatchEngine();
            int diverged = 0;

            for (ulong seed = 1; seed <= 60; seed++)
            {
                MatchReport baseline = engine.Simulate(new MatchPlan(Kickoff()), new Pcg32(seed));
                MatchReport changed = engine.Simulate(
                    new MatchPlan(Kickoff()).WithChange(m + 1,
                        new MatchInput(LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away), HomeAttacking())),
                    new Pcg32(seed));

                List<MatchEvent> prefixBase = baseline.Events.Where(e => e.Minute <= m).ToList();
                List<MatchEvent> prefixChanged = changed.Events.Where(e => e.Minute <= m).ToList();

                Assert.That(Describe(prefixChanged), Is.EqualTo(Describe(prefixBase)),
                    $"Seed {seed}: events up to minute {m} must be byte-identical after a change at {m + 1}.");

                if (MatchReportHasher.Hash(changed) != MatchReportHasher.Hash(baseline))
                    diverged++;
            }

            Assert.That(diverged, Is.GreaterThan(0),
                "A strong tactic change in the second half should alter the outcome on at least some seeds.");
        }

        [Test]
        public void AttackingChangeAtHalfTime_RaisesRemainderScoring()
        {
            const int m = 45;
            var engine = new MatchEngine();
            int baseGoals = 0, changedGoals = 0;

            for (ulong seed = 1; seed <= 200; seed++)
            {
                MatchReport baseline = engine.Simulate(new MatchPlan(Kickoff()), new Pcg32(seed));
                MatchReport changed = engine.Simulate(
                    new MatchPlan(Kickoff()).WithChange(m + 1,
                        new MatchInput(LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away), HomeAttacking())),
                    new Pcg32(seed));

                baseGoals += RemainderHomeGoals(baseline, m);
                changedGoals += RemainderHomeGoals(changed, m);
            }

            TestContext.Out.WriteLine(
                $"[resim] home goals after minute {m}: baseline {baseGoals} vs attacking switch {changedGoals}");

            Assert.That(changedGoals, Is.GreaterThan(baseGoals),
                "Switching to a strongly attacking setup at half-time should raise second-half scoring in aggregate.");
        }

        [Test]
        public void SubstitutionAtMinuteM_KeepsPrefixIdentical_AndStaysValid()
        {
            const int m = 70;
            var engine = new MatchEngine();

            MatchReport baseline = engine.Simulate(new MatchPlan(Kickoff()), new Pcg32(123));

            // Substitute one home outfield player for a bench player from minute m+1.
            Lineup subbed = WithOneSubstitution(_home, LineupSelector.BestEleven(_home));
            MatchPlan plan = new MatchPlan(Kickoff())
                .WithChange(m + 1, new MatchInput(subbed, LineupSelector.BestEleven(_away)));

            MatchReport changed = engine.Simulate(plan, new Pcg32(123));

            Assert.That(Describe(changed.Events.Where(e => e.Minute <= m)),
                Is.EqualTo(Describe(baseline.Events.Where(e => e.Minute <= m))),
                "A substitution at m+1 must not alter the events before it.");
            Assert.That(changed.Positions, Is.Not.Null,
                "Re-sim with a substituted XI must complete and still produce a position stream.");
            Assert.That(changed.Events.All(e => e.Minute >= 1 && e.Minute <= 90), Is.True);
        }

        /// <summary>Replaces the first non-GK starter with a squad player not already in the XI.</summary>
        private static Lineup WithOneSubstitution(Club club, Lineup eleven)
        {
            var ids = new HashSet<int>(eleven.Slots.Select(s => s.Player.Id));
            Player bench = club.Squad.Players.First(p => !ids.Contains(p.Id));

            var result = new Lineup { ClubId = eleven.ClubId };
            bool swapped = false;
            foreach (LineupSlot slot in eleven.Slots)
            {
                if (!swapped && slot.Role != PositionRole.Goalkeeper)
                {
                    result.Slots.Add(new LineupSlot { Role = slot.Role, Player = bench });
                    swapped = true;
                }
                else
                {
                    result.Slots.Add(new LineupSlot { Role = slot.Role, Player = slot.Player });
                }
            }

            return result;
        }

        private static int RemainderHomeGoals(MatchReport report, int afterMinute) =>
            report.Events.Count(e =>
                e.Type == MatchEventType.Goal && e.ClubId == report.HomeClubId && e.Minute > afterMinute);

        private static string Describe(IEnumerable<MatchEvent> events) =>
            string.Join("|", events.Select(e => $"{e.Minute},{(int)e.Type},{e.ClubId},{e.PlayerId}"));
    }
}
