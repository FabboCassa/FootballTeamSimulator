using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Pools the <see cref="AerialPassMetrics"/> of many matches against the R1 event bands of
    /// <see cref="RealismReference.Events"/>: shares are the mean of the per-match shares and counts
    /// per team-match, as the reference reads its splits (doc §11.4). REPORT ONLY.
    /// </summary>
    public sealed class AerialPassTally
    {
        private int _matches;
        private double _passes, _long, _high, _aerial;

        public int Matches => _matches;

        public void Add(AerialPassMetrics match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            _matches++;
            _passes += match.Passes;
            _long += match.LongPercent;
            _high += match.HighPercent;
            _aerial += match.AerialDuels;
        }

        public IReadOnlyList<ShotPassRow> Rows()
        {
            double n = _matches == 0 ? 1 : _matches;
            return new[]
            {
                new ShotPassRow(RealismReference.Events.PassesPerTeamMatch, _passes / (2 * n)),
                new ShotPassRow(RealismReference.Events.LongPassPercent, _long / n),
                new ShotPassRow(RealismReference.Events.HighPassPercent, _high / n),
                // Every duel has a man of each side: a team's won plus lost is the match's count.
                new ShotPassRow(RealismReference.Events.AerialDuelsPerTeamMatch, _aerial / n),
            };
        }

        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== long, high passes and aerial duels R4 (report only): {label} ({Matches} matches) ===");
            sb.AppendLine("  pass = Pass/LongBall/Cross action, restarts included; long > 30 m; high = peak >= 2 m (doc §11.3)");
            foreach (ShotPassRow row in Rows())
            {
                string band = row.Band?.Describe() ?? "-";
                string verdict = row.InBand == null ? "" : (row.InBand.Value ? "IN" : "OUT");
                sb.AppendLine(string.Format(inv, "  {0,-46} {1,9:F3}   band {2,-12} {3}", row.Name, row.Value, band, verdict));
            }

            return sb.ToString();
        }
    }
}
