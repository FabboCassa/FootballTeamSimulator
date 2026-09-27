using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>One preset's take against one opponent: the share of the points on offer it won.</summary>
    public readonly struct TournamentMatchup
    {
        public int Preset { get; }
        public int Opponent { get; }
        public double Share { get; }

        public TournamentMatchup(int preset, int opponent, double share)
        {
            Preset = preset;
            Opponent = opponent;
            Share = share;
        }
    }

    /// <summary>
    /// The table of the R8 tactic tournament (watchable-match spec): every preset against every
    /// other, 3 points a win and 1 a draw, read as the share of the points on offer. A REPORT, not a
    /// gate: R8 reads no preset above 55% and each preset under 45% against at least one opponent,
    /// and the user judges the output. Pure bookkeeping — the harness plays the matches.
    /// </summary>
    public sealed class TournamentTable
    {
        private const int WinPoints = 3;
        private const int DrawPoints = 1;

        private readonly int[,] _points;
        private readonly int[,] _games;

        public int Presets { get; }

        public TournamentTable(int presets)
        {
            if (presets < 0) throw new ArgumentOutOfRangeException(nameof(presets));
            Presets = presets;
            _points = new int[presets, presets];
            _games = new int[presets, presets];
        }

        /// <summary>The round-robin schedule: every unordered pair once, (0,1), (0,2) … (n-2,n-1).</summary>
        public static IReadOnlyList<(int A, int B)> Pairings(int presets)
        {
            var pairs = new List<(int, int)>();
            for (int a = 0; a < presets; a++)
                for (int b = a + 1; b < presets; b++)
                    pairs.Add((a, b));
            return pairs;
        }

        /// <summary>One match between <paramref name="a"/> and <paramref name="b"/>, whatever the venue.</summary>
        public void Add(int a, int b, int aGoals, int bGoals)
        {
            if (a == b) throw new ArgumentException("A preset does not play itself.", nameof(b));
            _games[a, b]++;
            _games[b, a]++;
            _points[a, b] += PointsFor(aGoals, bGoals);
            _points[b, a] += PointsFor(bGoals, aGoals);
        }

        public int Points(int preset) => SumRow(_points, preset);

        public int Games(int preset) => SumRow(_games, preset);

        /// <summary>Points won over the points on offer (0 when it has not played).</summary>
        public double PointsShare(int preset) => Share(Points(preset), Games(preset));

        public double ShareAgainst(int preset, int opponent) => Share(_points[preset, opponent], _games[preset, opponent]);

        /// <summary>The opponent the preset takes least from (ties to the lower index), or -1 when it has not played.</summary>
        public int WorstOpponent(int preset)
        {
            int worst = -1;
            for (int o = 0; o < Presets; o++)
            {
                if (o == preset || _games[preset, o] == 0) continue;
                if (worst < 0 || ShareAgainst(preset, o) < ShareAgainst(preset, worst)) worst = o;
            }

            return worst;
        }

        /// <summary>The lowest share any preset takes against any opponent (the most lopsided pairing).</summary>
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

        /// <summary>The printable table: one line per preset, then the worst matchup.</summary>
        public string Format(string label, IReadOnlyList<string> names)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== tactic tournament: {label} ({Presets} presets, R8 reads max 55%, each < 45% vs someone) ===");

            for (int p = 0; p < Presets; p++)
            {
                int o = WorstOpponent(p);
                string worst = o < 0 ? "-" : $"{names[o]} {Pct(ShareAgainst(p, o), inv)}";
                sb.AppendLine(string.Format(inv, "  {0,-24} points share {1,6}   games {2,5}   worst opponent {3}",
                    names[p], Pct(PointsShare(p), inv), Games(p), worst));
            }

            TournamentMatchup m = WorstMatchup();
            if (m.Preset >= 0)
                sb.AppendLine($"  worst matchup: {names[m.Preset]} takes {Pct(m.Share, inv)} vs {names[m.Opponent]}");
            return sb.ToString();
        }

        private static int PointsFor(int own, int against) =>
            own > against ? WinPoints : own == against ? DrawPoints : 0;

        private int SumRow(int[,] table, int row)
        {
            int sum = 0;
            for (int c = 0; c < Presets; c++) sum += table[row, c];
            return sum;
        }

        private static double Share(int points, int games) =>
            games == 0 ? 0 : (double)points / (WinPoints * games);

        private static string Pct(double share, CultureInfo inv) => (100 * share).ToString("F1", inv) + "%";
    }
}
