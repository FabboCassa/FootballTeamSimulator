using System;
using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// The playback plan of one match: contiguous segments tiling frames [0, <see cref="FrameCount"/>),
    /// adjacent segments always at different rates, plus one summary per cut span of a minute or more.
    /// </summary>
    public sealed class BroadcastTimeline
    {
        public BroadcastTimeline(int framesPerMinute, int frameCount, int secondHalfStartFrame,
            IReadOnlyList<BroadcastSegment> segments, IReadOnlyList<CutSummary> summaries)
        {
            FramesPerMinute = framesPerMinute;
            FrameCount = frameCount;
            SecondHalfStartFrame = secondHalfStartFrame;
            Segments = segments;
            Summaries = summaries;
        }

        public static BroadcastTimeline Empty { get; } = new BroadcastTimeline(
            0, 0, 0, Array.Empty<BroadcastSegment>(), Array.Empty<CutSummary>());

        /// <summary>Stream frames per match minute: real time is this many frames per 60 s of playback.</summary>
        public int FramesPerMinute { get; }

        public int FrameCount { get; }

        /// <summary>The frame of the half-time whistle; the first half is every frame before it.</summary>
        public int SecondHalfStartFrame { get; }

        public IReadOnlyList<BroadcastSegment> Segments { get; }

        public IReadOnlyList<CutSummary> Summaries { get; }

        public long TotalPlaybackMilliseconds => PlaybackMilliseconds(0, FrameCount);

        /// <summary>The rate a frame plays at; <see cref="PlaybackRate.Cut"/> outside the stream.</summary>
        public PlaybackRate RateAt(int frame)
        {
            int lo = 0, hi = Segments.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                BroadcastSegment s = Segments[mid];
                if (frame < s.StartFrame) hi = mid - 1;
                else if (frame >= s.EndFrame) lo = mid + 1;
                else return s.Rate;
            }

            return PlaybackRate.Cut;
        }

        /// <summary>Playback time at 1x of frames [<paramref name="fromFrame"/>, <paramref name="toFrame"/>), rounded down to the millisecond.</summary>
        public long PlaybackMilliseconds(int fromFrame, int toFrame) =>
            FramesPerMinute <= 0 ? 0
                : PlaybackUnits(fromFrame, toFrame) * 60_000 / ((long)PlaybackRates.UnitsPerRealFrame * FramesPerMinute);

        /// <summary>Playback time at 1x of frames [<paramref name="fromFrame"/>, <paramref name="toFrame"/>), in seconds.</summary>
        public double PlaybackSeconds(int fromFrame, int toFrame) =>
            FramesPerMinute <= 0 ? 0.0
                : PlaybackUnits(fromFrame, toFrame) * 60.0 / ((double)PlaybackRates.UnitsPerRealFrame * FramesPerMinute);

        /// <summary>Summed in whole cost units (see <see cref="PlaybackRates"/>) so the total stays exact.</summary>
        private long PlaybackUnits(int fromFrame, int toFrame)
        {
            long units = 0;
            foreach (BroadcastSegment s in Segments)
            {
                int frames = Math.Min(s.EndFrame, toFrame) - Math.Max(s.StartFrame, fromFrame);
                if (frames > 0) units += (long)frames * PlaybackRates.CostOf(s.Rate);
            }

            return units;
        }
    }
}
