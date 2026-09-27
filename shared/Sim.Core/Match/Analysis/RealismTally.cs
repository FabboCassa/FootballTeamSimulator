using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>One line of the realism report: a reading, the band it is judged against, and whether it is inside.</summary>
    public readonly struct RealismRow
    {
        public RealismBand Band { get; }
        public double Value { get; }
        public bool InBand => Band.Contains(Value);

        public RealismRow(RealismBand band, double value)
        {
            Band = band;
            Value = value;
        }
    }

    /// <summary>
    /// Adds up the realism readings of many matches and prints them against
    /// <see cref="RealismBands"/>. A REPORT, not a gate: nothing here fails a run.
    ///
    /// The time per match is handed in by the caller (the harness owns the clock), so this class
    /// stays pure like the rest of Sim.Core.
    /// </summary>
    public sealed class RealismTally
    {
        private long _goals, _shots, _onTarget, _corners, _fouls, _boxEntries;
        private long _chances, _chanceShots, _possessions, _sterile;
        private double _totalMs;
        private readonly List<double> _offTargetMedians = new List<double>();

        public int Matches { get; private set; }

        public double MsPerMatch => Matches == 0 ? 0 : _totalMs / Matches;

        public void Add(MatchMetrics match, RealismMetrics realism, double simulateMs)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (realism == null) throw new ArgumentNullException(nameof(realism));

            Matches++;
            _goals += match.TotalGoals;
            _shots += match.TotalShots;
            _onTarget += match.Home.ShotsOnTarget + match.Away.ShotsOnTarget;
            _corners += match.TotalCorners;
            _fouls += match.TotalFouls;
            _boxEntries += realism.HomeBoxEntries + realism.AwayBoxEntries;
            _chances += realism.OpenGoalChances;
            _chanceShots += realism.OpenGoalShots;
            _possessions += realism.Possessions;
            _sterile += realism.SterilePossessions;
            _offTargetMedians.Add(realism.MedianOffTargetSeconds);
            _totalMs += simulateMs;
        }

        /// <summary>Every reading against its band. <paramref name="baselineMsPerMatch"/> is V10's time in the same run.</summary>
        public IReadOnlyList<RealismRow> Rows(double baselineMsPerMatch)
        {
            double m = Matches == 0 ? 1 : Matches;
            return new[]
            {
                new RealismRow(RealismBands.Goals, _goals / m),
                new RealismRow(RealismBands.Shots, _shots / m),
                new RealismRow(RealismBands.OnTargetPercent, _shots == 0 ? 0 : 100.0 * _onTarget / _shots),
                new RealismRow(RealismBands.Corners, _corners / m),
                new RealismRow(RealismBands.Fouls, _fouls / m),
                new RealismRow(RealismBands.BoxEntriesPerSide, _boxEntries / (2 * m)),
                new RealismRow(RealismBands.OpenGoalShotRate, _chances == 0 ? 1.0 : (double)_chanceShots / _chances),
                new RealismRow(RealismBands.SterilePossessionShare, _possessions == 0 ? 0 : (double)_sterile / _possessions),
                new RealismRow(RealismBands.MedianOffTargetSeconds, Median(_offTargetMedians)),
                new RealismRow(RealismBands.TimeVsV10, baselineMsPerMatch <= 0 ? 0 : MsPerMatch / baselineMsPerMatch)
            };
        }

        /// <summary>The printable block: one line per band, marked IN or OUT, plus the raw counts behind the rates.</summary>
        public string Format(string label, double baselineMsPerMatch)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== realism: {label} ({Matches} matches, {MsPerMatch.ToString("F1", inv)} ms/match) ===");

            int inside = 0;
            IReadOnlyList<RealismRow> rows = Rows(baselineMsPerMatch);
            foreach (RealismRow row in rows)
            {
                if (row.InBand) inside++;
                sb.AppendLine(string.Format(inv, "  {0,-30} {1,9:F3}   band {2,-10} {3}",
                    row.Band.Name, row.Value, row.Band.Describe(), row.InBand ? "IN" : "OUT"));
            }

            sb.AppendLine(string.Format(inv,
                "  open-goal chances {0} (shot {1}) | possessions {2} (sterile {3}) | inside {4}/{5} bands",
                _chances, _chanceShots, _possessions, _sterile, inside, rows.Count));
            return sb.ToString();
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = values.ToArray();
            Array.Sort(sorted);
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }
    }
}
