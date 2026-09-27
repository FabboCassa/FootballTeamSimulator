using System;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// The shared clock of an online live match (spec R16): the moment on screen is the director
    /// timeline played at 1x from the shared kickoff instant. Every client builds the same timeline
    /// from the same report, so any two viewers read the same frame at the same wall-clock instant,
    /// whenever each of them opened the match.
    ///
    /// Reads are cheap when time moves forward (the playback only advances by the difference) and a
    /// read of an earlier instant replays from kickoff, so a device clock that steps back is followed.
    /// </summary>
    public sealed class LiveBroadcastClock
    {
        private readonly BroadcastPlayback _playback;
        private double _elapsedSeconds;

        public LiveBroadcastClock(BroadcastTimeline timeline, DateTime kickoffUtc)
        {
            Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            KickoffUtc = kickoffUtc;
            _playback = new BroadcastPlayback(timeline);
        }

        public BroadcastTimeline Timeline { get; }

        public DateTime KickoffUtc { get; }

        /// <summary>The stream position (in frames) on screen at <paramref name="nowUtc"/>.</summary>
        public double PositionAt(DateTime nowUtc)
        {
            double elapsed = Math.Max(0.0, (nowUtc - KickoffUtc).TotalSeconds);
            if (elapsed < _elapsedSeconds)
            {
                _playback.Seek(0);
                _elapsedSeconds = 0.0;
            }

            _playback.Advance(elapsed - _elapsedSeconds);
            _elapsedSeconds = elapsed;
            return _playback.Position;
        }

        /// <summary>The match minute on screen at <paramref name="nowUtc"/> (90 once the timeline is over).</summary>
        public int MinuteAt(DateTime nowUtc)
        {
            PositionAt(nowUtc);
            return _playback.Minute;
        }

        public bool FinishedAt(DateTime nowUtc)
        {
            PositionAt(nowUtc);
            return _playback.Finished;
        }

        /// <summary>
        /// The wall seconds after kickoff at which the shown minute reaches <paramref name="minute"/>
        /// (full time at 90). Rounded up to the millisecond, so a clock read at kickoff plus this
        /// value never lands a hair before the minute.
        /// </summary>
        public double SecondsToReach(int minute)
        {
            int framesPerMinute = Timeline.FramesPerMinute;
            if (framesPerMinute <= 0 || minute <= 0 || Timeline.FrameCount <= 0)
                return 0.0;

            long target = Math.Min((long)minute * framesPerMinute, Timeline.FrameCount - 1);
            double realFramesPerSecond = framesPerMinute / 60.0;
            double seconds = 0.0;
            foreach (BroadcastSegment s in Timeline.Segments)
            {
                if (s.StartFrame >= target)
                    break;
                if (s.Rate == PlaybackRate.Cut)
                    continue;
                long frames = Math.Min(s.EndFrame, target) - s.StartFrame;
                seconds += frames / (realFramesPerSecond * (int)s.Rate);
            }

            return Math.Ceiling(seconds * 1000.0) / 1000.0;
        }
    }
}
