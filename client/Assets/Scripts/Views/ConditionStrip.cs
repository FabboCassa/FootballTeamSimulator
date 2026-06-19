using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Shared condition indicators (task 4.2): a fitness bar, a form arrow and a
    /// morale face, appended to a row. Used by both the Squad screen and the in-match
    /// pause panel so the iconography is identical everywhere. The caller owns the row
    /// (layout, click, and the why-tooltip); this only paints the strip.
    /// </summary>
    internal static class ConditionStrip
    {
        private static readonly Color BarTrackColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color FitnessGood = new Color(0.45f, 0.80f, 0.45f);
        private static readonly Color FitnessMid = new Color(0.90f, 0.78f, 0.35f);
        private static readonly Color FitnessLow = new Color(0.88f, 0.40f, 0.36f);

        /// <summary>Appends [fitness bar][form arrow][morale face] to <paramref name="row"/>.</summary>
        public static void Append(VisualElement row, string formArrow, string moraleFace, int fitness)
        {
            row.Add(FitnessBar(fitness));
            row.Add(Glyph(formArrow, 26, 13));
            row.Add(Glyph(moraleFace, 32, 12));
        }

        private static VisualElement FitnessBar(int fitness)
        {
            int clamped = fitness < 0 ? 0 : (fitness > 100 ? 100 : fitness);

            var track = new VisualElement();
            track.style.width = 46;
            track.style.height = 8;
            track.style.marginRight = 8;
            track.style.backgroundColor = BarTrackColor;
            track.style.borderTopLeftRadius = 3;
            track.style.borderTopRightRadius = 3;
            track.style.borderBottomLeftRadius = 3;
            track.style.borderBottomRightRadius = 3;

            var fill = new VisualElement();
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(clamped);
            fill.style.backgroundColor = clamped >= 70 ? FitnessGood : clamped >= 40 ? FitnessMid : FitnessLow;
            fill.style.borderTopLeftRadius = 3;
            fill.style.borderBottomLeftRadius = 3;
            track.Add(fill);

            return track;
        }

        private static Label Glyph(string text, float width, int fontSize)
        {
            var label = new Label(text ?? string.Empty);
            label.style.width = width;
            label.style.fontSize = fontSize;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }
    }
}
