using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// The client's design-token theme + component factory (task 6.1 art pass). The whole UI is
    /// code-built UI Toolkit, so this is the single home for the cartoon look: a colour palette,
    /// a type scale, spacing and corner-radius tokens, and factory methods (<see cref="Screen"/>,
    /// <see cref="Title"/>, <see cref="Subtitle"/>, <see cref="MenuButton"/>, <see cref="Card"/>,
    /// <see cref="PrimaryButton"/>, <see cref="Pill"/>, <see cref="ProgressBar"/> …). Every screen
    /// funnels through these, so restyling here restyles the whole game at once.
    ///
    /// The historical members (<see cref="MenuGreen"/>, <see cref="HubBlue"/>, <see cref="PanelGray"/>,
    /// <see cref="Screen"/>, <see cref="Title"/>, <see cref="Subtitle"/>, <see cref="MenuButton"/>)
    /// keep their names and signatures so existing screens compile unchanged and inherit the new
    /// theme for free.
    /// </summary>
    public static class UiKit
    {
        // ---------------------------------------------------------------- palette tokens
        /// <summary>App background — deep, slightly warm navy.</summary>
        public static readonly Color Background = Hex(0x141C30);
        /// <summary>A raised surface / panel.</summary>
        public static readonly Color Surface = Hex(0x1E2A44);
        /// <summary>A lighter surface (rows, secondary buttons).</summary>
        public static readonly Color SurfaceAlt = Hex(0x2A3A5C);
        /// <summary>Hairline border on cards / inputs.</summary>
        public static readonly Color Border = Hex(0x3A4D74);

        /// <summary>Brand accent — friendly emerald (CTAs, highlights).</summary>
        public static readonly Color Accent = Hex(0x27C281);
        /// <summary>Accent pressed/darker shade.</summary>
        public static readonly Color AccentDark = Hex(0x1C9B66);
        /// <summary>Secondary accent — warm amber (badges, attention).</summary>
        public static readonly Color Amber = Hex(0xF2B33D);

        public static readonly Color TextPrimary = Hex(0xF4F7FB);
        public static readonly Color TextMuted = new Color(0.96f, 0.97f, 0.99f, 0.62f);
        /// <summary>Legible text on top of the Accent fill.</summary>
        public static readonly Color TextOnAccent = Hex(0x0C2419);

        public static readonly Color Positive = Hex(0x6FCF6B);
        public static readonly Color Warning = Hex(0xE6C75A);
        public static readonly Color Danger = Hex(0xE0675C);

        // ---------------------------------------------------------------- legacy aliases (kept so existing screens compile + re-theme)
        /// <summary>Main-menu backdrop — a rich pitch green.</summary>
        public static readonly Color MenuGreen = Hex(0x123A23);
        /// <summary>Hub backdrop (re-pointed to the themed background).</summary>
        public static readonly Color HubBlue = Background;
        /// <summary>Panel colour used across screens (re-pointed to the themed surface).</summary>
        public static readonly Color PanelGray = Surface;

        // ---------------------------------------------------------------- scale tokens
        public const int RadiusSm = 8;
        public const int RadiusMd = 14;
        public const int RadiusLg = 20;
        public const int SpaceXs = 4;
        public const int SpaceSm = 8;
        public const int SpaceMd = 16;
        public const int SpaceLg = 24;

        public const int FontTitle = 40;
        public const int FontHeader = 26;
        public const int FontBody = 18;
        public const int FontSmall = 14;

        // ---------------------------------------------------------------- containers

        /// <summary>
        /// Full-screen centered column container, themed background. Backed by a vertical
        /// <see cref="ScrollView"/> so a screen taller than the viewport scrolls instead of
        /// clipping its top/bottom (Roadmap 6.3 — WebGL/small windows). The content container
        /// keeps flexGrow + centred justification, so a SHORT screen still sits centred while a
        /// TALL one grows past the viewport and becomes scrollable. ScrollView derives from
        /// VisualElement and .Add() targets its content container, so existing call sites
        /// (Root = UiKit.Screen(...); Root.Add(...)) are unchanged.
        /// </summary>
        public static VisualElement Screen(Color background)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.backgroundColor = background;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            VisualElement content = scroll.contentContainer;
            content.style.flexGrow = 1f;
            content.style.alignItems = Align.Center;
            content.style.justifyContent = Justify.Center;
            content.style.paddingLeft = SpaceMd;
            content.style.paddingRight = SpaceMd;
            content.style.paddingTop = SpaceLg;
            content.style.paddingBottom = SpaceLg;
            return scroll;
        }

        /// <summary>A rounded surface card with a hairline border.</summary>
        public static VisualElement Card()
        {
            var e = new VisualElement();
            e.style.backgroundColor = Surface;
            e.style.paddingLeft = SpaceMd;
            e.style.paddingRight = SpaceMd;
            e.style.paddingTop = SpaceMd;
            e.style.paddingBottom = SpaceMd;
            e.style.marginTop = SpaceSm;
            e.style.marginBottom = SpaceSm;
            Round(e, RadiusMd);
            SetBorder(e, Border, 1);
            return e;
        }

        /// <summary>A horizontal row (flex-row, centred items) for list entries.</summary>
        public static VisualElement Row()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            return e;
        }

        /// <summary>A thin divider line.</summary>
        public static VisualElement Divider()
        {
            var e = new VisualElement();
            e.style.height = 1;
            e.style.marginTop = SpaceSm;
            e.style.marginBottom = SpaceSm;
            e.style.backgroundColor = Border;
            return e;
        }

        // ---------------------------------------------------------------- text

        public static Label Title(string text)
        {
            var label = new Label(text);
            label.style.fontSize = FontTitle;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = TextPrimary;
            label.style.marginBottom = SpaceLg;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }

        /// <summary>Section header — smaller than a Title, still bold.</summary>
        public static Label Header(string text)
        {
            var label = new Label(text);
            label.style.fontSize = FontHeader;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = TextPrimary;
            label.style.marginBottom = SpaceSm;
            return label;
        }

        public static Label Subtitle(string text)
        {
            var label = new Label(text);
            label.style.fontSize = FontBody;
            label.style.color = TextMuted;
            label.style.marginBottom = SpaceMd;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }

        /// <summary>A small caption / helper line.</summary>
        public static Label Caption(string text)
        {
            var label = new Label(text);
            label.style.fontSize = FontSmall;
            label.style.color = TextMuted;
            return label;
        }

        // ---------------------------------------------------------------- buttons

        /// <summary>The standard navigation/menu button — rounded surface pill, bold label.</summary>
        public static Button MenuButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.width = 320;
            button.style.height = 54;
            button.style.fontSize = FontBody;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.color = TextPrimary;
            button.style.backgroundColor = SurfaceAlt;
            button.style.marginTop = 5;
            button.style.marginBottom = 5;
            ClearButtonChrome(button);
            Round(button, RadiusMd);
            SetBorder(button, Border, 1);
            return button;
        }

        /// <summary>A primary call-to-action — accent fill, dark legible label.</summary>
        public static Button PrimaryButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 54;
            button.style.fontSize = FontBody;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.color = TextOnAccent;
            button.style.backgroundColor = Accent;
            button.style.marginTop = 5;
            button.style.marginBottom = 5;
            button.style.paddingLeft = SpaceLg;
            button.style.paddingRight = SpaceLg;
            ClearButtonChrome(button);
            Round(button, RadiusMd);
            return button;
        }

        // ---------------------------------------------------------------- small components

        /// <summary>A small rounded label chip (status / tag).</summary>
        public static Label Pill(string text, Color background, Color textColor)
        {
            var label = new Label(text);
            label.style.fontSize = FontSmall;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = textColor;
            label.style.backgroundColor = background;
            label.style.paddingLeft = SpaceSm;
            label.style.paddingRight = SpaceSm;
            label.style.paddingTop = 2;
            label.style.paddingBottom = 2;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            Round(label, RadiusSm);
            return label;
        }

        /// <summary>A simple [track][fill] horizontal bar, value in [0,1].</summary>
        public static VisualElement ProgressBar(float value01, Color fill, float width = 120f, float height = 10f)
        {
            float v = value01 < 0f ? 0f : (value01 > 1f ? 1f : value01);

            var track = new VisualElement();
            track.style.width = width;
            track.style.height = height;
            track.style.backgroundColor = new Color(0f, 0f, 0f, 0.40f);
            Round(track, RadiusSm);

            var bar = new VisualElement();
            bar.style.height = height;
            bar.style.width = Length.Percent(v * 100f);
            bar.style.backgroundColor = fill;
            Round(bar, RadiusSm);
            track.Add(bar);
            return track;
        }

        // ---------------------------------------------------------------- style helpers

        /// <summary>Sets all four corner radii.</summary>
        public static void Round(VisualElement e, float radius)
        {
            e.style.borderTopLeftRadius = radius;
            e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = radius;
            e.style.borderBottomRightRadius = radius;
        }

        /// <summary>Sets a uniform border colour + width on all four sides.</summary>
        public static void SetBorder(VisualElement e, Color color, float width)
        {
            e.style.borderTopWidth = width;
            e.style.borderRightWidth = width;
            e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width;
            e.style.borderTopColor = color;
            e.style.borderRightColor = color;
            e.style.borderBottomColor = color;
            e.style.borderLeftColor = color;
        }

        /// <summary>Removes the default Unity Button background tint so our colours show cleanly.</summary>
        private static void ClearButtonChrome(Button button)
        {
            button.style.borderTopWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
        }

        /// <summary>0xRRGGBB → opaque Color.</summary>
        public static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
