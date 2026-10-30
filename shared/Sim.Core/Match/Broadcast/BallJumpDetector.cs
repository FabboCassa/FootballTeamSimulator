using System;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>One drawn ball that landed more than the bar away from the one drawn before it.</summary>
    public readonly struct BallJump
    {
        public BallJump(double fromPosition, double toPosition, float fromX, float fromY, float toX, float toY, float distanceDm)
        {
            FromPosition = fromPosition;
            ToPosition = toPosition;
            FromX = fromX;
            FromY = fromY;
            ToX = toX;
            ToY = toY;
            DistanceDm = distanceDm;
        }

        /// <summary>Stream position of the frame drawn before the jump.</summary>
        public double FromPosition { get; }

        /// <summary>Stream position of the frame the ball jumped on.</summary>
        public double ToPosition { get; }

        /// <summary>The stream tick (frame) the ball jumped on.</summary>
        public int Tick => (int)ToPosition;

        public float FromX { get; }
        public float FromY { get; }
        public float ToX { get; }
        public float ToY { get; }
        public float DistanceDm { get; }
    }

    /// <summary>
    /// The dev overlay's check on the drawn ball (spec free-ball-real-movement R9): fed the ball's
    /// point on every rendered frame, it reports each frame where the ball moved more than
    /// <see cref="ThresholdDm"/> since the last one — a lurch the eye reads as a teleport.
    ///
    /// Jumps the picture hides or means are not jumps: a frame under a cut's curtain is never
    /// compared, nor is the first clear frame after it compared with anything before it, and a
    /// frame on the other ends from the last one (the change of ends mirrors the picture) starts a
    /// new run. Presentation only, allocation-free on every frame.
    /// </summary>
    public sealed class BallJumpDetector
    {
        /// <summary>2 m: the most the ball may move between two frames rendered at 1x.</summary>
        public const float ThresholdDm = 20f;

        private bool _has;
        private bool _secondHalf;
        private double _position;
        private float _x;
        private float _y;

        /// <summary>Drops the last frame, so the next one is compared with nothing (a seek, a skip, a cut).</summary>
        public void Forget() => _has = false;

        /// <summary>
        /// Reads one rendered frame: the stream <paramref name="position"/> drawn, the ball's point on
        /// the grass in stream pitch decimetres, whether the picture is on its second-half ends, the
        /// curtain over it (0 clear) and the playback <paramref name="speed"/>. True with the jump
        /// when the ball moved more than the bar since the previous clear frame on the same ends.
        /// The bar is <see cref="ThresholdDm"/> × speed, because the renderer steps speed × the stream
        /// per rendered frame (<see cref="BroadcastPlayback.Advance"/> scales its budget by it); a
        /// shared clock ignores speed and so reads at 1.
        /// </summary>
        public bool Observe(double position, float xDm, float yDm, bool secondHalf, double curtain, int speed, out BallJump jump)
        {
            jump = default;
            if (curtain > 0.0)
            {
                _has = false;
                return false;
            }

            bool compare = _has && _secondHalf == secondHalf;
            double fromPosition = _position;
            float fromX = _x, fromY = _y;

            _has = true;
            _secondHalf = secondHalf;
            _position = position;
            _x = xDm;
            _y = yDm;

            if (!compare)
                return false;

            float dx = xDm - fromX, dy = yDm - fromY;
            float bar = ThresholdDm * Math.Max(1, speed);
            float squared = dx * dx + dy * dy;
            if (squared <= bar * bar)
                return false;

            jump = new BallJump(fromPosition, position, fromX, fromY, xDm, yDm, (float)Math.Sqrt(squared));
            return true;
        }
    }
}
