using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// The one top-down pitch in the game (tasks 6.7 · 13.1). The formation/lineup pitch,
    /// the tactics preview and the watched match all paint through here, so there is a
    /// single set of markings and a single set of colours rather than two that drift
    /// apart. Pure presentation, no Sim.Core reference — the 1050x680 dm aspect is
    /// inlined as local constants (same numbers as Sim.Core's <c>Pitch</c>), which is why
    /// FTS.MatchView can reference FTS.Views for it without dragging the sim across.
    ///
    /// A pitch is letterboxed into whatever element hosts it (<see cref="FitRect"/>);
    /// normalized coordinates (0..1 along the length, 0..1 across the width) map into
    /// that rect via <see cref="ToPixel"/>, so tokens drawn as real VisualElements — or
    /// painted by the match renderer — line up exactly with the markings.
    /// </summary>
    public static class PitchGraphics
    {
        public const float LengthDm = 1050f;
        public const float WidthDm = 680f;

        public static readonly Color TurfColor = new Color(0.16f, 0.42f, 0.20f);
        public static readonly Color TurfStripe = new Color(0.18f, 0.46f, 0.22f);
        public static readonly Color LineColor = new Color(1f, 1f, 1f, 0.75f);
        public static readonly Color GoalColor = new Color(1f, 1f, 1f, 0.95f);

        // Real markings, in decimetres.
        private const float BoxDepth = 165f;        // penalty area, 16.5 m
        private const float BoxHalf = 201.5f;       // 40.3 m wide
        private const float GoalAreaDepth = 55f;    // 6-yard box, 5.5 m
        private const float GoalAreaHalf = 91.6f;   // 18.32 m wide
        private const float PenaltySpot = 110f;     // 11 m
        private const float CircleRadius = 91.5f;   // 9.15 m
        private const float CornerRadius = 10f;     // 1 m
        private const float GoalHalf = 37f;         // 7.32 m wide

        /// <summary>The letterboxed pitch rectangle (aspect 1050:680) centred in a w x h box.</summary>
        public static Rect FitRect(float w, float h)
        {
            float scale = Mathf.Min(w / LengthDm, h / WidthDm);
            float pw = LengthDm * scale;
            float ph = WidthDm * scale;
            return new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        }

        /// <summary>Maps a normalized point into the pitch rect. mirror flips the length (attack left).</summary>
        public static Vector2 ToPixel(Rect fit, float nx, float ny, bool mirror)
        {
            float x = mirror ? 1f - nx : nx;
            return new Vector2(fit.x + x * fit.width, fit.y + ny * fit.height);
        }

        /// <summary>Maps a point in Sim.Core decimetres into the pitch rect.</summary>
        public static Vector2 ToPixelDm(Rect fit, float xDm, float yDm) =>
            new Vector2(fit.x + xDm / LengthDm * fit.width, fit.y + yDm / WidthDm * fit.height);

        /// <summary>Paints turf + markings into <paramref name="fit"/> (call from OnGenerateVisualContent).</summary>
        public static void Draw(Painter2D p, Rect fit)
        {
            if (fit.width <= 1f || fit.height <= 1f)
                return;

            float x0 = fit.x, y0 = fit.y, x1 = fit.xMax, y1 = fit.yMax;
            float midX = fit.x + fit.width * 0.5f, midY = fit.y + fit.height * 0.5f;
            float scale = fit.width / LengthDm;

            // Turf.
            p.fillColor = TurfColor;
            FillRect(p, x0, y0, x1, y1);

            // Mowing stripes, for a bit of richness (cheap, purely cosmetic).
            p.fillColor = TurfStripe;
            float stripe = fit.width / 8f;
            for (float sx = x0 + stripe; sx < x1 - 0.5f; sx += stripe * 2f)
                FillRect(p, sx, y0, Mathf.Min(sx + stripe, x1), y1);

            // Markings.
            p.strokeColor = LineColor;
            p.lineWidth = Mathf.Max(1.5f, 2f * scale);

            StrokeRect(p, x0, y0, x1, y1);                        // outline
            Line(p, midX, y0, midX, y1);                          // halfway line
            Circle(p, midX, midY, CircleRadius * scale);          // centre circle
            Dot(p, midX, midY, Mathf.Max(1.5f, 3f * scale));      // centre spot

            for (int end = 0; end < 2; end++)
            {
                bool left = end == 0;
                float goalLine = left ? x0 : x1;
                float inward = left ? 1f : -1f;

                StrokeRect(p,
                    goalLine, midY - BoxHalf * scale,
                    goalLine + inward * BoxDepth * scale, midY + BoxHalf * scale);

                StrokeRect(p,
                    goalLine, midY - GoalAreaHalf * scale,
                    goalLine + inward * GoalAreaDepth * scale, midY + GoalAreaHalf * scale);

                float spotX = goalLine + inward * PenaltySpot * scale;
                Dot(p, spotX, midY, Mathf.Max(1.5f, 3f * scale));

                // The D: the slice of the centre-circle-radius arc that falls outside the box.
                float sweep = 53f;
                if (left) Arc(p, spotX, midY, CircleRadius * scale, -sweep, sweep);
                else Arc(p, spotX, midY, CircleRadius * scale, 180f - sweep, 180f + sweep);

                // Corner arcs.
                Arc(p, goalLine, y0, CornerRadius * scale, left ? 0f : 90f, left ? 90f : 180f);
                Arc(p, goalLine, y1, CornerRadius * scale, left ? 270f : 180f, left ? 360f : 270f);

                // Goal mouth: a heavier stroke sitting on the line.
                p.strokeColor = GoalColor;
                p.lineWidth = Mathf.Max(2.5f, 5f * scale);
                Line(p, goalLine, midY - GoalHalf * scale, goalLine, midY + GoalHalf * scale);
                p.strokeColor = LineColor;
                p.lineWidth = Mathf.Max(1.5f, 2f * scale);
            }
        }

        private static void FillRect(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Fill();
        }

        private static void StrokeRect(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Stroke();
        }

        private static void Line(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y1));
            p.Stroke();
        }

        private static void Circle(Painter2D p, float cx, float cy, float radius)
        {
            p.BeginPath();
            p.Arc(new Vector2(cx, cy), radius, 0f, 360f);
            p.Stroke();
        }

        private static void Arc(Painter2D p, float cx, float cy, float radius, float from, float to)
        {
            p.BeginPath();
            p.Arc(new Vector2(cx, cy), radius, from, to);
            p.Stroke();
        }

        private static void Dot(Painter2D p, float cx, float cy, float radius)
        {
            Color stroke = p.strokeColor;
            p.fillColor = stroke;
            p.BeginPath();
            p.Arc(new Vector2(cx, cy), radius, 0f, 360f);
            p.Fill();
        }
    }
}
