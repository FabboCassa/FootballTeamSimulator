namespace Sim.Core.Match.Analysis
{
    /// <summary>The reading a touchline shout is meant to move (R11).</summary>
    public enum ShoutMetric
    {
        None = 0,
        HighRecoveries = 1,
        PossessionShare = 2,
        Shots = 3,
        BallLosses = 4,
        OwnHalfLosses = 5
    }

    /// <summary>
    /// Each shout's target metric, read off the shouting side over the minutes it is heard. The
    /// mapping follows the spec's wording (R11) and what each <see cref="ShoutEffect"/> actually
    /// turns in the engine:
    ///   PressHigh   "pressing up"               → balls won back in the opponent's half, up
    ///   KeepBall    "tempo down, risk down"     → possession share, up
    ///   AllForward  "mentality up"              → shots, up
    ///   Encourage   "composure up"              → balls lost anywhere, down (pressure is felt less)
    ///   Concentrate "fewer defensive errors"    → balls lost in the own half, down
    /// </summary>
    public static class ShoutTargets
    {
        public static ShoutMetric MetricOf(TouchlineShout shout)
        {
            switch (shout)
            {
                case TouchlineShout.PressHigh: return ShoutMetric.HighRecoveries;
                case TouchlineShout.KeepBall: return ShoutMetric.PossessionShare;
                case TouchlineShout.AllForward: return ShoutMetric.Shots;
                case TouchlineShout.Encourage: return ShoutMetric.BallLosses;
                case TouchlineShout.Concentrate: return ShoutMetric.OwnHalfLosses;
                default: return ShoutMetric.None;
            }
        }

        /// <summary>+1 when the shout should raise the metric, -1 when it should lower it, 0 for none.</summary>
        public static int ExpectedSign(ShoutMetric metric)
        {
            switch (metric)
            {
                case ShoutMetric.HighRecoveries:
                case ShoutMetric.PossessionShare:
                case ShoutMetric.Shots:
                    return 1;
                case ShoutMetric.BallLosses:
                case ShoutMetric.OwnHalfLosses:
                    return -1;
                default:
                    return 0;
            }
        }

        public static string Describe(ShoutMetric metric)
        {
            switch (metric)
            {
                case ShoutMetric.HighRecoveries: return "high recoveries (up)";
                case ShoutMetric.PossessionShare: return "possession share (up)";
                case ShoutMetric.Shots: return "shots (up)";
                case ShoutMetric.BallLosses: return "balls lost (down)";
                case ShoutMetric.OwnHalfLosses: return "own-half losses (down)";
                default: return "none";
            }
        }

        /// <summary>The metric for <paramref name="home"/>'s side over minutes [from, to).</summary>
        public static double Read(ShoutMetric metric, PositionStream s, bool home, int from, int to)
        {
            switch (metric)
            {
                case ShoutMetric.HighRecoveries: return MatchWindow.HighRecoveries(s, home, from, to);
                case ShoutMetric.PossessionShare: return MatchWindow.PossessionShare(s, home, from, to);
                case ShoutMetric.Shots: return MatchWindow.Shots(s, home, from, to);
                case ShoutMetric.BallLosses: return MatchWindow.BallLosses(s, home, from, to);
                case ShoutMetric.OwnHalfLosses: return MatchWindow.OwnHalfLosses(s, home, from, to);
                default: return 0;
            }
        }
    }
}
