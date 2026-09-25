using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R1 on the pitch: with Brain = V11, over 1,000 watched matches, every side spends some time
    /// in every phase of every match, and the time share per phase is printed for the eye.
    ///
    /// Statistically sensitive: a phase that is rare in one match (a side that never wins the ball
    /// back live, say) fails it. Run with --logger "console;verbosity=detailed" for the numbers.
    /// A watched match costs about half a second, so the sweep runs matches in parallel — each on
    /// its own simulator and seed, so the result does not depend on the scheduling.
    /// </summary>
    [TestFixture]
    [Category("Harness")]
    public class TeamPhaseHarnessTests
    {
        private const int Matches = 1000;
        private const ulong FirstSeed = 32_000;

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        [Test]
        public void Harness_V11_EveryPhaseAboveZero_InEachOf1000Matches()
        {
            int phases = TeamPhaseMachine.PhaseCount;
            var share = new double[Matches, 2, phases];
            var ticks = new int[Matches, 2];
            var cfg = new BalanceConfig().Match;
            cfg.Brain = MatchBrainVersion.V11;

            Parallel.For(0, Matches, i =>
            {
                Club home = _league.Clubs[(2 * i) % _league.Clubs.Count];
                Club away = _league.Clubs[(2 * i + 1) % _league.Clubs.Count];
                var sim = new MatchSimulator(cfg);
                sim.Generate(LineupSelector.BestEleven(home), LineupSelector.BestEleven(away),
                    new MatchReport { HomeClubId = home.Id, AwayClubId = away.Id },
                    new Pcg32(FirstSeed + (ulong)i), null);

                for (int side = 0; side < 2; side++)
                {
                    int total = 0;
                    for (int p = 0; p < phases; p++) total += sim.PhaseTicks(side, (TeamPhase)p);
                    ticks[i, side] = total;
                    for (int p = 0; p < phases; p++)
                        share[i, side, p] = sim.PhaseTicks(side, (TeamPhase)p) * 100.0 / total;
                }
            });

            int lastTick = 90 * cfg.TicksPerMinute;
            Assert.That(ticks.Cast<int>().All(t => t == lastTick), Is.True,
                "every tick puts each side in exactly one phase");

            int failures = 0;
            TestContext.Out.WriteLine($"V11 phase share over {Matches} matches (% of ticks, per side):");
            for (int p = 0; p < phases; p++)
            {
                var values = Enumerable.Range(0, Matches)
                    .SelectMany(i => new[] { share[i, 0, p], share[i, 1, p] }).ToArray();
                int zero = values.Count(v => v <= 0);
                failures += zero;
                TestContext.Out.WriteLine(
                    $"  {(TeamPhase)p,-18} min {values.Min(),6:F2}  mean {values.Average(),6:F2}  max {values.Max(),6:F2}  zero {zero}");
            }

            Assert.That(failures, Is.Zero, "every phase must occupy >0% of every side's match");
        }

        [Test]
        public void V10_CountsNoPhases()
        {
            var sim = new MatchSimulator(new BalanceConfig().Match);
            sim.Generate(LineupSelector.BestEleven(_league.Clubs[0]), LineupSelector.BestEleven(_league.Clubs[1]),
                new MatchReport(), new Pcg32(7), null);

            foreach (TeamPhase p in Enum.GetValues(typeof(TeamPhase)))
                Assert.That(sim.PhaseTicks(0, p) + sim.PhaseTicks(1, p), Is.Zero,
                    "the phase machine is V11's; V10 does not run it");
        }
    }
}
