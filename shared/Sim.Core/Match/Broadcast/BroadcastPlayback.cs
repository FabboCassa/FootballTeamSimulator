using System;
using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// The playback clock of a <see cref="BroadcastTimeline"/> (spec R12): wall time in, stream
    /// position out. Live frames play at 1.3x the stream's own rate × speed, dead-time frames at 2x,
    /// and cut frames are jumped at no wall-time cost, so nothing ever plays below 1.3x real time at
    /// 1x and 2x / 4x scale every rate. Position runs from the start frame to the last frame, where
    /// playback is finished.
    ///
    /// Every jumped cut is a <see cref="CutTransition"/> (spec believable-match R11): <see cref="CutCrossed"/>
    /// fires as it is crossed, and <see cref="Curtain"/> dips to black around it, so the jump itself
    /// is never seen. The dip overlaps the frames either side and costs no wall time of its own.
    ///
    /// Pure and allocation-free per <see cref="Advance"/>, so a renderer can drive it every pump.
    /// </summary>
    public sealed class BroadcastPlayback
    {
        /// <summary>The clock at the last frame, and a ceiling it never reads past before it.</summary>
        public const int FullTimeMinute = 90;

        /// <summary>Wall seconds of a cut's fade at 1x, half before the jump and half after; 2x and 4x shorten it.</summary>
        public const double FadeSeconds = 0.3;

        private const double HalfFade = FadeSeconds / 2;

        private readonly BroadcastTimeline _timeline;
        private readonly double _realFramesPerSecond;
        private int _segment;

        public BroadcastPlayback(BroadcastTimeline timeline, int startFrame = 0)
        {
            _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            _realFramesPerSecond = timeline.FramesPerMinute / 60.0;
            LastFrame = timeline.FrameCount > 0 ? timeline.FrameCount - 1 : 0;
            Seek(startFrame);
        }

        /// <summary>1, 2 or 4: the factor over the timeline's own pace.</summary>
        public int Speed { get; private set; } = 1;

        /// <summary>Stream position in frames: <see cref="Frame"/> plus <see cref="Fraction"/>.</summary>
        public double Position { get; private set; }

        public int Frame => (int)Position;

        public double Fraction => Position - Frame;

        public int LastFrame { get; }

        public bool Finished => Position >= LastFrame || _timeline.FramesPerMinute <= 0;

        /// <summary>Fired by <see cref="Advance"/> and <see cref="Follow"/> for every cut boundary crossed.</summary>
        public event Action<CutTransition>? CutCrossed;

        /// <summary>
        /// How far the picture is faded out at <see cref="Position"/>: 0 clear, 1 black. It falls
        /// linearly over the last <see cref="FadeSeconds"/>/2 of 1x playback before a cut and rises
        /// back over the first after it, so at speed S the whole dip lasts FadeSeconds/S of wall.
        /// </summary>
        public double Curtain
        {
            get
            {
                if (Finished) return 0.0; // full time shows its last frame, even when a cut runs to it
                int i = SegmentAt(Position);
                if (i >= _timeline.Segments.Count) return 0.0;
                if (_timeline.Segments[i].Rate == PlaybackRate.Cut) return 1.0;
                return Math.Max(Dip(SecondsSinceCut(i)), Dip(SecondsToCut(i)));
            }
        }

        public int Minute
        {
            get
            {
                if (_timeline.FramesPerMinute <= 0) return 0;
                if (Finished) return FullTimeMinute;
                return Math.Min(FullTimeMinute, Frame / _timeline.FramesPerMinute);
            }
        }

        public void SetSpeed(int speed)
        {
            if (speed < 1)
                throw new ArgumentOutOfRangeException(nameof(speed), speed, "playback never runs below real time");
            Speed = speed;
        }

        /// <summary>Moves playback to <paramref name="frame"/> (a re-sim resumes from the input's tick).</summary>
        public void Seek(int frame)
        {
            Position = Math.Max(0, Math.Min(frame, LastFrame));
            _segment = SegmentAt(Position);
        }

        /// <summary>
        /// Moves to the position a shared clock shows (an online live match), crossing the cuts in
        /// between as <see cref="Advance"/> would. A clock that steps back is followed without
        /// replaying any transition.
        /// </summary>
        public void Follow(double position)
        {
            position = Math.Max(0.0, Math.Min(position, LastFrame));
            if (position < Position)
            {
                Position = position;
                _segment = SegmentAt(position);
                return;
            }

            // Capped at the last frame as Advance caps it, so a cut that runs to full time is crossed too.
            while (_segment < _timeline.Segments.Count && Math.Min(_timeline.Segments[_segment].EndFrame, LastFrame) <= position)
            {
                BroadcastSegment s = _timeline.Segments[_segment++];
                if (s.Rate == PlaybackRate.Cut) Cross(s);
            }

            Position = position;
        }

        public void Skip()
        {
            Position = LastFrame;
            _segment = _timeline.Segments.Count;
        }

        /// <summary>Plays <paramref name="wallSeconds"/> of wall time at the current speed.</summary>
        public void Advance(double wallSeconds)
        {
            double budget = Math.Max(0.0, wallSeconds) * Speed;

            while (!Finished && _segment < _timeline.Segments.Count)
            {
                BroadcastSegment s = _timeline.Segments[_segment];
                double end = Math.Min(s.EndFrame, LastFrame);

                if (s.Rate == PlaybackRate.Cut)
                {
                    Position = end;
                    _segment++;
                    Cross(s);
                    continue;
                }

                double framesPerSecond = _realFramesPerSecond * s.Rate.Factor();
                double need = (end - Position) / framesPerSecond;
                if (budget < need)
                {
                    Position += budget * framesPerSecond;
                    return;
                }

                budget -= need;
                Position = end;
                _segment++;
            }
        }

        private void Cross(BroadcastSegment cut) =>
            CutCrossed?.Invoke(new CutTransition(cut.StartFrame, cut.EndFrame, SummaryOf(cut.StartFrame), FadeSeconds / Speed));

        /// <summary>The summary filed for the cut starting at <paramref name="frame"/>; summaries are in frame order.</summary>
        private CutSummary? SummaryOf(int frame)
        {
            IReadOnlyList<CutSummary> summaries = _timeline.Summaries;
            int lo = 0, hi = summaries.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                int start = summaries[mid].StartFrame;
                if (start == frame) return summaries[mid];
                if (start < frame) lo = mid + 1;
                else hi = mid - 1;
            }

            return null;
        }

        /// <summary>The segment holding <paramref name="position"/>; the count past the last one.</summary>
        private int SegmentAt(double position)
        {
            IReadOnlyList<BroadcastSegment> segments = _timeline.Segments;
            int lo = 0, hi = segments.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (segments[mid].EndFrame <= position) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        private static double Dip(double seconds) => seconds < HalfFade ? 1.0 - seconds / HalfFade : 0.0;

        /// <summary>1x seconds of playback back to the last cut, or past the fade when there is none near.</summary>
        private double SecondsSinceCut(int segment)
        {
            double seconds = 0.0;
            double position = Position;
            for (int i = segment; i >= 0 && seconds < HalfFade; i--)
            {
                BroadcastSegment s = _timeline.Segments[i];
                if (s.Rate == PlaybackRate.Cut) return seconds;
                seconds += (position - s.StartFrame) / (_realFramesPerSecond * s.Rate.Factor());
                position = s.StartFrame;
            }

            return double.MaxValue;
        }

        /// <summary>1x seconds of playback on to the next cut, or past the fade when there is none near.</summary>
        private double SecondsToCut(int segment)
        {
            double seconds = 0.0;
            double position = Position;
            for (int i = segment; i < _timeline.Segments.Count && seconds < HalfFade; i++)
            {
                BroadcastSegment s = _timeline.Segments[i];
                if (s.Rate == PlaybackRate.Cut) return seconds;
                seconds += (s.EndFrame - position) / (_realFramesPerSecond * s.Rate.Factor());
                position = s.EndFrame;
            }

            return double.MaxValue;
        }
    }
}
