using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// A procedural "portrait" placeholder for a player until real art (task 6.8): a rounded
    /// disc filled with his club's kit colours and stamped with his initials. Deterministic —
    /// the colours come from the club identity (task 6.1) and the initials from his name, so the
    /// same player always looks the same, and squad-mates share a kit while reading distinctly.
    ///
    /// Dumb view: primitives only (size + Unity colours + initials), NO Sim.Core reference — the
    /// presenter maps a club's <c>ClubVisual</c> onto these arguments. Cheap (one VisualElement +
    /// a Label, no painter2D) so it scales to long roster/market lists without GC churn.
    /// </summary>
    public sealed class PlayerAvatarView : VisualElement
    {
        public PlayerAvatarView(float size, Color fill, Color ring, Color text, string initials)
        {
            style.width = size;
            style.height = size;
            style.backgroundColor = fill;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            style.flexShrink = 0f;
            pickingMode = PickingMode.Ignore;
            UiKit.Round(this, size * 0.5f);
            UiKit.SetBorder(this, ring, Mathf.Max(1.5f, size * 0.06f));

            var label = new Label(Clean(initials));
            label.style.color = text;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = size * 0.42f;
            label.pickingMode = PickingMode.Ignore;
            Add(label);
        }

        /// <summary>Up to two uppercase initials from a "First Last" name.</summary>
        public static string InitialsOf(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "?";

            string[] parts = fullName.Trim().Split(' ');
            if (parts.Length == 1)
                return parts[0].Substring(0, System.Math.Min(2, parts[0].Length)).ToUpperInvariant();

            string first = parts[0];
            string last = parts[parts.Length - 1];
            return (char.ToUpperInvariant(first[0]).ToString() + char.ToUpperInvariant(last[0])).ToUpperInvariant();
        }

        private static string Clean(string initials)
        {
            if (string.IsNullOrEmpty(initials)) return "?";
            string s = initials.Trim().ToUpperInvariant();
            return s.Length > 2 ? s.Substring(0, 2) : s;
        }
    }
}
