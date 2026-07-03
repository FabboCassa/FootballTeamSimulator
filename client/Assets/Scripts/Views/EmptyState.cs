using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// A reusable empty-state illustration (task 6.8): a large muted vector icon (reusing the
    /// <see cref="IconKit"/> glyphs) above a short caption, centred in the available space. Gives
    /// "nothing here yet" screens a friendly visual instead of a lone line of text.
    /// Dumb helper — primitives only, no Sim.Core reference.
    /// </summary>
    public static class EmptyState
    {
        /// <summary>Builds a centred icon + message column. <paramref name="iconId"/> is an IconKit id.</summary>
        public static VisualElement Build(string iconId, string message, float iconSize = 72f)
        {
            var col = new VisualElement();
            col.style.flexGrow = 1f;
            col.style.alignItems = Align.Center;
            col.style.justifyContent = Justify.Center;
            col.style.paddingTop = UiKit.SpaceLg;
            col.style.paddingBottom = UiKit.SpaceLg;

            VisualElement icon = IconKit.Icon(iconId, iconSize, UiKit.TextMuted);
            icon.style.opacity = 0.5f;
            icon.style.marginBottom = UiKit.SpaceSm;
            col.Add(icon);

            var label = new Label(message ?? string.Empty);
            label.style.color = UiKit.TextMuted;
            label.style.fontSize = UiKit.FontSmall;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.maxWidth = 320;
            col.Add(label);

            return col;
        }
    }
}
