using System;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// How the top-down picture shows the ball's height (spec believable-match R12, from the
    /// stream's <see cref="PositionStream.BallZ"/>): the ball grows and is lifted up the screen off
    /// its shadow, which stays on the ground point and fades and shrinks as the ball rises. Capped
    /// at <see cref="FullHeightDm"/>, so a lob reads as high without leaving the pitch.
    /// Presentation only; pure and allocation-free, read on every repaint.
    /// </summary>
    public readonly struct BallLook
    {
        /// <summary>The height, in decimetres, at which the ball looks as high as it gets.</summary>
        public const float FullHeightDm = 100f;

        /// <summary>The ball's drawn size at <see cref="FullHeightDm"/>, as a multiple of its size on the grass.</summary>
        public const float MaxScale = 1.8f;

        /// <summary>How far up the screen the ball is drawn off its shadow at <see cref="FullHeightDm"/>, in pitch decimetres.</summary>
        public const float MaxLiftDm = 40f;

        /// <summary>The shadow's opacity on the grass and at <see cref="FullHeightDm"/>.</summary>
        public const float ShadowAlphaGround = 0.45f;
        public const float ShadowAlphaHigh = 0.15f;

        /// <summary>The shadow's size at <see cref="FullHeightDm"/>, as a multiple of the ball's size on the grass.</summary>
        public const float ShadowScaleHigh = 0.7f;

        public BallLook(float scale, float liftDm, float shadowAlpha, float shadowScale)
        {
            Scale = scale;
            LiftDm = liftDm;
            ShadowAlpha = shadowAlpha;
            ShadowScale = shadowScale;
        }

        /// <summary>The ball's drawn size, as a multiple of its size on the grass.</summary>
        public float Scale { get; }

        /// <summary>How far up the screen the ball is drawn off its ground point, in pitch decimetres.</summary>
        public float LiftDm { get; }

        public float ShadowAlpha { get; }

        /// <summary>The shadow's size, as a multiple of the ball's size on the grass.</summary>
        public float ShadowScale { get; }

        /// <summary>The look of a ball <paramref name="heightDm"/> decimetres above the grass.</summary>
        public static BallLook At(float heightDm)
        {
            float h = heightDm <= 0f ? 0f : (heightDm >= FullHeightDm ? 1f : heightDm / FullHeightDm);
            return new BallLook(
                1f + (MaxScale - 1f) * h,
                MaxLiftDm * h,
                ShadowAlphaGround + (ShadowAlphaHigh - ShadowAlphaGround) * h,
                1f + (ShadowScaleHigh - 1f) * h);
        }

        /// <summary>
        /// The ball's height at stream position <paramref name="position"/>, interpolated between
        /// frames; 0 when the stream carries no height (a replay stored before it existed).
        /// </summary>
        public static float HeightAt(int[] ballZ, double position)
        {
            if (ballZ == null || ballZ.Length == 0)
                return 0f;
            if (position <= 0)
                return ballZ[0];

            int last = ballZ.Length - 1;
            if (position >= last)
                return ballZ[last];

            int a = (int)Math.Floor(position);
            float f = (float)(position - a);
            return ballZ[a] + (ballZ[a + 1] - ballZ[a]) * f;
        }
    }
}
