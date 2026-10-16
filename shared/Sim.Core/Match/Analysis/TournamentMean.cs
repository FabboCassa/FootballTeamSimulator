using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The R14 tactic tournament gate (real-match spec): the R8 <see cref="TournamentTable"/> read as
    /// the mean over several seed sets, because one seed set was shown to be coin-flip fragile even
    /// on the base. Every share is the plain mean of the seed sets' shares (each set weighs the
    /// same), over the sets in which the preset or pairing played. Pure bookkeeping.
    /// </summary>
    public sealed class TournamentMean
    {
        private readonly TournamentTable[] _sets;

        public int Presets { get; }

        public int SeedSets => _sets.Length;

        public TournamentMean(IReadOnlyList<TournamentTable> seedSets)
        {
            if (seedSets == null) throw new ArgumentNullException(nameof(seedSets));
            if (seedSets.Count == 0) throw new ArgumentException("At least one seed set.", nameof(seedSets));
            Presets = seedSets[0].Presets;
            if (seedSets.Any(t => t == null || t.Presets != Presets))
                throw new ArgumentException("Every seed set must cover the same presets.", nameof(seedSets));
            _sets = seedSets.ToArray();
        }

        public double PointsShare(int preset) =>
            Mean(_sets.Where(t => t.Games(preset) > 0).Select(t => t.PointsShare(preset)));

        public double ShareAgainst(int preset, int opponent) =>
            Mean(_sets.Where(t => t.GamesAgainst(preset, opponent) > 0).Select(t => t.ShareAgainst(preset, opponent)));

        /// <summary>The opponent the preset takes least from on the mean (ties to the lower index), or -1 when it never played.</summary>
        public int WorstOpponent(int preset)
        {
            int worst = -1;
            double worstShare = double.MaxValue;
            for (int o = 0; o < Presets; o++)
            {
                if (o == preset || !Played(preset, o)) continue;
                double share = ShareAgainst(preset, o);
                if (share < worstShare)
                {
                    worst = o;
                    worstShare = share;
                }
            }

            return worst;
        }

        /// <summary>The lowest mean share any preset takes against any opponent.</summary>
        public TournamentMatchup WorstMatchup()
        {
            var worst = new TournamentMatchup(-1, -1, double.MaxValue);
            for (int p = 0; p < Presets; p++)
            {
                int o = WorstOpponent(p);
                if (o < 0) continue;
                double share = ShareAgainst(p, o);
                if (share < worst.Share) worst = new TournamentMatchup(p, o, share);
            }

            return worst;
        }

        /// <summary>
        /// R14: no preset above <paramref name="maxPointsShare"/> of its points, and every preset that
        /// played under <paramref name="maxWorstMatchupShare"/> against at least one opponent.
        /// </summary>
        public bool MeetsGate(double maxPointsShare, double maxWorstMatchupShare)
        {
            for (int p = 0; p < Presets; p++)
            {
                int o = WorstOpponent(p);
                if (o < 0) continue;
                if (PointsShare(p) > maxPointsShare || ShareAgainst(p, o) >= maxWorstMatchupShare) return false;
            }

            return true;
        }

        /// <summary>The printable table: one line per preset with its mean and per-seed-set shares, the worst matchup, the verdict.</summary>
        public string Format(string label, IReadOnlyList<string> names, double maxPointsShare, double maxWorstMatchupShare)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== tactic tournament, mean of {SeedSets} seed sets: {label} " +
                          $"(R14 gate: max {Pct(maxPointsShare, inv)}, each < {Pct(maxWorstMatchupShare, inv)} vs someone) ===");

            for (int p = 0; p < Presets; p++)
            {
                int o = WorstOpponent(p);
                string worst = o < 0 ? "-" : $"{names[o]} {Pct(ShareAgainst(p, o), inv)}";
                string perSet = string.Join(" / ", _sets.Select(t => Pct(t.PointsShare(p), inv)));
                sb.AppendLine(string.Format(inv, "  {0,-24} points share {1,6}   per seed set {2}   worst opponent {3}",
                    names[p], Pct(PointsShare(p), inv), perSet, worst));
            }

            TournamentMatchup m = WorstMatchup();
            if (m.Preset >= 0)
                sb.AppendLine($"  worst matchup: {names[m.Preset]} takes {Pct(m.Share, inv)} vs {names[m.Opponent]}");
            sb.AppendLine($"  gate: {(MeetsGate(maxPointsShare, maxWorstMatchupShare) ? "PASS" : "FAIL")}");
            return sb.ToString();
        }

        private bool Played(int preset, int opponent) => _sets.Any(t => t.GamesAgainst(preset, opponent) > 0);

        private static double Mean(IEnumerable<double> values)
        {
            double sum = 0;
            int n = 0;
            foreach (double v in values)
            {
                sum += v;
                n++;
            }

            return n == 0 ? 0 : sum / n;
        }

        private static string Pct(double share, CultureInfo inv) => (100 * share).ToString("F1", inv) + "%";
    }
}
