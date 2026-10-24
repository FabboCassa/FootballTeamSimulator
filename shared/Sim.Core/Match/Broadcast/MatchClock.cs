using System;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// The one minute formula of a watched match (spec believable-match R10). The clock, a toast and a
    /// commentary line all read the minute of the frame they show, counted as the scoresheet counts
    /// it, so the three agree on the same moment.
    /// </summary>
    public static class MatchClock
    {
        /// <summary>Match minute of a frame: frames 0 to fpm-1 are minute 1, and nothing reads past 90.</summary>
        public static int MinuteOf(int frame, int framesPerMinute) =>
            framesPerMinute > 0 ? InProgress(frame / framesPerMinute) : 1;

        /// <summary>The minute being played once <paramref name="elapsedMinutes"/> whole minutes are behind: kickoff reads 1'.</summary>
        public static int InProgress(int elapsedMinutes) =>
            Math.Max(1, Math.Min(BroadcastPlayback.FullTimeMinute, elapsedMinutes + 1));

        /// <summary>The first frame of match minute <paramref name="minute"/> (1 to 90).</summary>
        public static int FirstFrameOf(int minute, int framesPerMinute) =>
            (Math.Max(1, Math.Min(BroadcastPlayback.FullTimeMinute, minute)) - 1) * Math.Max(0, framesPerMinute);
    }
}
