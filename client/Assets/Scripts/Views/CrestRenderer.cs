using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Draws a procedural club crest with the UI Toolkit painter2D vector API (task 6.1 art pass).
    /// Pure presentation, primitives only (shape/pattern ints + Unity colours + initials) so it
    /// stays in FTS.Views with NO Sim.Core reference — the presenter maps a club's generated
    /// <c>CrestDesign</c> (Sim.Core) onto these arguments via <c>ClubVisual</c>.
    ///
    /// The two fill colours are arranged by <paramref name="pattern"/> across the crest's bounding
    /// box (which may overflow the silhouette), then an even-odd "frame mask" painted in
    /// <paramref name="mask"/> (the colour of the surface the crest sits on) covers everything
    /// outside the silhouette — so any pattern is cleanly clipped to the shape without needing a
    /// real clip region. A trim stroke outlines the silhouette and the club initials sit on top.
    ///
    /// Shape codes match Sim.Core CrestShape: 0 Shield · 1 Circle · 2 Diamond · 3 RoundedSquare.
    /// Pattern codes match Sim.Core CrestPattern: 0 Solid · 1 VerticalHalves · 2 HorizontalHalves
    /// · 3 DiagonalSash · 4 VerticalStripes · 5 Hoops · 6 Quarters.
    /// </summary>
    public sealed class CrestRenderer : VisualElement
    {
        private readonly int _shape;
        private readonly int _pattern;
        private readonly Color _fillA;
        private readonly Color _fillB;
        private readonly Color _trim;
        private readonly Color _mask;
        private readonly float _pad;

        public CrestRenderer(float size, int shape, int pattern,
                             Color fillA, Color fillB, Color trim, Color emblem, Color mask,
                             string initials)
        {
            _shape = shape;
            _pattern = pattern;
            _fillA = fillA;
            _fillB = fillB;
            _trim = trim;
            _mask = mask;
            _pad = size * 0.06f;

            style.width = size;
            style.height = size;
            generateVisualContent += OnGenerate;

            // Initials sit on top of the painter2D content (children render above it).
            var label = new Label(Clean(initials));
            label.style.position = Position.Absolute;
            label.style.left = 0;
            label.style.right = 0;
            label.style.top = 0;
            label.style.bottom = 0;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = size * 0.34f;
            label.style.color = emblem;
            Add(label);

            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        private static string Clean(string initials)
        {
            if (string.IsNullOrEmpty(initials)) return string.Empty;
            string s = initials.Trim().ToUpperInvariant();
            return s.Length > 3 ? s.Substring(0, 3) : s;
        }

        private void OnGenerate(MeshGenerationContext mgc)
        {
            Rect r = contentRect;
            if (r.width <= 1f || r.height <= 1f) return;

            float side = Mathf.Min(r.width, r.height) - 2f * _pad;
            if (side <= 1f) return;
            float cx = r.width * 0.5f;
            float cy = r.height * 0.5f;
            float x0 = cx - side * 0.5f, y0 = cy - side * 0.5f;
            float x1 = cx + side * 0.5f, y1 = cy + side * 0.5f;

            Painter2D p = mgc.painter2D;

            // 1) Pattern fills across the bounding box (may overflow the silhouette).
            PaintPattern(p, x0, y0, x1, y1);

            // 2) Frame mask: everything outside the silhouette becomes the surface colour.
            p.fillColor = _mask;
            p.BeginPath();
            RectSub(p, 0f, 0f, r.width, r.height); // outer
            ShapeSub(p, x0, y0, x1, y1, cx, cy, side); // inner (the hole)
            p.Fill(FillRule.OddEven);

            // 3) Trim outline around the silhouette.
            p.strokeColor = _trim;
            p.lineWidth = Mathf.Max(2f, side * 0.05f);
            p.BeginPath();
            ShapeSub(p, x0, y0, x1, y1, cx, cy, side);
            p.Stroke();
        }

        // ------------------------------------------------------------- patterns

        private void PaintPattern(Painter2D p, float x0, float y0, float x1, float y1)
        {
            float w = x1 - x0, h = y1 - y0;
            switch (_pattern)
            {
                case 1: // VerticalHalves
                    FillRect(p, x0, y0, x0 + w * 0.5f, y1, _fillA);
                    FillRect(p, x0 + w * 0.5f, y0, x1, y1, _fillB);
                    break;
                case 2: // HorizontalHalves
                    FillRect(p, x0, y0, x1, y0 + h * 0.5f, _fillA);
                    FillRect(p, x0, y0 + h * 0.5f, x1, y1, _fillB);
                    break;
                case 3: // DiagonalSash
                    FillRect(p, x0, y0, x1, y1, _fillA);
                    FillPoly(p, _fillB,
                        new Vector2(x0, y1 - h * 0.34f), new Vector2(x1 - w * 0.34f, y0),
                        new Vector2(x1, y0 + h * 0.34f), new Vector2(x0 + w * 0.34f, y1));
                    break;
                case 4: // VerticalStripes
                {
                    const int n = 5;
                    float sw = w / n;
                    for (int i = 0; i < n; i++)
                        FillRect(p, x0 + i * sw, y0, x0 + (i + 1) * sw, y1, (i % 2 == 0) ? _fillA : _fillB);
                    break;
                }
                case 5: // Hoops
                {
                    const int n = 5;
                    float sh = h / n;
                    for (int i = 0; i < n; i++)
                        FillRect(p, x0, y0 + i * sh, x1, y0 + (i + 1) * sh, (i % 2 == 0) ? _fillA : _fillB);
                    break;
                }
                case 6: // Quarters
                    FillRect(p, x0, y0, x0 + w * 0.5f, y0 + h * 0.5f, _fillA);
                    FillRect(p, x0 + w * 0.5f, y0, x1, y0 + h * 0.5f, _fillB);
                    FillRect(p, x0, y0 + h * 0.5f, x0 + w * 0.5f, y1, _fillB);
                    FillRect(p, x0 + w * 0.5f, y0 + h * 0.5f, x1, y1, _fillA);
                    break;
                default: // Solid
                    FillRect(p, x0, y0, x1, y1, _fillA);
                    break;
            }
        }

        // ------------------------------------------------------------- shape subpaths

        /// <summary>Traces the silhouette as a closed subpath (no Begin/Fill — caller owns those).</summary>
        private void ShapeSub(Painter2D p, float x0, float y0, float x1, float y1, float cx, float cy, float side)
        {
            switch (_shape)
            {
                case 1: // Circle
                    p.MoveTo(new Vector2(cx + side * 0.5f, cy));
                    p.Arc(new Vector2(cx, cy), side * 0.5f, 0f, 360f);
                    p.ClosePath();
                    break;
                case 2: // Diamond
                    p.MoveTo(new Vector2(cx, y0));
                    p.LineTo(new Vector2(x1, cy));
                    p.LineTo(new Vector2(cx, y1));
                    p.LineTo(new Vector2(x0, cy));
                    p.ClosePath();
                    break;
                case 3: // RoundedSquare (drawn as a square box)
                    RectSub(p, x0, y0, x1, y1);
                    break;
                default: // Shield (flat top, pointed bottom)
                    float shoulder = y0 + side * 0.52f;
                    p.MoveTo(new Vector2(x0, y0));
                    p.LineTo(new Vector2(x1, y0));
                    p.LineTo(new Vector2(x1, shoulder));
                    p.LineTo(new Vector2(cx, y1));
                    p.LineTo(new Vector2(x0, shoulder));
                    p.ClosePath();
                    break;
            }
        }

        // ------------------------------------------------------------- low-level

        private static void RectSub(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
        }

        private static void FillRect(Painter2D p, float x0, float y0, float x1, float y1, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            RectSub(p, x0, y0, x1, y1);
            p.Fill();
        }

        private static void FillPoly(Painter2D p, Color c, params Vector2[] pts)
        {
            if (pts.Length < 3) return;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.Fill();
        }
    }
}
