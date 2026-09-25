using System;
using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// The commentary of a match (spec R14, R15): one sentence per event chain (pass, cross, header,
    /// save), per foul, per cut summary of the director and per touchline shout, in match order.
    /// Pure: it reads the report and the timeline and names nobody; <see cref="CommentaryText"/>
    /// writes a line out in the viewer's language.
    /// </summary>
    public static class CommentaryBuilder
    {
        /// <summary>The touches told before a strike; older ones are dropped so a line stays one line.</summary>
        public const int MaxBuildUpSteps = 3;

        public static IReadOnlyList<CommentaryLine> Build(MatchReport report, BroadcastTimeline timeline)
        {
            var lines = new List<CommentaryLine>();
            PositionStream? s = report?.Positions?.Unpack();
            if (s != null && s.TicksPerMinute > 0)
                ChainReader.Read(s, lines);

            if (timeline != null)
                foreach (CutSummary cut in timeline.Summaries)
                    lines.Add(CutLine(cut, timeline.FramesPerMinute));

            if (report != null)
                AddShouts(report, s != null && s.TicksPerMinute > 0 ? s.TicksPerMinute : 1, lines);

            return InMatchOrder(lines);
        }

        /// <summary>Match minute of a frame, as the scoresheet counts it: frames 0 to fpm-1 are minute 1.</summary>
        internal static int MinuteOf(int frame, int framesPerMinute)
        {
            int minute = framesPerMinute > 0 ? frame / framesPerMinute + 1 : 1;
            return Math.Max(1, Math.Min(90, minute));
        }

        private static CommentaryLine CutLine(CutSummary cut, int fpm)
        {
            bool home = cut.Possession != PossessionSide.Away;
            string zone = cut.Possession == PossessionSide.None ? CommentaryKeys.CutNobody
                : cut.Zone == CutZone.Defensive ? CommentaryKeys.CutDefensive
                : cut.Zone == CutZone.Middle ? CommentaryKeys.CutMiddle
                : CommentaryKeys.CutAttacking;

            var steps = new List<CommentaryClause> { new CommentaryClause(zone, home) };
            string? stop = cut.Stoppage.HasValue ? StoppageKey(cut.Stoppage.Value) : null;
            if (stop != null) steps.Add(new CommentaryClause(stop, home));

            return new CommentaryLine(cut.StartFrame, MinuteOf(cut.StartFrame, fpm),
                MinuteOf(cut.EndFrame - 1, fpm), CommentaryIcon.Cut, false, steps, null);
        }

        private static string? StoppageKey(BallActionKind kind)
        {
            switch (kind)
            {
                case BallActionKind.HalfTime: return CommentaryKeys.StopHalfTime;
                case BallActionKind.Foul: return CommentaryKeys.StopFoul;
                case BallActionKind.Offside: return CommentaryKeys.StopOffside;
                case BallActionKind.Corner: return CommentaryKeys.StopCorner;
                case BallActionKind.FreeKick: return CommentaryKeys.StopFreeKick;
                case BallActionKind.GoalKick: return CommentaryKeys.StopGoalKick;
                case BallActionKind.ThrowIn: return CommentaryKeys.StopThrowIn;
                case BallActionKind.Kickoff: return CommentaryKeys.StopKickoff;
                default: return null;
            }
        }

        private static void AddShouts(MatchReport report, int fpm, List<CommentaryLine> lines)
        {
            foreach (MatchEvent e in report.Events)
            {
                if (e.Type != MatchEventType.Shout) continue;

                bool home = e.ClubId == report.HomeClubId;
                var clause = new CommentaryClause(ShoutKey(e.Shout), home);
                lines.Add(new CommentaryLine((e.Minute - 1) * fpm, e.Minute, e.Minute, CommentaryIcon.Shout, false,
                    new[] { clause }, null));
            }
        }

        private static string ShoutKey(TouchlineShout shout)
        {
            switch (shout)
            {
                case TouchlineShout.PressHigh: return CommentaryKeys.ShoutPressHigh;
                case TouchlineShout.KeepBall: return CommentaryKeys.ShoutKeepBall;
                case TouchlineShout.AllForward: return CommentaryKeys.ShoutAllForward;
                case TouchlineShout.Encourage: return CommentaryKeys.ShoutEncourage;
                default: return CommentaryKeys.ShoutConcentrate;
            }
        }

        /// <summary>Sorted by frame; lines on the same frame keep the order they were added in.</summary>
        private static IReadOnlyList<CommentaryLine> InMatchOrder(List<CommentaryLine> lines)
        {
            var order = new int[lines.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => lines[a].Frame != lines[b].Frame
                ? lines[a].Frame.CompareTo(lines[b].Frame)
                : a.CompareTo(b));

            var sorted = new CommentaryLine[lines.Count];
            for (int i = 0; i < order.Length; i++) sorted[i] = lines[order[i]];
            return sorted;
        }
    }
}
