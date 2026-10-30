using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Pools the free-ball turns and geometric crosses of many matches (free-ball-real-movement
    /// spec R2) and prints each reading next to its R1 band: the turn share against
    /// <see cref="RealismReference.Tracking.FastFreeBallTurnPercent"/> (the mean of the per-match
    /// shares, doc §10.4), the crosses per team-match against <see cref="RealismReference.Events"/>.
    /// Every turn with no cause is listed as a defect. REPORT ONLY: nothing here fails a run.
    /// </summary>
    public sealed class TurnCrossTally
    {
        public const string StepsName = "  fast free-ball steps/match";
        public const string NoCauseName = "  ... turns with no cause (defects)/match";
        public const string LabelledName = "  crosses the engine labels/team-match";

        private static readonly BallEventKind[] Causes =
            { BallEventKind.Touch, BallEventKind.Bounce, BallEventKind.Deflection, BallEventKind.Post, BallEventKind.None };

        private readonly int[] _causes = new int[Causes.Length];
        private readonly List<(int Match, int FramesPerMinute, FreeBallTurn Turn)> _defects =
            new List<(int, int, FreeBallTurn)>();

        private int _turnMatches, _crossMatches;
        private double _turnPercent, _steps;
        private int _high, _low, _labelled;

        public int Matches => Math.Max(_turnMatches, _crossMatches);

        /// <summary>Turns that nothing explains, with the index the caller gave their match.</summary>
        public IReadOnlyList<(int Match, int FramesPerMinute, FreeBallTurn Turn)> Defects => _defects;

        public void Add(int match, FreeBallTurnMetrics turns)
        {
            if (turns == null) throw new ArgumentNullException(nameof(turns));
            _turnMatches++;
            _turnPercent += turns.TurnPercent;
            _steps += turns.Steps;
            for (int c = 0; c < Causes.Length; c++) _causes[c] += turns.CountOf(Causes[c]);
            foreach (FreeBallTurn t in turns.Turns)
                if (t.Cause == BallEventKind.None) _defects.Add((match, turns.FramesPerMinute, t));
        }

        public void Add(GeometricCrossMetrics crosses)
        {
            if (crosses == null) throw new ArgumentNullException(nameof(crosses));
            _crossMatches++;
            _high += crosses.High;
            _low += crosses.Low;
            _labelled += crosses.Labelled;
        }

        public IReadOnlyList<ShotPassRow> Rows()
        {
            double n = _turnMatches == 0 ? 1 : _turnMatches, teams = 2.0 * (_crossMatches == 0 ? 1 : _crossMatches);
            var rows = new List<ShotPassRow>
            {
                new ShotPassRow(RealismReference.Tracking.FastFreeBallTurnPercent, _turnPercent / n),
                new ShotPassRow(StepsName, _steps / n),
            };
            for (int c = 0; c < Causes.Length; c++)
                rows.Add(new ShotPassRow(Causes[c] == BallEventKind.None ? NoCauseName : CauseName(Causes[c]), _causes[c] / n));
            rows.Add(new ShotPassRow(RealismReference.Events.HighCrossesPerTeamMatch, _high / teams));
            rows.Add(new ShotPassRow(RealismReference.Events.LowCrossesPerTeamMatch, _low / teams));
            rows.Add(new ShotPassRow(LabelledName, _labelled / teams));
            return rows;
        }

        public static string CauseName(BallEventKind cause) =>
            "  ... turns off a " + cause.ToString().ToLowerInvariant() + "/match";

        /// <summary>The printable block: one line per reading with its band and IN/OUT, then every defect.</summary>
        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== free-ball turn causes, geometric crosses R2 (report only): {label} ({Matches} matches) ===");
            sb.AppendLine("  turn: > 20 deg between 0.2 s steps, both > 5 m/s, nobody within 2 m, ball below 1 m (doc §10.3)");
            sb.AppendLine("  cross: open-play pass from the wide final third into the box; high = peak >= 2 m (doc §11.3)");
            foreach (ShotPassRow row in Rows())
            {
                string band = row.Band?.Describe() ?? "-";
                string verdict = row.InBand == null ? "" : (row.InBand.Value ? "IN" : "OUT");
                sb.AppendLine(string.Format(inv, "  {0,-46} {1,9:F3}   band {2,-12} {3}", row.Name, row.Value, band, verdict));
            }

            sb.AppendLine(string.Format(inv, "  DEFECTS: {0} free-ball turns with no cause", _defects.Count));
            foreach ((int match, int fpm, FreeBallTurn t) in _defects)
            {
                int fps = Math.Max(1, fpm / 60);
                sb.AppendLine(string.Format(inv,
                    "    DEFECT match {0} frame {1} ({2:00}:{3:00}): no cause; nearest {4} {5} at {6:F1} m",
                    match, t.Frame, t.Frame / Math.Max(1, fpm), t.Frame / fps % 60,
                    t.NearestHome ? "home" : "away", t.NearestSlot, t.NearestDm / 10.0));
            }

            return sb.ToString();
        }
    }
}
