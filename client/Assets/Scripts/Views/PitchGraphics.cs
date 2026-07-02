using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Shared top-down pitch drawing (task 6.7). Mirrors the pitch turf + markings
    /// painted by the 3.1 <c>MatchRenderer</c> so the formation/lineup pitch and the
    /// watched-match pitch look identical, without crossing the FTS.MatchView asmdef
    /// boundary. Pure presentation, no Sim.Core reference — the 1050x680 dm aspect
    /// is inlined as local constants (same numbers as Sim.Core's <c>Pitch</c>).
    ///
    /// A pitch is letterboxed into whatever element hosts it (<see cref="FitRect"/>);
    /// normalized coordinates (0..1 along the length, 0..1 across the width) map into
    /// that rect via <see cref="ToPixel"/>, so tokens drawn as real VisualElements line
    /// up exactly with the painted markings.
    /// </summary>
    internal static class PitchGraphics
    {
        public const float LengthDm = 1050f;
        public const float WidthDm = 680f;

        private static readonly Color TurfColor = new Color(0.16f, 0.42f, 0.20f);
        private static readonly Color TurfStripe = new Color(0.18f, 0.46f, 0.22f);
        private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.75f);

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

            // A couple of mowing stripes for a bit of richness (cheap, purely cosmetic).
            p.fillColor = TurfStripe;
            float stripe = fit.width / 6f;
            for (float sx = x0 + stripe; sx < x1 - 0.5f; sx += stripe * 2f)
                FillRect(p, sx, y0, Mathf.Min(sx + stripe, x1), y1);

            // Markings.
            p.strokeColor = LineColor;
            p.lineWidth = Mathf.Max(1.5f, 2f * scale);

            StrokeRect(p, x0, y0, x1, y1);                       // outline
            Line(p, midX, y0, midX, y1);                         // halfway line
            Circle(p, midX, midY, 91.5f * scale);                // centre circle (9.15 m)

            // Penalty boxes (16.5 m deep, 40.3 m wide).
            float boxDepth = 165f * scale;
            float boxHalf = 201.5f * scale;
            StrokeRect(p, x0, midY - boxHalf, x0 + boxDepth, midY + boxHalf);
            StrokeRect(p, x1 - boxDepth, midY - boxHalf, x1, midY + boxHalf);
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
    }
}
