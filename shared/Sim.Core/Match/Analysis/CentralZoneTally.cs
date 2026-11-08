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

        private const int N = CentralZoneAnalyzer.SourceCount;
        private readonly long[] _boxBy = new long[N], _boxPressedBy = new long[N];
        private readonly long[] _zoneBy = new long[N], _zonePressedBy = new long[N];
        private readonly long[] _shotsBy = new long[N], _goalsBy = new long[N], _xgBy = new long[N];
        private long _outsideShots, _outsideGoals, _outsideXg, _restartGoals;
        private readonly long[] _carryFrom = new long[N], _carryBand = new long[CentralZoneMetrics.CarryBands];

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
                for (int i = 0; i < N; i++)
                {
                    var src = (ChanceSource)i;
                    _boxBy[i] += match.BoxSpells(home, src);
                    _boxPressedBy[i] += match.BoxPressed(home, src);
                    _zoneBy[i] += match.ZoneSpells(home, src);
                    _zonePressedBy[i] += match.ZonePressed(home, src);
                    _shotsBy[i] += match.Shots(home, src);
                    _goalsBy[i] += match.Goals(home, src);
                    _xgBy[i] += match.XgPermille(home, src);
                    _carryFrom[i] += match.ZoneCarriesFrom(home, src);
                }

                _outsideShots += match.OutsideShots(home);
                _outsideGoals += match.OutsideGoals(home);
                _outsideXg += match.OutsideXgPermille(home);
                _restartGoals += match.RestartGoals(home);
                for (int b = 0; b < CentralZoneMetrics.CarryBands; b++) _carryBand[b] += match.ZoneCarriesFromBand(home, b);
            }
        }

        /// <summary>Zone spells per team-match whose arrival in the zone came from <paramref name="source"/>.</summary>
        public double ZoneSpellsFrom(ChanceSource source) => PerTeamMatch(_zoneBy[(int)source]);

        /// <summary>Box spells per team-match whose arrival in the box came from <paramref name="source"/>.</summary>
        public double BoxSpellsFrom(ChanceSource source) => PerTeamMatch(_boxBy[(int)source]);

        /// <summary>Goals per team-match from spells labelled <paramref name="source"/>.</summary>
        public double GoalsFrom(ChanceSource source) => PerTeamMatch(_goalsBy[(int)source]);

        private double PerTeamMatch(long count) => _matches == 0 ? 0 : count / (2.0 * _matches);

        private static double Share(long part, long whole) => whole == 0 ? 0 : 100.0 * part / whole;

        /// <summary>
        /// Issue #210: where the zone and box spells come from, per team-match, with the share pressed
        /// on arrival, and the shots, goals and location xG of the spells each source labels.
        /// </summary>
        public string FormatSources()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("  sources (issue #210): per team-match; pressed = defender within 3 m on arrival; shots/goals/xG of the spells each source labels");
            sb.AppendLine("    source        zone  pressed%     box  pressed%   shots   goals      xG   xG/shot");
            for (int i = 0; i < N; i++)
            {
                sb.AppendLine(string.Format(inv, "    {0,-12} {1,6:F2}  {2,7:F1}  {3,6:F2}  {4,7:F1}  {5,6:F2}  {6,6:F3}  {7,6:F3}  {8,7:F3}",
                    (ChanceSource)i, PerTeamMatch(_zoneBy[i]), Share(_zonePressedBy[i], _zoneBy[i]),
                    PerTeamMatch(_boxBy[i]), Share(_boxPressedBy[i], _boxBy[i]), PerTeamMatch(_shotsBy[i]),
                    PerTeamMatch(_goalsBy[i]), PerTeamMatch(_xgBy[i]) / 1000.0,
                    _shotsBy[i] == 0 ? 0 : _xgBy[i] / 1000.0 / _shotsBy[i]));
            }

            sb.AppendLine(string.Format(inv, "    {0,-12} {1,6}  {2,7}  {3,6}  {4,7}  {5,6:F2}  {6,6:F3}  {7,6:F3}  {8,7:F3}",
                "outside box", "-", "-", "-", "-", PerTeamMatch(_outsideShots), PerTeamMatch(_outsideGoals),
                PerTeamMatch(_outsideXg) / 1000.0, _outsideShots == 0 ? 0 : _outsideXg / 1000.0 / _outsideShots));
            sb.AppendLine(string.Format(inv, "    goals off a set-piece taker's own shot (penalty, direct free kick) {0:F3}", PerTeamMatch(_restartGoals)));
            sb.Append("    zone carries, how he got it:");
            for (int i = 0; i < N; i++)
                if (_carryFrom[i] > 0) sb.Append(string.Format(inv, " {0} {1:F2}", (ChanceSource)i, PerTeamMatch(_carryFrom[i])));
            sb.AppendLine();
            sb.AppendLine(string.Format(inv, "    zone carries began: <20 m {0:F2}   20-25 m {1:F2}   >=25 m {2:F2}",
                PerTeamMatch(_carryBand[0]), PerTeamMatch(_carryBand[1]), PerTeamMatch(_carryBand[2])));
            return sb.ToString();
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

            sb.Append(FormatSources());
            return sb.ToString();
        }
    }
}
