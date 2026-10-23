using System;
using System.Collections.Generic;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Analysis
{
    /// <summary>The R19 fingerprint metrics: the six every style is read on, then the three a style picks its seventh from.</summary>
    public enum FingerprintMetric
    {
        PassesPerTeam,
        PossessionPercent,
        PassesPerSequence,
        TenPlusSequencesPerTeam,
        DirectSpeed,
        Ppda,
        CrossesPerTeam,
        LongBallSharePercent,
        LineHeightM,
    }

    /// <summary>One side's R19 readings over a set of matches.</summary>
    public sealed class FingerprintReading
    {
        public double PassesPerTeam { get; set; }

        /// <summary>
        /// The side's share of the match's attempted passes, as FBref and Opta compute possession: the
        /// scale of the R19 anchors (doc §3.2, §3.3), not the engine's share of frames with the ball held.
        /// </summary>
        public double PossessionPercent { get; set; }
        public double PassesPerSequence { get; set; }
        public double TenPlusSequencesPerTeam { get; set; }
        public double DirectSpeed { get; set; }
        public double Ppda { get; set; }
        public double CrossesPerTeam { get; set; }
        public double LongBallSharePercent { get; set; }

        /// <summary>The side's last line (second-deepest man, the keeper being the deepest) while the opponent has the ball, m from its own goal.</summary>
        public double LineHeightM { get; set; }

        /// <summary>Not a fingerprint metric: the Gegenpress anchor reads it against Balanced.</summary>
        public double HighTurnoversPerTeam { get; set; }

        public double Get(FingerprintMetric metric) => metric switch
        {
            FingerprintMetric.PassesPerTeam => PassesPerTeam,
            FingerprintMetric.PossessionPercent => PossessionPercent,
            FingerprintMetric.PassesPerSequence => PassesPerSequence,
            FingerprintMetric.TenPlusSequencesPerTeam => TenPlusSequencesPerTeam,
            FingerprintMetric.DirectSpeed => DirectSpeed,
            FingerprintMetric.Ppda => Ppda,
            FingerprintMetric.CrossesPerTeam => CrossesPerTeam,
            FingerprintMetric.LongBallSharePercent => LongBallSharePercent,
            FingerprintMetric.LineHeightM => LineHeightM,
            _ => throw new ArgumentOutOfRangeException(nameof(metric)),
        };
    }

    /// <summary>
    /// A style's R19 fingerprint: for each of its seven metrics the direction real football moves
    /// it against the league mean (+1 up, -1 down, 0 no published direction, which can never count
    /// as moved). Read from docs/research/football-reference.md §3.1; the table and its sources
    /// are §3.3 there.
    /// </summary>
    public sealed class StyleFingerprint
    {
        public PlayingStyle Style { get; }
        public FingerprintMetric Seventh { get; }
        private readonly int[] _directions;

        public StyleFingerprint(PlayingStyle style, int passes, int possession, int perSequence, int tenPlus,
            int directSpeed, int ppda, FingerprintMetric seventh, int seventhDirection)
        {
            if (seventh < FingerprintMetric.CrossesPerTeam)
                throw new ArgumentOutOfRangeException(nameof(seventh), "the seventh metric is crosses, long-ball share or line height");
            Style = style;
            Seventh = seventh;
            _directions = new[] { passes, possession, perSequence, tenPlus, directSpeed, ppda, seventhDirection };
        }

        /// <summary>The seven metrics, in R19 order.</summary>
        public IReadOnlyList<FingerprintMetric> Metrics => new[]
        {
            FingerprintMetric.PassesPerTeam, FingerprintMetric.PossessionPercent, FingerprintMetric.PassesPerSequence,
            FingerprintMetric.TenPlusSequencesPerTeam, FingerprintMetric.DirectSpeed, FingerprintMetric.Ppda, Seventh,
        };

        public int Direction(int index) => _directions[index];
    }

    /// <summary>The verdict on one metric of a style against Balanced.</summary>
    public readonly struct FingerprintRow
    {
        public FingerprintMetric Metric { get; }
        public int Direction { get; }
        public double Style { get; }
        public double Balanced { get; }
        public double Minimum { get; }
        public bool Moved { get; }

        public FingerprintRow(FingerprintMetric metric, int direction, double style, double balanced, double minimum, bool moved)
        {
            Metric = metric;
            Direction = direction;
            Style = style;
            Balanced = balanced;
            Minimum = minimum;
            Moved = moved;
        }
    }

    /// <summary>
    /// R19 (docs/specs/real-match-and-playing-styles.md): each style, against Balanced with the
    /// same formation and equal sides, moves at least <see cref="RequiredMoves"/> of its seven
    /// metrics in its real direction by the documented minimum; four anchor styles also meet
    /// absolute bands. Doc: docs/research/football-reference.md §3.2 (anchors) and §3.3 (table).
    /// </summary>
    public static class StyleFingerprints
    {
        public const int RequiredMoves = 5;

        /// <summary>
        /// The documented minimum shift of each metric (doc §3.3): relative to the Balanced reading
        /// for counts and rates, in percentage points or metres for shares and the line. Each is
        /// about the smallest gap §3.1 shows between a style's example team and its league
        /// (Atlético 15-16: 1.05x the passes, 1.5 pp under even possession).
        /// </summary>
        public static double Minimum(FingerprintMetric metric, double balanced) => metric switch
        {
            FingerprintMetric.PassesPerTeam => 0.05 * balanced,
            FingerprintMetric.PossessionPercent => 2.0,
            FingerprintMetric.PassesPerSequence => 0.05 * balanced,
            FingerprintMetric.TenPlusSequencesPerTeam => 0.10 * balanced,
            FingerprintMetric.DirectSpeed => 0.05 * balanced,
            FingerprintMetric.Ppda => 0.05 * balanced,
            FingerprintMetric.CrossesPerTeam => 0.10 * balanced,
            FingerprintMetric.LongBallSharePercent => 2.0,
            FingerprintMetric.LineHeightM => 1.5,
            _ => throw new ArgumentOutOfRangeException(nameof(metric)),
        };

        // Anchor bands (spec R19, doc §3.2). Volume and ratio figures as the spec states them.
        public static readonly RealismBand TikiTakaPossession = new RealismBand("tiki-taka possession %", 58, 70);
        public static readonly RealismBand TikiTakaPasses = new RealismBand("tiki-taka passes/team", 600, 780);
        public static readonly RealismBand TikiTakaPassesPerSequence =
            new RealismBand("tiki-taka passes per sequence", 5.0, double.PositiveInfinity);
        public static readonly RealismBand GegenpressPpda = new RealismBand("gegenpress PPDA", double.NegativeInfinity, 9);
        public static readonly RealismBand GegenpressHighTurnoverRatio =
            new RealismBand("gegenpress high turnovers x Balanced", 1.4, double.PositiveInfinity);
        public static readonly RealismBand RouteOneLongBallShare =
            new RealismBand("route one long-ball share %", 18, double.PositiveInfinity);
        public static readonly RealismBand RouteOnePassesPerSequence =
            new RealismBand("route one passes per sequence", double.NegativeInfinity, 3.0);
        public static readonly RealismBand ParkTheBusPossession =
            new RealismBand("park the bus possession %", double.NegativeInfinity, 40);
        public static readonly RealismBand ParkTheBusPpda = new RealismBand("park the bus PPDA", 16, double.PositiveInfinity);
        public static readonly RealismBand ParkTheBusLine = new RealismBand("park the bus line m", double.NegativeInfinity, 32);

        private const FingerprintMetric Crosses = FingerprintMetric.CrossesPerTeam;
        private const FingerprintMetric Long = FingerprintMetric.LongBallSharePercent;
        private const FingerprintMetric Line = FingerprintMetric.LineHeightM;

        /// <summary>
        /// Every style but Balanced. Columns: passes, possession, passes per sequence, 10+
        /// sequences, direct speed, PPDA, then the seventh metric and its direction (doc §3.3).
        /// </summary>
        public static readonly IReadOnlyList<StyleFingerprint> All = new[]
        {
            new StyleFingerprint(PlayingStyle.TikiTaka, +1, +1, +1, +1, -1, -1, Long, -1),
            new StyleFingerprint(PlayingStyle.PositionalPlay, +1, +1, +1, +1, -1, -1, Line, +1),
            new StyleFingerprint(PlayingStyle.Gegenpress, -1, +1, -1, -1, +1, -1, Line, +1),
            new StyleFingerprint(PlayingStyle.VerticalTikiTaka, +1, +1, +1, +1, -1, -1, Long, -1),
            new StyleFingerprint(PlayingStyle.ControlPossession, +1, +1, +1, +1, -1, 0, Long, -1),
            new StyleFingerprint(PlayingStyle.WingPlay, -1, -1, -1, -1, +1, -1, Crosses, +1),
            new StyleFingerprint(PlayingStyle.RouteOne, -1, -1, -1, -1, +1, +1, Long, +1),
            new StyleFingerprint(PlayingStyle.Direct, -1, -1, -1, -1, +1, 0, Long, +1),
            new StyleFingerprint(PlayingStyle.FluidCounter, -1, -1, -1, -1, +1, 0, Long, +1),
            new StyleFingerprint(PlayingStyle.DirectCounter, -1, -1, -1, -1, +1, +1, Long, +1),
            new StyleFingerprint(PlayingStyle.MidBlock, +1, -1, +1, +1, 0, 0, Long, -1),
            new StyleFingerprint(PlayingStyle.Catenaccio, -1, -1, -1, -1, +1, +1, Line, -1),
            new StyleFingerprint(PlayingStyle.ParkTheBus, -1, -1, -1, -1, +1, +1, Line, -1),
            new StyleFingerprint(PlayingStyle.ManOrientedPress, -1, 0, -1, -1, +1, -1, Line, +1),
        };

        public static StyleFingerprint Get(PlayingStyle style)
        {
            foreach (StyleFingerprint f in All)
                if (f.Style == style) return f;
            throw new ArgumentOutOfRangeException(nameof(style));
        }

        /// <summary>The seven verdicts of a style's reading against the Balanced reading of the same fixtures.</summary>
        public static IReadOnlyList<FingerprintRow> Compare(StyleFingerprint fingerprint, FingerprintReading style,
            FingerprintReading balanced)
        {
            IReadOnlyList<FingerprintMetric> metrics = fingerprint.Metrics;
            var rows = new FingerprintRow[metrics.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                FingerprintMetric metric = metrics[i];
                int direction = fingerprint.Direction(i);
                double s = style.Get(metric), b = balanced.Get(metric);
                double min = Minimum(metric, b);
                bool moved = direction != 0 && direction * (s - b) >= min;
                rows[i] = new FingerprintRow(metric, direction, s, b, min, moved);
            }

            return rows;
        }

        public static int MovedCount(IReadOnlyList<FingerprintRow> rows)
        {
            int moved = 0;
            foreach (FingerprintRow row in rows)
                if (row.Moved) moved++;
            return moved;
        }
    }
}
