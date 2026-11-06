using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Pools the <see cref="CentralZoneMetrics"/> of many matches and prints each reading per
    /// team-match next to its doc §11.5 event band. The unpressed share is pooled (unpressed spells
    /// over zone spells), as the reference pools it. REPORT ONLY: nothing here fails a run.
    /// </summary>
    public sealed class CentralZoneTally
    {
        /// <summary>No band: doc §11.5 has no tracking reference for "no defender within 3 m" (StatsBomb's pressure flag is another instrument).</summary>
        public const string UnpressedName = "  central-zone spells, no defender within 3 m %";

        private int _matches;
        private long _box, _zone, _unpressed;

        public int Matches => _matches;

        public double BoxSpellsPerTeamMatch => _matches == 0 ? 0 : _box / (2.0 * _matches);

        public double ZoneSpellsPerTeamMatch => _matches == 0 ? 0 : _zone / (2.0 * _matches);

        public double ZoneUnpressedPercent => _zone == 0 ? 0 : 100.0 * _unpressed / _zone;

        public void Add(CentralZoneMetrics match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            _matches++;
            foreach (bool home in new[] { true, false })
            {
                _box += match.BoxSpells(home);
                _zone += match.ZoneSpells(home);
                _unpressed += match.ZoneUnpressed(home);
            }
        }

        /// <summary>Every reading against its band, in doc §11.5 table order.</summary>
        public IReadOnlyList<ShotPassRow> Rows() => new[]
        {
            new ShotPassRow(RealismReference.Events.PenaltyAreaTouchesPerTeamMatch, BoxSpellsPerTeamMatch),
            new ShotPassRow(RealismReference.Events.CentralZonePossessionsPerTeamMatch, ZoneSpellsPerTeamMatch),
            new ShotPassRow(UnpressedName, ZoneUnpressedPercent),
        };

        /// <summary>The printable block: one line per reading with its band and IN/OUT.</summary>
        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== central zone and penalty area, issue #206 (report only): {label} ({Matches} matches) ===");
            sb.AppendLine("  on-ball spells per team-match; zone <= 16 m from goal centre; unpressed = no defender within 3 m (doc §11.3)");
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
