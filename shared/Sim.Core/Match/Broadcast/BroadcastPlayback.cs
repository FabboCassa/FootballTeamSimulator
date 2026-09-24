using System;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// The playback clock of a <see cref="BroadcastTimeline"/> (spec R12-R13): wall time in, stream
    /// position out. RealTime frames play at the stream's own rate × speed, Double frames at twice
    /// that, and cut frames are jumped at no wall-time cost, so nothing ever plays below real time
    /// at 1x. Position runs from the start frame to the last frame, where playback is finished.
    ///
    /// Pure and allocation-free per <see cref="Advance"/>, so a renderer can drive it every pump.
    /// </summary>
    public sealed class BroadcastPlayback
    {
        /// <summary>The clock at the last frame, and a ceiling it never reads past before it.</summary>
        public const int FullTimeMinute = 90;

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
            _segment = 0;
            while (_segment < _timeline.Segments.Count && _timeline.Segments[_segment].EndFrame <= Position)
                _segment++;
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
                    continue;
                }

                double framesPerSecond = _realFramesPerSecond * (int)s.Rate;
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
    }
}
