using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>One line of the shape-and-movement report. <see cref="InBand"/> is null for a reading printed without a target.</summary>
    public readonly struct ShapeMovementRow
    {
        public string Name { get; }
        public double Value { get; }
        public RealismBand? Band { get; }
        public string Source { get; }
        public bool? InBand => Band?.Contains(Value);

        public ShapeMovementRow(string name, double value, RealismBand? band, string source)
        {
            Name = name;
            Value = value;
            Band = band;
            Source = source;
        }
    }

    /// <summary>
    /// Pools the <see cref="ShapeMovementMetrics"/> of many matches and prints them against
    /// <see cref="ShapeMovementTargets"/> (spec R4, R5, R6, R8) and, for distance, the R2 band of
    /// <see cref="RealismReference"/>. A REPORT, not a gate: nothing here fails a run.
    /// </summary>
    public sealed class ShapeMovementTally
    {
        private const string Reference = "R2 RealismReference";

        private readonly ShapeMovementMetrics _sum = new ShapeMovementMetrics();
        private readonly List<double> _offTargetMedians = new List<double>();

        public int Matches { get; private set; }

        public void Add(ShapeMovementMetrics m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));

            Matches++;
            _sum.OpenPlayFrames += m.OpenPlayFrames;
            _sum.DefendingSamples += m.DefendingSamples;
            _sum.DefendingLengthDm += m.DefendingLengthDm;
            _sum.DefendingWidthDm += m.DefendingWidthDm;
            _sum.OwnHalfDefendingSamples += m.OwnHalfDefendingSamples;
            _sum.OwnHalfLengthDm += m.OwnHalfLengthDm;
            _sum.AttackingSamples += m.AttackingSamples;
            _sum.AttackingWidthDm += m.AttackingWidthDm;
            _sum.BothTouchlinesSamples += m.BothTouchlinesSamples;
            _sum.OutfieldOpenPlayFrames += m.OutfieldOpenPlayFrames;
            _sum.StandStillFrames += m.StandStillFrames;
            _sum.HomeOutfieldDistanceDm += m.HomeOutfieldDistanceDm;
            _sum.AwayOutfieldDistanceDm += m.AwayOutfieldDistanceDm;
            _sum.OffPitchFrames += m.OffPitchFrames;
            _sum.KeeperOpenPlayFrames += m.KeeperOpenPlayFrames;
            _sum.KeeperDepthBreakFrames += m.KeeperDepthBreakFrames;
            _offTargetMedians.Add(m.MedianOffTargetSeconds);
        }

        /// <summary>Every reading against its target, in spec order.</summary>
        public IReadOnlyList<ShapeMovementRow> Rows()
        {
            int teamMatches = Matches == 0 ? 1 : 2 * Matches;
            double distanceKm = (_sum.DistancePerOutfieldPlayerKm(true) + _sum.DistancePerOutfieldPlayerKm(false)) / teamMatches;

            return new[]
            {
                Row(ShapeMovementTargets.OwnHalfBlockLengthM, _sum.OwnHalfBlockLengthM, ShapeMovementTargets.R4),
                Row(ShapeMovementTargets.BlockLengthM, _sum.BlockLengthM, ShapeMovementTargets.R4),
                Row(ShapeMovementTargets.BlockWidthM, _sum.BlockWidthM, ShapeMovementTargets.R4),
                Row(ShapeMovementTargets.AttackingWidthM, _sum.AttackingWidthM, ShapeMovementTargets.R4),
                new ShapeMovementRow(ShapeMovementTargets.BothTouchlinesName, _sum.BothTouchlinesPercent, null, ShapeMovementTargets.R4),
                Row(ShapeMovementTargets.MedianOffTargetSeconds, Median(_offTargetMedians), ShapeMovementTargets.R4),
                Row(ShapeMovementTargets.StandStillPercent, _sum.StandStillPercent, ShapeMovementTargets.R8),
                Row(RealismReference.DistancePerOutfieldPlayerKm, distanceKm, Reference),
                Row(ShapeMovementTargets.OffPitchFrames, _sum.OffPitchFrames, ShapeMovementTargets.R5),
                Row(ShapeMovementTargets.KeeperDepthBreakPercent, _sum.KeeperDepthBreakPercent, ShapeMovementTargets.R6),
            };
        }

        /// <summary>The printable block: one line per reading, marked IN, OUT or report only, with where its target comes from.</summary>
        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== shape and movement: {label} ({Matches} matches) ===");

            foreach (ShapeMovementRow row in Rows())
            {
                string target = row.Band?.Describe() ?? "-";
                string verdict = row.InBand == null ? "report only" : row.InBand.Value ? "IN" : "OUT";
                sb.AppendLine(string.Format(inv, "  {0,-34} {1,10:F3}   target {2,-10} {3,-11} [{4}]",
                    row.Name, row.Value, target, verdict, row.Source));
            }

            sb.AppendLine(string.Format(inv,
                "  open-play frames {0} | defending samples {1} (own half {2}) | attacking samples {3}",
                _sum.OpenPlayFrames, _sum.DefendingSamples, _sum.OwnHalfDefendingSamples, _sum.AttackingSamples));
            return sb.ToString();
        }

        private static ShapeMovementRow Row(RealismBand band, double value, string source) =>
            new ShapeMovementRow(band.Name, value, band, source);

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            double[] sorted = values.ToArray();
            Array.Sort(sorted);
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }
    }
}
