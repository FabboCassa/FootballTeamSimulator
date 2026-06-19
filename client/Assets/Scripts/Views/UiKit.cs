using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Code-built styling helpers for the placeholder screens of task 2.1.
    /// Proper UXML/USS assets arrive with the real screens (2.4+); keeping the
    /// placeholders in code avoids editor asset wiring at this stage.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color MenuGreen = new Color(0.07f, 0.32f, 0.15f);
        public static readonly Color HubBlue = new Color(0.10f, 0.16f, 0.28f);
        public static readonly Color PanelGray = new Color(0.15f, 0.15f, 0.18f);

        /// <summary>Full-screen centered column container.</summary>
        public static VisualElement Screen(Color background)
        {
            var e = new VisualElement();
            e.style.flexGrow = 1f;
            e.style.alignItems = Align.Center;
            e.style.justifyContent = Justify.Center;
            e.style.backgroundColor = background;
            return e;
        }

        public static Label Title(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 40;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = Color.white;
            label.style.marginBottom = 24;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }

        public static Label Subtitle(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 18;
            label.style.color = new Color(1f, 1f, 1f, 0.7f);
            label.style.marginBottom = 16;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }

        public static Button MenuButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.width = 300;
            button.style.height = 56;
            button.style.fontSize = 20;
            button.style.marginTop = 6;
            button.style.marginBottom = 6;
            return button;
        }
    }
}
