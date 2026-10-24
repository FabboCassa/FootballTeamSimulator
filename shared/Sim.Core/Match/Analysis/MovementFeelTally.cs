using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Pools the <see cref="MovementFeelMetrics"/> of many matches and prints them against the R1
    /// movement bands of <see cref="RealismReference.Movement"/>. REPORT ONLY: every row says IN or
    /// OUT but nothing fails a run until the balance task of the believable-match spec gates them.
    /// </summary>
    public sealed class MovementFeelTally
    {
        private MovementFeelMetrics? _sum;

        public int Matches => _sum?.Matches ?? 0;

        public void Add(MovementFeelMetrics match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            _sum = _sum == null ? match.Plus(new MovementFeelMetrics { Matches = 0 }) : _sum.Plus(match);
        }

        /// <summary>Every reading against its band, in doc §9.4 table order (the ball-speed rows aside).</summary>
        public IReadOnlyList<RealismRow> Rows()
        {
            MovementFeelMetrics t = _sum ?? new MovementFeelMetrics { Matches = 0 };
            return new[]
            {
                new RealismRow(RealismReference.Movement.AgreementMedian, t.AgreementMedian),
                new RealismRow(RealismReference.Movement.AgreementAbove09Percent, t.AgreementAbove09Percent),
                new RealismRow(RealismReference.Movement.ReversalsPerPlayerMinute, t.ReversalsPerPlayerMinute),
                new RealismRow(RealismReference.Movement.SpeedBelow02Percent, t.SpeedPercent(0)),
                new RealismRow(RealismReference.Movement.Speed02To2Percent, t.SpeedPercent(1)),
                new RealismRow(RealismReference.Movement.Speed2To4Percent, t.SpeedPercent(2)),
                new RealismRow(RealismReference.Movement.Speed4To55Percent, t.SpeedPercent(3)),
                new RealismRow(RealismReference.Movement.Speed55To7Percent, t.SpeedPercent(4)),
                new RealismRow(RealismReference.Movement.SpeedAbove7Percent, t.SpeedPercent(5)),
                new RealismRow(RealismReference.Movement.AccelerationsPerPlayerMinute, t.AccelerationsPerPlayerMinute),
            };
        }

        /// <summary>The printable block: one line per reading, marked IN or OUT, none gated.</summary>
        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            MovementFeelMetrics t = _sum ?? new MovementFeelMetrics { Matches = 0 };
            var sb = new StringBuilder();
            sb.AppendLine($"=== movement feel R1 (report only): {label} ({Matches} matches) ===");
            sb.AppendLine("  open play, outfielders; speed and acceleration over +-1 frame, R and reversals over 1 s steps");
            foreach (RealismRow row in Rows())
                sb.AppendLine(string.Format(inv, "  {0,-40} {1,9:F3}   band {2:0.###}-{3,-8:0.###} {4}",
                    row.Band.Name, row.Value, row.Band.Min, row.Band.Max, row.InBand ? "IN" : "OUT"));

            sb.AppendLine(string.Format(inv, "  R windows {0} | player-minutes {1:F0} | reversals {2} | accelerations {3}",
                t.AgreementWindows, t.PlayerMinutes, t.Reversals, t.Accelerations));
            return sb.ToString();
        }
    }
}
