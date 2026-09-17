using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Shared condition indicators (task 4.2, redrawn in 14.4): a fitness bar with its number,
    /// then two small lettered pills — F (form) and M (morale) — tinted green / neutral / red.
    /// The 4.2 version printed an arrow glyph and an ASCII face (":|") in a 12px font: the glyphs
    /// are not in the game's fonts and the face read as noise. The presenter still hands over the
    /// same form arrow and morale face strings; this only decides a tone from them, so nothing
    /// upstream changed. The caller owns the row (layout, click, and the "why" tooltip).
    /// </summary>
    internal static class ConditionStrip
    {
        /// <summary>Appends [fitness bar + number][F pill][M pill] to <paramref name="row"/>.</summary>
        public static void Append(VisualElement row, string formArrow, string moraleFace, int fitness)
        {
            var strip = new VisualElement();
            strip.AddToClassList("fts-cond");
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.alignItems = Align.Center;
            strip.style.flexShrink = 0f;
            strip.pickingMode = PickingMode.Ignore;

            strip.Add(FitnessBar(fitness));
            strip.Add(Pill("F", FormTone(formArrow)));
            strip.Add(Pill("M", MoraleTone(moraleFace)));
            row.Add(strip);
        }

        /// <summary>+1 good, 0 steady, -1 poor, read off the arrow glyphs ConditionDisplay emits.</summary>
        public static int FormTone(string arrow)
        {
            if (string.IsNullOrEmpty(arrow)) return 0;
            if (arrow.IndexOf('▲') >= 0) return 1;   // ▲
            if (arrow.IndexOf('▼') >= 0) return -1;  // ▼
            return 0;
        }

        /// <summary>+1 happy, 0 settled, -1 low, read off the faces ConditionDisplay emits.</summary>
        public static int MoraleTone(string face)
        {
            if (string.IsNullOrEmpty(face)) return 0;
            if (face.Contains("(")) return -1;
            if (face.Contains(")") || face.Contains("D")) return 1;
            return 0;
        }

        private static VisualElement FitnessBar(int fitness)
        {
            int clamped = fitness < 0 ? 0 : (fitness > 100 ? 100 : fitness);

            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Row;
            wrap.style.alignItems = Align.Center;
            wrap.pickingMode = PickingMode.Ignore;

            var track = new VisualElement();
            track.AddToClassList("fts-cond__bar");
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            if (!UiKit.StylesLoaded)
            {
                track.style.width = 46;
                track.style.height = 8;
                track.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            }

            var fill = new VisualElement();
            fill.AddToClassList("fts-cond__fill");
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(clamped);
            fill.style.backgroundColor = clamped >= 70 ? UiKit.Positive : clamped >= 40 ? UiKit.Warning : UiKit.Danger;
            fill.pickingMode = PickingMode.Ignore;
            track.Add(fill);
            wrap.Add(track);

            var number = new Label(clamped.ToString());
            number.AddToClassList("fts-cond__num");
            number.pickingMode = PickingMode.Ignore;
            wrap.Add(number);
            return wrap;
        }

        private static Label Pill(string letter, int tone)
        {
            var pill = new Label(letter);
            pill.AddToClassList("fts-cond__pill");
            pill.AddToClassList(tone > 0 ? "fts-cond__pill--good" : tone < 0 ? "fts-cond__pill--bad" : "fts-cond__pill--even");
            pill.style.unityTextAlign = TextAnchor.MiddleCenter;
            pill.pickingMode = PickingMode.Ignore;
            if (!UiKit.StylesLoaded)
            {
                pill.style.width = 22;
                pill.style.height = 22;
                pill.style.marginLeft = 6;
                pill.style.backgroundColor = tone > 0 ? UiKit.Positive : tone < 0 ? UiKit.Danger : UiKit.TagSurface;
            }
            return pill;
        }
    }
}
