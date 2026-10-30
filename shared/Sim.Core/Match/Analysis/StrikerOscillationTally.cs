using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Pools the <see cref="StrikerOffsideMetrics"/> and <see cref="OscillationMetrics"/> of many
    /// matches and prints them against their R1 bands (free-ball spec R2). REPORT ONLY: every row
    /// says IN or OUT but nothing fails a run.
    /// </summary>
    public sealed class StrikerOscillationTally
    {
        private StrikerOffsideMetrics _striker = new StrikerOffsideMetrics { Matches = 0 };
        private readonly OscillationMetrics _oscillation = new OscillationMetrics { Matches = 0 };

        public StrikerOffsideMetrics Striker => _striker;
        public OscillationMetrics Oscillation => _oscillation;

        public void Add(StrikerOffsideMetrics match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            _striker = _striker.Plus(match);
        }

        public void Add(OscillationMetrics match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            _oscillation.Matches += match.Matches;
            foreach (PlayerOscillation p in match.Players) _oscillation.Add(p);
        }

        /// <summary>Every reading against its band: striker offside position, offsides, oscillation.</summary>
        public IReadOnlyList<RealismRow> Rows() => new[]
        {
            new RealismRow(RealismReference.Tracking.StrikerOffsidePercent, _striker.OffsidePercent),
            new RealismRow(RealismReference.PitchEvents.OffsidesPerTeamMatch, _striker.OffsidesPerTeamMatch),
            new RealismRow(RealismReference.Tracking.ReversalsPerPlayerMinute, _oscillation.ReversalsPerPlayerMinute),
        };

        /// <summary>The printable block: one line per reading, marked IN or OUT, none gated, and the per-player spread.</summary>
        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            OscillationMetrics o = _oscillation;
            double bandMax = RealismReference.Tracking.ReversalsPerPlayerMinute.Max;
            var sb = new StringBuilder();
            sb.AppendLine($"=== striker line and oscillation R2 (report only): {label} ({_striker.Matches} matches) ===");
            sb.AppendLine("  open play; striker = the Striker role; oscillation = reversals > 120 deg between consecutive 1 s steps");
            foreach (RealismRow row in Rows())
                sb.AppendLine(string.Format(inv, "  {0,-52} {1,9:F3}   band {2:0.###}-{3,-8:0.###} {4}",
                    row.Band.Name, row.Value, row.Band.Min, row.Band.Max, row.InBand ? "IN" : "OUT"));

            sb.AppendLine(string.Format(inv,
                "  striker frames in possession {0} | offside position {1} | offsides {2}",
                _striker.PossessionFrames, _striker.OffsideFrames, _striker.Offsides));
            sb.AppendLine(string.Format(inv,
                "  oscillation per player (>= {0:0} min, {1} men): p50 {2:F3}  p90 {3:F3}  max {4:F3}  above band {5:F1}%",
                OscillationMetrics.MinSpreadMinutes, o.SpreadPlayers, o.PlayerQuantile(0.5), o.PlayerQuantile(0.9),
                o.PlayerQuantile(1.0), o.PlayersAbovePercent(bandMax)));
            sb.AppendLine(string.Format(inv, "  player-minutes {0:F0} | reversals {1}", o.PlayerMinutes, o.Reversals));
            return sb.ToString();
        }
    }
}
