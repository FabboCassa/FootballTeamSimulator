using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Icon factory for the shell / navigation (task 6.6). Icons are painter2D vector glyphs
    /// (like <see cref="CrestRenderer"/>): zero image assets, crisp at any scale, tinted from
    /// the theme. The <see cref="PngOverride"/> hook lets a host swap any icon for a real
    /// image at runtime — the groundwork for the planned MOD editor (user-supplied PNGs for
    /// players/clubs/icons): when the resolver returns a texture for an id, an Image is used
    /// instead of the vector glyph. Known ids: overview, inbox, squad, tactics, training,
    /// support, market, scouting, club, career, league, continue, advance, back, exit.
    /// </summary>
    public static class IconKit
    {
        /// <summary>
        /// Optional PNG resolver (mod support): given an icon id, return a texture to use
        /// instead of the built-in vector glyph, or null to keep the vector. Set by the host
        /// (e.g. a future ModService) before screens are built.
        /// </summary>
        public static Func<string, Texture2D> PngOverride;

        /// <summary>Creates an icon element of the given square size, tinted (vector) or raw (PNG override).</summary>
        public static VisualElement Icon(string id, float size, Color tint)
        {
            Texture2D png = PngOverride?.Invoke(id);
            if (png != null)
            {
                var image = new Image { image = png, scaleMode = ScaleMode.ScaleToFit };
                image.style.width = size;
                image.style.height = size;
                return image;
            }
            return new VectorIcon(id, tint) { style = { width = size, height = size } };
        }
    }

    /// <summary>A single painter2D-drawn icon glyph, tintable via <see cref="SetTint"/>.</summary>
    public sealed class VectorIcon : VisualElement
    {
        private readonly string _id;
        private Color _tint;

        public VectorIcon(string id, Color tint)
        {
            _id = id;
            _tint = tint;
            generateVisualContent += OnGenerateVisualContent;
        }

        public void SetTint(Color tint)
        {
            _tint = tint;
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width <= 1f || r.height <= 1f)
                return;

            // Map a 100x100 design space into the element, centred, preserving aspect.
            float s = Mathf.Min(r.width, r.height) / 100f;
            float ox = r.x + (r.width - 100f * s) * 0.5f;
            float oy = r.y + (r.height - 100f * s) * 0.5f;
            Vector2 P(float x, float y) => new Vector2(ox + x * s, oy + y * s);

            Painter2D p = ctx.painter2D;
            p.strokeColor = _tint;
            p.fillColor = _tint;
            p.lineWidth = 7f * s;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            switch (_id)
            {
                case "overview": // house
                    p.BeginPath();
                    p.MoveTo(P(18, 52)); p.LineTo(P(50, 24)); p.LineTo(P(82, 52));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(30, 50)); p.LineTo(P(30, 78)); p.LineTo(P(70, 78)); p.LineTo(P(70, 50));
                    p.Stroke();
                    break;

                case "inbox": // envelope
                    p.BeginPath();
                    p.MoveTo(P(18, 30)); p.LineTo(P(82, 30)); p.LineTo(P(82, 72)); p.LineTo(P(18, 72)); p.ClosePath();
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(20, 33)); p.LineTo(P(50, 55)); p.LineTo(P(80, 33));
                    p.Stroke();
                    break;

                case "squad": // shirt
                    p.BeginPath();
                    p.MoveTo(P(38, 24)); p.LineTo(P(20, 32)); p.LineTo(P(14, 48)); p.LineTo(P(29, 53));
                    p.LineTo(P(29, 80)); p.LineTo(P(71, 80)); p.LineTo(P(71, 53)); p.LineTo(P(86, 48));
                    p.LineTo(P(80, 32)); p.LineTo(P(62, 24));
                    p.Arc(P(50, 24), 12f * s, 0f, 180f);
                    p.ClosePath();
                    p.Stroke();
                    break;

                case "tactics": // pitch with centre circle
                    p.BeginPath();
                    p.MoveTo(P(20, 25)); p.LineTo(P(80, 25)); p.LineTo(P(80, 75)); p.LineTo(P(20, 75)); p.ClosePath();
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(50, 25)); p.LineTo(P(50, 75));
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(P(50, 50), 10f * s, 0f, 360f);
                    p.Stroke();
                    break;

                case "training": // stopwatch
                    p.BeginPath();
                    p.Arc(P(50, 56), 22f * s, 0f, 360f);
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(50, 34)); p.LineTo(P(50, 26));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(41, 24)); p.LineTo(P(59, 24));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(50, 56)); p.LineTo(P(61, 46));
                    p.Stroke();
                    break;

                case "support": // speech bubble
                    p.BeginPath();
                    p.MoveTo(P(20, 28)); p.LineTo(P(80, 28)); p.LineTo(P(80, 60)); p.LineTo(P(54, 60));
                    p.LineTo(P(42, 76)); p.LineTo(P(42, 60)); p.LineTo(P(20, 60)); p.ClosePath();
                    p.Stroke();
                    break;

                case "market": // exchange arrows
                    p.BeginPath();
                    p.MoveTo(P(24, 38)); p.LineTo(P(72, 38));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(60, 27)); p.LineTo(P(74, 38)); p.LineTo(P(60, 49));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(76, 62)); p.LineTo(P(28, 62));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(40, 51)); p.LineTo(P(26, 62)); p.LineTo(P(40, 73));
                    p.Stroke();
                    break;

                case "scouting": // magnifier
                    p.BeginPath();
                    p.Arc(P(44, 44), 19f * s, 0f, 360f);
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(58, 58)); p.LineTo(P(78, 78));
                    p.Stroke();
                    break;

                case "club": // club flag
                    p.BeginPath();
                    p.MoveTo(P(34, 22)); p.LineTo(P(34, 80));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(34, 26)); p.LineTo(P(74, 34)); p.LineTo(P(34, 48)); p.ClosePath();
                    p.Fill();
                    break;

                case "career": // rosette star
                    p.BeginPath();
                    for (int i = 0; i < 5; i++)
                    {
                        float aOut = (-90f + i * 72f) * Mathf.Deg2Rad;
                        float aIn = (-54f + i * 72f) * Mathf.Deg2Rad;
                        Vector2 vOut = P(50f + 27f * Mathf.Cos(aOut), 52f + 27f * Mathf.Sin(aOut));
                        Vector2 vIn = P(50f + 12f * Mathf.Cos(aIn), 52f + 12f * Mathf.Sin(aIn));
                        if (i == 0) p.MoveTo(vOut); else p.LineTo(vOut);
                        p.LineTo(vIn);
                    }
                    p.ClosePath();
                    p.Fill();
                    break;

                case "league": // trophy
                    p.BeginPath();
                    p.MoveTo(P(32, 24)); p.LineTo(P(68, 24)); p.LineTo(P(63, 52)); p.LineTo(P(37, 52)); p.ClosePath();
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(32, 28)); p.LineTo(P(22, 30)); p.LineTo(P(26, 42)); p.LineTo(P(35, 44));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(68, 28)); p.LineTo(P(78, 30)); p.LineTo(P(74, 42)); p.LineTo(P(65, 44));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(50, 52)); p.LineTo(P(50, 66));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(36, 74)); p.LineTo(P(64, 74));
                    p.Stroke();
                    break;

                case "continue": // play triangle
                    p.BeginPath();
                    p.MoveTo(P(38, 27)); p.LineTo(P(76, 50)); p.LineTo(P(38, 73)); p.ClosePath();
                    p.Fill();
                    break;

                case "advance": // calendar with +
                    p.BeginPath();
                    p.MoveTo(P(22, 32)); p.LineTo(P(78, 32)); p.LineTo(P(78, 78)); p.LineTo(P(22, 78)); p.ClosePath();
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(36, 24)); p.LineTo(P(36, 38));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(64, 24)); p.LineTo(P(64, 38));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(50, 48)); p.LineTo(P(50, 68));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(40, 58)); p.LineTo(P(60, 58));
                    p.Stroke();
                    break;

                case "back": // chevron left
                    p.BeginPath();
                    p.MoveTo(P(60, 26)); p.LineTo(P(38, 50)); p.LineTo(P(60, 74));
                    p.Stroke();
                    break;

                case "exit": // door with out-arrow
                    p.BeginPath();
                    p.MoveTo(P(54, 22)); p.LineTo(P(28, 22)); p.LineTo(P(28, 78)); p.LineTo(P(54, 78));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(44, 50)); p.LineTo(P(80, 50));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(69, 40)); p.LineTo(P(82, 50)); p.LineTo(P(69, 60));
                    p.Stroke();
                    break;

                default: // unknown id — a neutral dot so the mistake is visible, not invisible
                    p.BeginPath();
                    p.Arc(P(50, 50), 12f * s, 0f, 360f);
                    p.Fill();
                    break;
            }
        }
    }
}
