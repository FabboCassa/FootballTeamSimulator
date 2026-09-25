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
    /// R2 on the pitch: with Brain = V11, over 1,000 watched matches, the median outfield player
    /// has 5 s or less of off-target time per match. R2's rule is "a man never sits over 25 m from
    /// his phase target for more than 5 s unless he is chasing or marking", so off-target time is
    /// the part of each unbroken spell further than <see cref="MatchBalance.V11OffTargetDm"/>
    /// (25 m) from the target, with no job, beyond its first
    /// <see cref="MatchBalance.V11OffTargetGraceMs"/> (5 s) — see MatchSimulator.OffTargetTicks.
    /// The raw time far from the target (no grace: mostly the sprint back after a lost chase) is
    /// printed alongside. Keepers are left out: the target is an outfield player's.
    ///
    /// Statistically sensitive: the threshold is a median over 20,000 player-matches, so it moves
    /// with any change to the V11 brain. Run with --logger "console;verbosity=detailed" for the
    /// distribution. Matches run in parallel, each on its own simulator and seed.
    /// </summary>
    [TestFixture]
    [Category("Harness")]
    public class V11PositioningHarnessTests
    {
        private const int Matches = 1000;
        private const ulong FirstSeed = 33_000;
        private const double MaxMedianSeconds = 5.0;

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        [Test]
        public void Harness_V11_MedianOffTargetSecondsPerPlayer_IsAtMost5()
        {
            var cfg = new BalanceConfig().Match;
            cfg.Brain = MatchBrainVersion.V11;
            double tickSeconds = 1.0 / cfg.TicksPerSecond;

            var perPlayer = new double[Matches][];
            var farPerPlayer = new double[Matches][];
            var runs = new int[Matches];
            var overlapSeconds = new double[Matches];

            Parallel.For(0, Matches, i =>
            {
                Club home = _league.Clubs[(2 * i) % _league.Clubs.Count];
                Club away = _league.Clubs[(2 * i + 1) % _league.Clubs.Count];
                Lineup homeXi = LineupSelector.BestEleven(home);
                Lineup awayXi = LineupSelector.BestEleven(away);
                var sim = new MatchSimulator(cfg);
                sim.Generate(homeXi, awayXi, new MatchReport { HomeClubId = home.Id, AwayClubId = away.Id },
                    new Pcg32(FirstSeed + (ulong)i), null);

                var outfield = Enumerable.Range(0, 2)
                    .SelectMany(side => Enumerable.Range(0, homeXi.Slots.Count)
                        .Where(slot => (side == 0 ? homeXi : awayXi).Slots[slot].Role != PositionRole.Goalkeeper)
                        .Select(slot => (side, slot)))
                    .ToArray();
                perPlayer[i] = outfield.Select(p => sim.OffTargetTicks(p.side, p.slot) * tickSeconds).ToArray();
                farPerPlayer[i] = outfield.Select(p => sim.FarFromTargetTicks(p.side, p.slot) * tickSeconds).ToArray();
                runs[i] = sim.RunsInBehind(0) + sim.RunsInBehind(1);
                overlapSeconds[i] = (sim.OverlapTicks(0) + sim.OverlapTicks(1)) * tickSeconds;
            });

            double[] all = perPlayer.SelectMany(p => p).OrderBy(v => v).ToArray();
            double[] far = farPerPlayer.SelectMany(p => p).OrderBy(v => v).ToArray();
            double median = Percentile(all, 50);

            TestContext.Out.WriteLine(
                $"V11 seconds per outfield player per match > {cfg.V11OffTargetDm / 10} m from the phase target, no job, {all.Length} player-matches:");
            TestContext.Out.WriteLine(
                $"  off target (past {cfg.V11OffTargetGraceMs / 1000} s of a spell): median {median:F1}  mean {all.Average():F1}  p75 {Percentile(all, 75):F1}  p90 {Percentile(all, 90):F1}  max {all.Max():F1}");
            TestContext.Out.WriteLine(
                $"  far, no grace:                 median {Percentile(far, 50):F1}  mean {far.Average():F1}  p75 {Percentile(far, 75):F1}  p90 {Percentile(far, 90):F1}  max {far.Max():F1}");
            TestContext.Out.WriteLine(
                $"  runs in behind per match {runs.Average():F1}  overlap seconds per match {overlapSeconds.Average():F1}");

            Assert.That(median, Is.LessThanOrEqualTo(MaxMedianSeconds));
        }

        private static double Percentile(double[] sorted, int percent) =>
            sorted[Math.Min(sorted.Length - 1, sorted.Length * percent / 100)];
    }
}
