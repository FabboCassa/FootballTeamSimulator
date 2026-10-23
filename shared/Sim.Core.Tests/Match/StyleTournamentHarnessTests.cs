using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sim.Core.Match.Analysis;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The R20 style tournament and the R17 formation sweep (real-match spec, issue #95), played on
    /// <see cref="TacticHarnessTests.PlayTournament"/> and read on the mean of three seed sets
    /// (<see cref="TournamentMean"/>), like the R14 tactic tournament.
    /// R20: every style in its first suggested formation against every other; no style takes more
    /// than 55% of its points, and each takes under 45% against at least one opponent.
    /// R17: every catalogue formation on Balanced against 4-4-2 Balanced; none takes more than 60%.
    /// Statistically fragile: at the full sample (#95) 3-5-1-1 read 59.6%, inside one standard error
    /// (~1.7 pp over 600 matches) of the gate. The user judges the printed tables.
    ///
    /// Explicit: ~105 pairings x 200 x 3 watched matches. Run with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release
    ///     --filter "FullyQualifiedName~StyleTournamentHarnessTests" --logger "console;verbosity=detailed"
    /// Screening, which only reads: append
    ///   -- TestRunParameters.Parameter(name=\"stylePerPairing\", value=\"100\")
    /// (or "sweepPerPairing").
    /// </summary>
    [TestFixture]
    public class StyleTournamentHarnessTests
    {
        private const string Reason = "Harness: thousands of watched matches per seed set.";
        private const int MatchesPerPairing = 200;
        private const double MaxStylePointsShare = 0.55;
        private const double MaxWorstMatchupShare = 0.45;
        private const double MaxFormationPointsShare = 0.60;
        private const Formation Reference = Formation.F442;

        // Disjoint at the full sample: 105 x 200 = 21,000 style seeds and 26 x 200 = 5,200 sweep seeds a set.
        private static readonly ulong[] StyleSeedSets = { 100_000, 200_000, 300_000 };
        private static readonly ulong[] SweepSeedSets = { 400_000, 410_000, 420_000 };

        [Test, Explicit(Reason), Category("TacticHarness")]
        public void Harness_StyleTournament()
        {
            IReadOnlyList<StyleDefinition> styles = PlayingStyles.All;
            List<string> names = styles.Select(s => $"{s.Name} ({Formations.Name(s.SuggestedFormations[0])})").ToList();
            List<Tactic> tactics = styles.Select(s => new Tactic(s.SuggestedFormations[0], s.Instructions)).ToList();
            int perPairing = TestContext.Parameters.Get("stylePerPairing", MatchesPerPairing);
            var clock = Stopwatch.StartNew();

            TournamentMean mean = Play(tactics, TournamentTable.Pairings(tactics.Count), perPairing, StyleSeedSets);
            TestContext.Out.WriteLine(mean.Format($"R20 styles, {perPairing}/pairing, {clock.Elapsed.TotalSeconds:F0} s",
                names, MaxStylePointsShare, MaxWorstMatchupShare));
            TestContext.Out.WriteLine(Matrix(mean, styles.Select(s => s.Name).ToList()));

            IReadOnlyList<TournamentBreach> breaches = mean.Breaches(MaxStylePointsShare, MaxWorstMatchupShare);
            TestContext.Out.WriteLine(Describe(breaches, names));

            // R20 is the gate at the full sample; screening only reads.
            if (perPairing < MatchesPerPairing) return;
            Assert.That(breaches.Select(b => Describe(b, names)), Is.Empty);
        }

        [Test, Explicit(Reason), Category("TacticHarness")]
        public void Harness_FormationSweep()
        {
            List<Formation> formations = new[] { Reference }.Concat(Formations.Catalogue.Where(f => f != Reference)).ToList();
            List<string> names = formations.Select(Formations.Name).ToList();
            List<Tactic> tactics = formations.Select(f => new Tactic(f)).ToList();
            List<int> challengers = Enumerable.Range(1, formations.Count - 1).ToList();
            int perPairing = TestContext.Parameters.Get("sweepPerPairing", MatchesPerPairing);
            var clock = Stopwatch.StartNew();

            TournamentMean mean = Play(tactics, challengers.Select(c => (0, c)).ToList(), perPairing, SweepSeedSets);
            TestContext.Out.WriteLine(Sweep(mean, names, $"{perPairing}/pairing, {clock.Elapsed.TotalSeconds:F0} s"));

            IReadOnlyList<TournamentBreach> breaches = mean.PointsBreaches(MaxFormationPointsShare, challengers);
            TestContext.Out.WriteLine(Describe(breaches, names));

            if (perPairing < MatchesPerPairing) return;
            Assert.That(breaches.Select(b => Describe(b, names)), Is.Empty);
        }

        private static TournamentMean Play(IReadOnlyList<Tactic> tactics, IReadOnlyList<(int A, int B)> pairs,
            int perPairing, IEnumerable<ulong> seedSets)
        {
            var tables = new List<TournamentTable>();
            foreach (ulong seed in seedSets)
            {
                TournamentTable table = TacticHarnessTests.PlayTournament(tactics, pairs, perPairing, seed);
                foreach ((int a, int b) in pairs)
                    Assert.That(table.GamesAgainst(a, b), Is.EqualTo(perPairing), $"pairing {a}-{b}, seed set {seed}");
                tables.Add(table);
            }

            return new TournamentMean(tables);
        }

        /// <summary>The style x style matrix: each row's mean share of the points against each column.</summary>
        private static string Matrix(TournamentMean mean, IReadOnlyList<string> names)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("=== R20 points share of the row vs the column (mean of seed sets, %) ===");
            sb.Append(string.Format(inv, "  {0,-21}", ""));
            for (int c = 0; c < names.Count; c++) sb.Append(string.Format(inv, " {0,5}", c));
            sb.AppendLine("  total");
            for (int r = 0; r < names.Count; r++)
            {
                sb.Append(string.Format(inv, "  {0,2} {1,-18}", r, names[r]));
                for (int c = 0; c < names.Count; c++)
                    sb.Append(r == c ? "     -" : string.Format(inv, " {0,5:F1}", 100 * mean.ShareAgainst(r, c)));
                sb.AppendLine(string.Format(inv, "  {0,5:F1}", 100 * mean.PointsShare(r)));
            }

            return sb.ToString();
        }

        /// <summary>The R17 sweep: each challenger's mean share of the points against the reference.</summary>
        private static string Sweep(TournamentMean mean, IReadOnlyList<string> names, string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== R17 formation sweep vs {names[0]} Balanced, mean of {mean.SeedSets} seed sets, {label} " +
                          $"(gate: none > {100 * MaxFormationPointsShare:F0}%) ===");
            for (int f = 1; f < names.Count; f++)
                sb.AppendLine(string.Format(inv, "  {0,-16} points share {1,5:F1}%  {2}", names[f], 100 * mean.PointsShare(f),
                    mean.PointsShare(f) > MaxFormationPointsShare ? "OUT" : "IN"));
            sb.AppendLine(string.Format(inv, "  {0} itself takes {1:F1}% over the sweep", names[0], 100 * mean.PointsShare(0)));
            return sb.ToString();
        }

        private static string Describe(IReadOnlyList<TournamentBreach> breaches, IReadOnlyList<string> names) =>
            breaches.Count == 0
                ? "  breaches: none"
                : "  breaches:\n" + string.Join("\n", breaches.Select(b => "    " + Describe(b, names)));

        private static string Describe(TournamentBreach b, IReadOnlyList<string> names)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return b.Check == TournamentCheck.PointsShare
                ? string.Format(inv, "{0}: points share {1:F1}% > {2:F0}%", names[b.Preset], 100 * b.Value, 100 * b.Limit)
                : string.Format(inv, "{0}: takes {1:F1}% even from its worst opponent {2} (must be < {3:F0}%)",
                    names[b.Preset], 100 * b.Value, names[b.Opponent], 100 * b.Limit);
        }
    }
}
