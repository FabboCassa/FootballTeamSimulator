using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Core.Match.Analysis
{
    /// <summary>One R3 reading: top tier, second tier, the gap (second minus top) and whether R3 holds.</summary>
    public readonly struct TierGap
    {
        public string Name { get; }
        public double Top { get; }
        public double Second { get; }
        public double Delta => Second - Top;
        public string Target { get; }
        public bool Met { get; }

        public TierGap(string name, double top, double second, string target, bool met)
        {
            Name = name;
            Top = top;
            Second = second;
            Target = target;
            Met = met;
        }
    }

    /// <summary>
    /// The R3 quality-tier comparison (real-match spec): one tier's matches pooled, and the gap
    /// between two equal top-tier-average sides and two equal second-tier-average sides.
    ///
    /// Every pass and sequence reading (pass accuracy, long-ball share, passes per open-play
    /// sequence) and ball in play are <see cref="ShotPassAnalyzer"/>'s, the one reading of the §1.5
    /// definitions that R2-R10 share; this only pools them. Ratios are pooled over every match
    /// (§1.5); ball in play and goals are means over matches. Goals are the report's score.
    /// </summary>
    public sealed class QualityTierTally
    {
        // R3: the second tier against the top tier.
        public const double BallInPlayDropMinutes = 1.5;
        public const double PassAccuracyDropPoints = 3;
        public const double LongBallRisePoints = 2;
        public static readonly RealismBand SecondTierGoals = new RealismBand("second-tier goals/match", 2.3, 2.9);

        private ShotPassMetrics? _sum;
        private long _goals;

        public int Matches => _sum?.Matches ?? 0;

        /// <summary>One match: its <see cref="ShotPassAnalyzer"/> reading and its goals.</summary>
        public void Add(ShotPassMetrics passing, int goals)
        {
            if (passing == null) throw new ArgumentNullException(nameof(passing));
            if (goals < 0) throw new ArgumentOutOfRangeException(nameof(goals), goals, "Goals cannot be negative.");
            _sum = _sum == null ? passing.Plus(new ShotPassMetrics { Matches = 0 }) : _sum.Plus(passing);
            _goals += goals;
        }

        public double GoalsPerMatch => Matches == 0 ? 0 : (double)_goals / Matches;
        public double BallInPlayMinutes => Matches == 0 ? 0 : _sum!.BallInPlayMinutes / Matches;
        public double PassAccuracyPercent => _sum?.PassAccuracyPercent ?? 0;
        public double LongBallSharePercent => _sum?.LongBallSharePercent ?? 0;
        public double PassesPerSequence => _sum?.PassesPerSequence ?? 0;

        /// <summary>Every R3 reading, in the spec's order.</summary>
        public static IReadOnlyList<TierGap> Compare(QualityTierTally top, QualityTierTally second)
        {
            if (top == null) throw new ArgumentNullException(nameof(top));
            if (second == null) throw new ArgumentNullException(nameof(second));
            CultureInfo inv = CultureInfo.InvariantCulture;

            return new[]
            {
                Gap("ball in play min", top.BallInPlayMinutes, second.BallInPlayMinutes,
                    "<= -" + BallInPlayDropMinutes.ToString("0.0", inv), d => d <= -BallInPlayDropMinutes),
                Gap("pass accuracy %", top.PassAccuracyPercent, second.PassAccuracyPercent,
                    "<= -" + PassAccuracyDropPoints.ToString("0", inv) + " pp", d => d <= -PassAccuracyDropPoints),
                Gap("long-ball share %", top.LongBallSharePercent, second.LongBallSharePercent,
                    ">= +" + LongBallRisePoints.ToString("0", inv) + " pp", d => d >= LongBallRisePoints),
                Gap("passes per open-play sequence", top.PassesPerSequence, second.PassesPerSequence,
                    "< 0", d => d < 0),
                new TierGap("goals/match", top.GoalsPerMatch, second.GoalsPerMatch,
                    "second tier " + SecondTierGoals.Describe(), SecondTierGoals.Contains(second.GoalsPerMatch)),
            };
        }

        public static string Format(string label, QualityTierTally top, QualityTierTally second)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== R3 quality tier: {label} (top tier {top.Matches} matches, second tier {second.Matches} matches) ===");
            sb.AppendLine(string.Format(inv, "  {0,-32} {1,9} {2,9} {3,9}   {4,-24} {5}",
                "metric", "top", "second", "delta", "R3 target", "verdict"));
            foreach (TierGap g in Compare(top, second))
            {
                sb.AppendLine(string.Format(inv, "  {0,-32} {1,9:F2} {2,9:F2} {3,9}   {4,-24} {5}",
                    g.Name, g.Top, g.Second, g.Delta.ToString("+0.00;-0.00;0.00", inv), g.Target,
                    g.Met ? "MET" : "MISSED"));
            }

            return sb.ToString();
        }

        private static TierGap Gap(string name, double top, double second, string target, Func<double, bool> holds) =>
            new TierGap(name, top, second, target, holds(second - top));
    }
}
