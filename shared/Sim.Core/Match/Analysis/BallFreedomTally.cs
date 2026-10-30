using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Pools the <see cref="BallFreedomMetrics"/> of many matches and prints each reading next to its
    /// R1 tracking band of <see cref="RealismReference.Tracking"/>. Like the reference (doc §10.4,
    /// a split is the mean of its matches), every reading is the mean of the per-match values; the
    /// pass rows over the matches with at least one pass. REPORT ONLY: nothing here fails a run.
    /// </summary>
    public sealed class BallFreedomTally
    {
        public const string PassesName = "  tracking-derived passes/match";
        public const string LooseDuelsName = "  ... loose balls after a duel/match";
        public const string LooseDeflectionsName = "  ... loose balls off the other team/match";

        private int _matches, _passMatches;
        private double _free15, _free3, _within5, _within10, _above1m, _loose, _duels, _deflections, _passes;
        private double _flight50, _flight90, _receiver50, _intoSpace;

        public int Matches => _matches;

        public void Add(BallFreedomMetrics match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            _matches++;
            _free15 += match.Free15Percent;
            _free3 += match.Free3Percent;
            _within5 += match.Within5;
            _within10 += match.Within10;
            _above1m += match.Above1mPercent;
            _loose += match.LooseBalls;
            _duels += match.LooseDuels;
            _deflections += match.LooseDeflections;
            _passes += match.Passes;
            if (match.Passes == 0) return;

            _passMatches++;
            _flight50 += match.FlightP50Seconds;
            _flight90 += match.FlightP90Seconds;
            _receiver50 += match.ReceiverToArrivalP50Meters;
            _intoSpace += match.IntoSpacePercent;
        }

        /// <summary>Every reading against its band, in doc §10.5 table order, with the loose-ball split and the pass count.</summary>
        public IReadOnlyList<ShotPassRow> Rows()
        {
            double n = _matches == 0 ? 1 : _matches, p = _passMatches == 0 ? 1 : _passMatches;
            return new[]
            {
                new ShotPassRow(RealismReference.Tracking.BallFree15Percent, _free15 / n),
                new ShotPassRow(RealismReference.Tracking.BallFree3Percent, _free3 / n),
                new ShotPassRow(RealismReference.Tracking.PlayersWithin5m, _within5 / n),
                new ShotPassRow(RealismReference.Tracking.PlayersWithin10m, _within10 / n),
                new ShotPassRow(RealismReference.Tracking.BallAbove1mPercent, _above1m / n),
                new ShotPassRow(RealismReference.Tracking.PassFlightP50Seconds, _flight50 / p),
                new ShotPassRow(RealismReference.Tracking.PassFlightP90Seconds, _flight90 / p),
                new ShotPassRow(RealismReference.Tracking.ReceiverToArrivalP50Meters, _receiver50 / p),
                new ShotPassRow(RealismReference.Tracking.PassIntoSpacePercent, _intoSpace / p),
                new ShotPassRow(PassesName, _passes / n),
                new ShotPassRow(RealismReference.Tracking.LooseBallsPerMatch, _loose / n),
                new ShotPassRow(LooseDuelsName, _duels / n),
                new ShotPassRow(LooseDeflectionsName, _deflections / n),
            };
        }

        /// <summary>The printable block: one line per reading with its band and IN/OUT.</summary>
        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== ball freedom, passes, loose balls R2 (report only): {label} ({Matches} matches) ===");
            sb.AppendLine("  ball in play; holder = nearest player within 1.5 m (doc §10.3)");
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
