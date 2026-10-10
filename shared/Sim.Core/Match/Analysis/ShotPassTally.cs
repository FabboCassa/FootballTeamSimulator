using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>One printed reading, with the band it is judged against; context-only readings carry none.</summary>
    public readonly struct ShotPassRow
    {
        public string Name { get; }
        public double Value { get; }
        public RealismBand? Band { get; }
        public bool? InBand => Band?.Contains(Value);

        public ShotPassRow(RealismBand band, double value) : this(band.Name, value, band) { }

        public ShotPassRow(string name, double value, RealismBand? band = null)
        {
            Name = name;
            Value = value;
            Band = band;
        }
    }

    /// <summary>
    /// Pools the <see cref="ShotPassMetrics"/> of many matches and prints each reading next to its
    /// <see cref="RealismReference"/> band, or the spec band of <see cref="ShotPassBands"/>. Counts
    /// are per match or per team (÷ 2) as the band says; ratios are pooled (doc §1.5). A REPORT,
    /// not a gate: nothing here fails a run.
    /// </summary>
    public sealed class ShotPassTally
    {
        public const string KeeperToCrossingName = "keeper to crossing point on saves m";
        public const string LongBallShareName = "long-ball share %";
        public const string HighTurnoversName = "high turnovers/match";
        public const string OpenPlayShotsTwoName = "open-play shots after 2 passes %";

        private ShotPassMetrics? _sum;

        public int Matches => _sum?.Matches ?? 0;

        public void Add(ShotPassMetrics match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            _sum = _sum == null ? match.Plus(new ShotPassMetrics { Matches = 0 }) : _sum.Plus(match);
        }

        public IReadOnlyList<ShotPassRow> Rows()
        {
            ShotPassMetrics t = _sum ?? new ShotPassMetrics { Matches = 0 };
            double matches = t.Matches == 0 ? 1 : t.Matches;
            double teams = 2 * matches;
            return new[]
            {
                new ShotPassRow(RealismReference.Shots, t.Shots / matches),
                new ShotPassRow(RealismReference.OnTargetPercent, t.OnTargetPercent),
                new ShotPassRow(RealismReference.BlockedPercent, t.BlockedPercent),
                new ShotPassRow(RealismReference.InsideBoxPercent, t.InsideBoxPercent),
                new ShotPassRow(RealismReference.HeadedShots, t.HeadedShots / matches),
                new ShotPassRow(RealismReference.SaveRatePercent, t.SaveRatePercent),
                new ShotPassRow(RealismReference.SaveRateInsideBoxPercent, t.SaveRateInsideBoxPercent),
                new ShotPassRow(RealismReference.SaveRateOutsideBoxPercent, t.SaveRateOutsideBoxPercent),
                new ShotPassRow(ShotPassBands.KeeperReachPercent, t.KeeperReachPercent),
                new ShotPassRow(KeeperToCrossingName, t.MeanKeeperToCrossingM),
                new ShotPassRow(ShotPassBands.PassesPerTeamAt81Minutes, t.PassesAttempted / teams),
                new ShotPassRow(RealismReference.PassAccuracyPercent, t.PassAccuracyPercent),
                new ShotPassRow(RealismReference.PassesPerSequence, t.PassesPerSequence),
                new ShotPassRow(RealismReference.TenPlusSequencesPerTeam, t.TenPlusSequences / teams),
                new ShotPassRow(RealismReference.DirectSpeed, t.DirectSpeed),
                new ShotPassRow(RealismReference.Ppda, t.Ppda),
                new ShotPassRow(RealismReference.CrossesPerTeam, t.Crosses / teams),
                new ShotPassRow(LongBallShareName, t.LongBallSharePercent),
                new ShotPassRow(HighTurnoversName, t.HighTurnovers / matches),
                new ShotPassRow(RealismReference.BallInPlayMinutes, t.BallInPlayMinutes / matches),
                new ShotPassRow(ShotPassBands.OpenPlayShotsZeroToOnePercent, t.OpenPlayShotsZeroToOnePercent),
                new ShotPassRow(ShotPassBands.OpenPlayShotsZeroToOneExclPercent, t.OpenPlayShotsZeroToOneExclReboundsPercent),
                new ShotPassRow(OpenPlayShotsTwoName, t.OpenPlayShotsTwoPercent),
                new ShotPassRow(ShotPassBands.OpenPlayShotsThreePlusPercent, t.OpenPlayShotsThreePlusPercent),
                new ShotPassRow(ShotPassBands.OpenGoalShortcutPercent, t.OpenGoalShortcutPercent),
            };
        }

        /// <summary>The printable block: one line per reading with its band and IN/OUT, plus the raw counts behind the rates.</summary>
        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== shots, keepers, passing: {label} ({Matches} matches) ===");

            int inside = 0, banded = 0;
            foreach (ShotPassRow row in Rows())
            {
                string band = row.Band?.Describe() ?? "-";
                string verdict = row.InBand == null ? "" : (row.InBand.Value ? "IN" : "OUT");
                if (row.InBand != null) banded++;
                if (row.InBand == true) inside++;
                sb.AppendLine(string.Format(inv, "  {0,-40} {1,9:F3}   band {2,-10} {3}", row.Name, row.Value, band, verdict));
            }

            ShotPassMetrics t = _sum ?? new ShotPassMetrics { Matches = 0 };
            sb.AppendLine(string.Format(inv,
                "  saves {0} (path read {1}, reached {2}) | open-play sequences {3} | open-play shots {4} | inside {5}/{6} bands",
                t.Saves, t.SavesMeasured, t.SavesReached, t.OpenPlaySequences, t.OpenPlayShots, inside, banded));
            return sb.ToString();
        }
    }
}
