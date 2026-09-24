using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>One summary line for every cut span of at least a match minute (spec R14).</summary>
    internal static class CutSummaryBuilder
    {
        // Most telling first: a span with a foul and a throw-in is summarised by the foul.
        private static readonly BallActionKind[] StoppagePriority =
        {
            BallActionKind.HalfTime, BallActionKind.Foul, BallActionKind.Offside, BallActionKind.Corner,
            BallActionKind.FreeKick, BallActionKind.GoalKick, BallActionKind.ThrowIn, BallActionKind.Kickoff
        };

        public static IReadOnlyList<CutSummary> Build(PositionStream s, IReadOnlyList<BroadcastSegment> segments)
        {
            var summaries = new List<CutSummary>();
            foreach (BroadcastSegment seg in segments)
                if (seg.Rate == PlaybackRate.Cut && seg.Frames >= s.TicksPerMinute)
                    summaries.Add(Summarise(s, seg.StartFrame, seg.EndFrame));
            return summaries;
        }

        private static CutSummary Summarise(PositionStream s, int start, int end)
        {
            int home = 0, away = 0;
            for (int f = start; f < end && f < s.Owner.Length; f++)
                if (s.TryOwner(s.Owner[f], out bool isHome, out _))
                {
                    if (isHome) home++;
                    else away++;
                }

            PossessionSide side = home > away ? PossessionSide.Home
                : away > home ? PossessionSide.Away
                : PossessionSide.None;
            return new CutSummary(start, end, side, Zone(s, start, end, side != PossessionSide.Away), Stoppage(s, start, end));
        }

        /// <summary>The third the ball spent most frames in, seen from the side in possession (home when nobody).</summary>
        private static CutZone Zone(PositionStream s, int start, int end, bool home)
        {
            var counts = new int[3];
            for (int f = start; f < end; f++)
            {
                int progress = FrameClassifier.Progress(s.BallXY[f * 2], home);
                counts[progress * 3 / (Pitch.LengthDm + 1)]++;
            }

            int best = 0;
            for (int z = 1; z < counts.Length; z++)
                if (counts[z] > counts[best]) best = z;
            return (CutZone)best;
        }

        private static BallActionKind? Stoppage(PositionStream s, int start, int end)
        {
            int rank = StoppagePriority.Length;
            foreach (BallAction a in s.Actions)
            {
                if (a.Tick < start || a.Tick >= end) continue;
                int r = System.Array.IndexOf(StoppagePriority, a.Kind);
                if (r >= 0 && r < rank) rank = r;
            }

            return rank < StoppagePriority.Length ? StoppagePriority[rank] : (BallActionKind?)null;
        }
    }
}
