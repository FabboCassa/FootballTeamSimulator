using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Pools the <see cref="ChanceDecisionMetrics"/> of many matches and prints the chance-decision
    /// table: per match, how many carriers each bucket saw and what they did. REPORT ONLY: no row
    /// carries a band until the decision and balance tasks of the believable-match spec gate it.
    /// </summary>
    public sealed class ChanceDecisionTally
    {
        private static readonly string[] ActionNames = { "shot", "fwd/square", "back", "carry", "clear", "lost" };

        private ChanceDecisionMetrics? _sum;

        public int Matches => _sum?.Matches ?? 0;

        public void Add(ChanceDecisionMetrics match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            _sum = _sum == null ? match.Plus(new ChanceDecisionMetrics { Matches = 0 }) : _sum.Plus(match);
        }

        /// <summary>The row's name: a bucket's chances per match when <paramref name="action"/> is null, else that action's.</summary>
        public static string RowName(bool free, bool near, ChanceAction? action) =>
            $"{Bucket(free, near)} {(action == null ? "chances" : ActionNames[(int)action.Value])}/match";

        /// <summary>Per bucket: its chances per match, then each action per match. None carries a band.</summary>
        public IReadOnlyList<ShotPassRow> Rows()
        {
            ChanceDecisionMetrics t = _sum ?? new ChanceDecisionMetrics { Matches = 0 };
            double matches = t.Matches == 0 ? 1 : t.Matches;
            var rows = new List<ShotPassRow>();
            foreach ((bool free, bool near) in Buckets())
            {
                rows.Add(new ShotPassRow(RowName(free, near, null), t.Chances(free, near) / matches));
                for (int a = 0; a < ChanceDecisionMetrics.ActionCount; a++)
                    rows.Add(new ShotPassRow(RowName(free, near, (ChanceAction)a), t.Count(free, near, (ChanceAction)a) / matches));
            }

            return rows;
        }

        /// <summary>The printable table: one line per bucket, each action per match with its share of the bucket.</summary>
        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            ChanceDecisionMetrics t = _sum ?? new ChanceDecisionMetrics { Matches = 0 };
            double matches = t.Matches == 0 ? 1 : t.Matches;

            var sb = new StringBuilder();
            sb.AppendLine($"=== chance decisions (report only): {label} ({Matches} matches) ===");
            sb.AppendLine("  carrier <=16 m from goal centre, |y-34| <=10 m; free = no outfield defender within 3 m; per match (share)");
            sb.Append(string.Format(inv, "  {0,-16} {1,7}", "bucket", "chances"));
            foreach (string name in ActionNames) sb.Append(string.Format(inv, " {0,15}", name));
            sb.AppendLine();

            foreach ((bool free, bool near) in Buckets())
            {
                int chances = t.Chances(free, near);
                sb.Append(string.Format(inv, "  {0,-16} {1,7:F2}", Bucket(free, near), chances / matches));
                for (int a = 0; a < ChanceDecisionMetrics.ActionCount; a++)
                {
                    int count = t.Count(free, near, (ChanceAction)a);
                    double share = chances == 0 ? 0 : 100.0 * count / chances;
                    sb.Append(string.Format(inv, " {0,7:F2} ({1,4:F0}%)", count / matches, share));
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Bucket(bool free, bool near) => $"{(free ? "free" : "pressed")} {(near ? "<=12 m" : "12-16 m")}";

        private static IEnumerable<(bool Free, bool Near)> Buckets()
        {
            yield return (true, true);
            yield return (true, false);
            yield return (false, true);
            yield return (false, false);
        }
    }
}
