using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Pools the <see cref="BallContinuityMetrics"/> of many matches and prints each reading next to
    /// its band: R5's zero jumps and placed restarts, every wide shot over the line and every goal
    /// in the net, the R1 p99 ball speeds, and R6's keeper reach. REPORT ONLY: nothing here fails a
    /// run until the balance task of the believable-match spec gates it.
    /// </summary>
    public sealed class BallContinuityTally
    {
        public const string JumpsName = "ball jumps > 8 m in a frame/match";
        public const string JumpsAtRestartsName = "  ... on a restart frame (+-1)/match";
        public const string PlacedRestartsName = "placed restarts/match";
        public const string PlacedSharePercentName = "  ... share of restarts %";
        public const string WideCrossingName = "wide shots crossing the goal line %";
        public const string WideShortName = "  ... the others stop short of it, m";
        public const string GoalsBehindName = "goals ending >= 1 m behind the line %";
        public const string SaveDisplacementName = "keeper moved release -> save, m";
        public const string GoalDisplacementName = "keeper moved release -> line on goals, m";
        public const string KeeperNearName = "keeper within 1.5 m at contact on saves %";

        /// <summary>R5: no ball jumps and no placed restarts.</summary>
        public static readonly RealismBand JumpsPerMatch = new RealismBand(JumpsName, 0, 0);
        public static readonly RealismBand PlacedRestartsPerMatch = new RealismBand(PlacedRestartsName, 0, 0);

        /// <summary>R5: every wide shot crosses the line and every goal ends in the net.</summary>
        public static readonly RealismBand WideCrossingPercent = new RealismBand(WideCrossingName, 100, 100);
        public static readonly RealismBand GoalsBehindPercent = new RealismBand(GoalsBehindName, 100, 100);

        /// <summary>R6: on at least 70% of saves.</summary>
        public static readonly RealismBand KeeperNearPercent = new RealismBand(KeeperNearName, 70, double.PositiveInfinity);

        private BallContinuityMetrics? _sum;

        public int Matches => _sum?.Matches ?? 0;

        public void Add(BallContinuityMetrics match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            _sum = _sum == null ? match.Plus(new BallContinuityMetrics { Matches = 0 }) : _sum.Plus(match);
        }

        public IReadOnlyList<ShotPassRow> Rows()
        {
            BallContinuityMetrics t = _sum ?? new BallContinuityMetrics { Matches = 0 };
            double matches = t.Matches == 0 ? 1 : t.Matches;
            return new[]
            {
                new ShotPassRow(JumpsPerMatch, t.Jumps / matches),
                new ShotPassRow(JumpsAtRestartsName, t.JumpsAtRestarts / matches),
                new ShotPassRow(PlacedRestartsPerMatch, t.PlacedRestarts / matches),
                new ShotPassRow(PlacedSharePercentName, Percent(t.PlacedRestarts, t.Restarts)),
                new ShotPassRow(RealismReference.Movement.PassSpeedP99, t.SpeedPercentile(shot: false, 0.99)),
                new ShotPassRow(RealismReference.Movement.ShotSpeedP99, t.SpeedPercentile(shot: true, 0.99)),
                new ShotPassRow(WideCrossingPercent, Percent(t.WideShotsCrossing, t.WideShots)),
                new ShotPassRow(WideShortName, Mean(t.WideShotShortSumM, t.WideShots - t.WideShotsCrossing)),
                new ShotPassRow(GoalsBehindPercent, Percent(t.GoalsBehindLine, t.Goals)),
                new ShotPassRow(SaveDisplacementName, Mean(t.SaveDisplacementSumM, t.SaveDisplacements)),
                new ShotPassRow(GoalDisplacementName, Mean(t.GoalDisplacementSumM, t.GoalDisplacements)),
                new ShotPassRow(KeeperNearPercent, Percent(t.SavesNear, t.SavesRead)),
            };
        }

        /// <summary>The printable block: one line per reading with its band and IN/OUT, plus the raw counts behind the rates.</summary>
        public string Format(string label)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== ball continuity, shot flight, keeper (report only): {label} ({Matches} matches) ===");
            foreach (ShotPassRow row in Rows())
            {
                string band = row.Band?.Describe() ?? "-";
                string verdict = row.InBand == null ? "" : (row.InBand.Value ? "IN" : "OUT");
                sb.AppendLine(string.Format(inv, "  {0,-42} {1,9:F3}   band {2,-12} {3}", row.Name, row.Value, band, verdict));
            }

            BallContinuityMetrics t = _sum ?? new BallContinuityMetrics { Matches = 0 };
            sb.AppendLine(string.Format(inv,
                "  restarts {0} | wide shots {1} | goals {2} | saves {3} (path read {4}) | flight frames: passes {5}, shots {6}",
                t.Restarts, t.WideShots, t.Goals, t.SaveDisplacements, t.SavesRead, t.SpeedFrames(shot: false), t.SpeedFrames(shot: true)));
            return sb.ToString();
        }

        private static double Percent(int part, int whole) => whole <= 0 ? 0 : 100.0 * part / whole;

        private static double Mean(double sum, int count) => count <= 0 ? 0 : sum / count;
    }
}
