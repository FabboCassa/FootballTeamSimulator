using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// The client's design system bridge (tasks 6.1 art pass + 6.6 design system).
    /// Tokens and component styling live in <c>Resources/FtsTheme.uss</c> (loaded once by
    /// <see cref="Attach"/> onto the UIDocument root); the factory methods here create
    /// elements and assign the matching <c>.fts-*</c> classes — views stay code-built C#
    /// (ARCHITECTURE §5.3) while the look is centralized in the stylesheet (hover/active
    /// pseudo-states for free). If the stylesheet fails to load, a compact inline fallback
    /// keeps the UI legible and logs an error so the problem is visible in playtests.
    ///
    /// The C# colour constants remain the single source for DYNAMIC tinting from code
    /// (per-club accents, condition bars, category pills) and mirror the USS tokens —
    /// keep both in sync when retuning the palette.
    /// </summary>
    public static class UiKit
    {
        // ---------------------------------------------------------------- palette tokens (mirror FtsTheme.uss)
        public static readonly Color Background = Hex(0x141C30);
        public static readonly Color Surface = Hex(0x1E2A44);
        public static readonly Color SurfaceAlt = Hex(0x2A3A5C);
        public static readonly Color Border = Hex(0x3A4D74);

        public static readonly Color Accent = Hex(0x27C281);
        public static readonly Color AccentDark = Hex(0x1C9B66);
        public static readonly Color Amber = Hex(0xF2B33D);

        public static readonly Color TextPrimary = Hex(0xF4F7FB);
        public static readonly Color TextMuted = new Color(0.96f, 0.97f, 0.99f, 0.62f);
        public static readonly Color TextOnAccent = Hex(0x0C2419);

        public static readonly Color Positive = Hex(0x6FCF6B);
        public static readonly Color Warning = Hex(0xE6C75A);
        public static readonly Color Danger = Hex(0xE0675C);

        // ---------------------------------------------------------------- legacy aliases (kept so existing screens compile + re-theme)
        /// <summary>Task 6.6: re-pointed to the themed navy — the green main menu clashed with the rest of the app.</summary>
        public static readonly Color MenuGreen = Hex(0x141C30);
        public static readonly Color HubBlue = Background;
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

        /// <summary>Minimum comfortable touch-target edge in UI points (Roadmap 6.4).</summary>
        public const int MinTouchPx = 48;

        // ---------------------------------------------------------------- stylesheet attach (task 6.6)

        private static StyleSheet _theme;
        private static bool _loadAttempted;

        /// <summary>True once the USS theme has been loaded and attached; factories then rely on classes.</summary>
        public static bool StylesLoaded { get; private set; }

        /// <summary>
        /// Loads Resources/FtsTheme.uss and attaches it to <paramref name="root"/> (the UIDocument
        /// root — overlays share the same root so dialogs/toasts are themed too). Call once at startup.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            if (!_loadAttempted)
            {
                _loadAttempted = true;
                _theme = Resources.Load<StyleSheet>("FtsTheme");
                StylesLoaded = _theme != null;
                if (!StylesLoaded)
                    Debug.LogError("[UiKit] Resources/FtsTheme.uss missing — using inline fallback styling.");
            }

            root.AddToClassList("fts-root");
            if (_theme != null && !root.styleSheets.Contains(_theme))
                root.styleSheets.Add(_theme);
        }

        // ---------------------------------------------------------------- containers

        /// <summary>
        /// Full-screen centered column container, themed background. Backed by a vertical
        /// ScrollView so a screen taller than the viewport scrolls instead of clipping
        /// (6.3 stop-gap; the shell content host from 6.6 sizes it to the viewport).
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

        /// <summary>
        /// Full-height page container WITHOUT an outer scroller (task 6.6): content starts at the
        /// top and any inner list should flex-grow and scroll itself. Use this instead of
        /// <see cref="Screen"/> for screens that own an internal ScrollView — nesting two
        /// scrollers caused the 6.4 double-scrollbar/empty-gap bugs (CareerSetup, Inbox).
        /// </summary>
        public static VisualElement Page(Color background)
        {
            var e = new VisualElement();
            e.AddToClassList("fts-page");
            e.style.flexGrow = 1f;
            e.style.backgroundColor = background;
            e.style.alignItems = Align.Center;
            e.style.paddingLeft = SpaceMd;
            e.style.paddingRight = SpaceMd;
            e.style.paddingTop = SpaceMd;
            e.style.paddingBottom = SpaceSm;
            return e;
        }

        /// <summary>
        /// A centred, capped-width content column (task 6.9) so pushed screens don't sprawl
        /// edge-to-edge on wide desktop viewports — content stays readable and every screen
        /// lines up the same way. Add a screen's body into this and add this into the page/scroll.
        /// </summary>
        public static VisualElement CenteredColumn(float maxWidth = 720f)
        {
            var e = new VisualElement();
            e.style.width = Length.Percent(100);
            e.style.maxWidth = maxWidth;
            e.style.alignSelf = Align.Center;
            return e;
        }

        /// <summary>A rounded surface card with a hairline border.</summary>
        public static VisualElement Card()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-card");
            if (!StylesLoaded)
            {
                e.style.backgroundColor = Surface;
                e.style.paddingLeft = SpaceMd;
                e.style.paddingRight = SpaceMd;
                e.style.paddingTop = SpaceMd;
                e.style.paddingBottom = SpaceMd;
                e.style.marginTop = SpaceSm;
                e.style.marginBottom = SpaceSm;
                Round(e, RadiusMd);
                SetBorder(e, Border, 1);
            }
            return e;
        }

        /// <summary>A horizontal row (flex-row, centred items) for list entries.</summary>
        public static VisualElement Row()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-row");
            e.style.flexDirection = FlexDirection.Row;   // layout-critical: keep inline too
            e.style.alignItems = Align.Center;
            return e;
        }

        /// <summary>A thin divider line.</summary>
        public static VisualElement Divider()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-divider");
            if (!StylesLoaded)
            {
                e.style.height = 1;
                e.style.marginTop = SpaceSm;
                e.style.marginBottom = SpaceSm;
                e.style.backgroundColor = Border;
            }
            return e;
        }

        // ---------------------------------------------------------------- text

        public static Label Title(string text)
        {
            var label = new Label(text);
            label.AddToClassList("fts-title");
            if (!StylesLoaded)
            {
                label.style.fontSize = FontTitle;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = TextPrimary;
                label.style.marginBottom = SpaceLg;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
            }
            return label;
        }

        /// <summary>Section header — smaller than a Title, still bold.</summary>
        public static Label Header(string text)
        {
            var label = new Label(text);
            label.AddToClassList("fts-header");
            if (!StylesLoaded)
            {
                label.style.fontSize = FontHeader;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = TextPrimary;
                label.style.marginBottom = SpaceSm;
            }
            return label;
        }

        public static Label Subtitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("fts-subtitle");
            if (!StylesLoaded)
            {
                label.style.fontSize = FontBody;
                label.style.color = TextMuted;
                label.style.marginBottom = SpaceMd;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
            }
            return label;
        }

        /// <summary>A small caption / helper line.</summary>
        public static Label Caption(string text)
        {
            var label = new Label(text);
            label.AddToClassList("fts-caption");
            if (!StylesLoaded)
            {
                label.style.fontSize = FontSmall;
                label.style.color = TextMuted;
            }
            return label;
        }

        // ---------------------------------------------------------------- buttons

        /// <summary>The standard navigation/menu button — rounded surface pill, bold label.</summary>
        public static Button MenuButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("fts-btn");
            button.AddToClassList("fts-btn--menu");
            if (!StylesLoaded)
            {
                button.style.width = 320;
                button.style.height = 54;
                button.style.fontSize = FontBody;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.color = TextPrimary;
                button.style.backgroundColor = SurfaceAlt;
                ClearButtonChrome(button);
                Round(button, RadiusMd);
                SetBorder(button, Border, 1);
            }
            return button;
        }

        /// <summary>A primary call-to-action — accent fill, dark legible label.</summary>
        public static Button PrimaryButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("fts-btn");
            button.AddToClassList("fts-btn--primary");
            if (!StylesLoaded)
            {
                button.style.height = 54;
                button.style.fontSize = FontBody;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.color = TextOnAccent;
                button.style.backgroundColor = Accent;
                button.style.paddingLeft = SpaceLg;
                button.style.paddingRight = SpaceLg;
                ClearButtonChrome(button);
                Round(button, RadiusMd);
            }
            return button;
        }

        // ---------------------------------------------------------------- small components

        /// <summary>A small rounded label chip (status / tag). Colours stay inline — they're dynamic.</summary>
        public static Label Pill(string text, Color background, Color textColor)
        {
            var label = new Label(text);
            label.AddToClassList("fts-pill");
            label.style.color = textColor;
            label.style.backgroundColor = background;
            if (!StylesLoaded)
            {
                label.style.fontSize = FontSmall;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.paddingLeft = SpaceSm;
                label.style.paddingRight = SpaceSm;
                label.style.paddingTop = 2;
                label.style.paddingBottom = 2;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                Round(label, RadiusSm);
            }
            return label;
        }

        /// <summary>A simple [track][fill] horizontal bar, value in [0,1]. Fully dynamic → inline.</summary>
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

        /// <summary>Guarantees a tappable element is at least <see cref="MinTouchPx"/> tall (Roadmap 6.4).</summary>
        public static void EnsureTapTarget(VisualElement e)
        {
            e.style.minHeight = MinTouchPx;
        }

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
