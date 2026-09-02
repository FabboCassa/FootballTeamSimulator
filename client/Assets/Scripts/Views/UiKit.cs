using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// The client's design system bridge (tasks 6.1 art pass, 6.6 design system, 6.12 layout pass).
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
    ///
    /// Task 6.12 added the SHARED PAGE SCAFFOLD (ScreenRoot / PageColumn / SectionLabel /
    /// Panel / ListScroll / Toolbar / TabButton / ChipButton / FooterBar / StatTile / RowCard).
    /// Every screen is built from those so spacing, widths, headers and footers line up across
    /// the whole game instead of each view re-inventing its own private helpers.
    /// </summary>
    public static class UiKit
    {
        // ---------------------------------------------------------------- palette tokens (mirror FtsTheme.uss)
        // Task 14.1 repointed every one of these at the "Nuova Carriera" mockup: a near-black navy
        // ground, three quiet plate greys above it, ONE emerald accent, and a five-step text ramp
        // instead of the old alpha-white guesswork. The names are unchanged so all 45 screens keep
        // compiling and re-theme themselves the moment this file is saved.

        /// <summary>The page ground — nearly black, so plates and the accent do all the talking.</summary>
        public static readonly Color Background = Hex(0x080D18);
        /// <summary>A card / option block.</summary>
        public static readonly Color Surface = Hex(0x101A2C);
        /// <summary>A panel that HOLDS things (the list pane, the phone body): darker than a card.</summary>
        public static readonly Color SurfaceDeep = Hex(0x0D1626);
        /// <summary>The one lifted plate (summary block, phone header).</summary>
        public static readonly Color SurfaceRaised = Hex(0x14243C);
        /// <summary>Resting fill of a small control (chip, tab, small button).</summary>
        public static readonly Color SurfaceAlt = Hex(0x142034);
        /// <summary>Hover fill of any quiet control.</summary>
        public static readonly Color SurfaceHover = Hex(0x1A2740);
        /// <summary>Resting fill of a full-width list row.</summary>
        public static readonly Color RowSurface = Hex(0x121D31);
        /// <summary>Resting fill of a ghost (secondary) button.</summary>
        public static readonly Color Ghost = Hex(0x131D31);
        /// <summary>Text-input ground — the darkest plate in the system.</summary>
        public static readonly Color InputSurface = Hex(0x0A1120);
        /// <summary>Resting fill of a code/short tag.</summary>
        public static readonly Color TagSurface = Hex(0x1D2A42);

        /// <summary>The hairline around a card.</summary>
        public static readonly Color Border = Hex(0x1F2C46);
        /// <summary>The slightly louder hairline around a control.</summary>
        public static readonly Color BorderStrong = Hex(0x26334E);
        /// <summary>The hairline around a list row.</summary>
        public static readonly Color BorderRow = Hex(0x22304B);
        /// <summary>A rule between blocks.</summary>
        public static readonly Color DividerLine = Hex(0x1E2B45);

        public static readonly Color Accent = Hex(0x2FD08A);
        public static readonly Color AccentDark = Hex(0x1FA76D);
        /// <summary>The accent at 12% — the fill of a SELECTED row, where a solid accent would shout.</summary>
        public static readonly Color AccentSoft = new Color(47f / 255f, 208f / 255f, 138f / 255f, 0.12f);
        public static readonly Color Amber = Hex(0xF2A63B);

        /// <summary>
        /// Task 14.1: selection IS the accent now — the mockup has no second highlight colour.
        /// Anything that paints <see cref="Selection"/> must use <see cref="TextOnAccent"/> for its
        /// label; the light-on-blue pairing the old theme used is unreadable on emerald.
        /// </summary>
        public static readonly Color Selection = Accent;
        /// <summary>The quiet tint of <see cref="Selection"/> for a picked list row (light text is fine on it).</summary>
        public static readonly Color SelectionSoft = Hex(0x14352C);

        public static readonly Color TextPrimary = Hex(0xE6EDF8);
        /// <summary>Body copy one step below the headline.</summary>
        public static readonly Color TextSecondary = Hex(0xCFDAEC);
        public static readonly Color TextMuted = Hex(0xA9BAD6);
        /// <summary>Eyebrows, block captions, key columns.</summary>
        public static readonly Color TextLabel = Hex(0x7D90B0);
        /// <summary>Explanatory small print under a control.</summary>
        public static readonly Color TextHint = Hex(0x6E819F);
        /// <summary>Disabled / absent.</summary>
        public static readonly Color TextDim = Hex(0x4D5E7A);
        /// <summary>The label on a resting tag.</summary>
        public static readonly Color TagText = Hex(0x8FA2C0);
        public static readonly Color TextOnAccent = Hex(0x06221A);

        public static readonly Color Positive = Hex(0x5FD39A);
        public static readonly Color Warning = Hex(0xF2A63B);
        public static readonly Color Danger = Hex(0xE2574C);

        // ---------------------------------------------------------------- legacy aliases (kept so existing screens compile + re-theme)
        /// <summary>Task 6.6: re-pointed to the themed navy — the green main menu clashed with the rest of the app.</summary>
        public static readonly Color MenuGreen = Background;
        public static readonly Color HubBlue = Background;
        public static readonly Color PanelGray = Surface;

        // ---------------------------------------------------------------- scale tokens
        // These are DESKTOP values in UI POINTS, not mockup pixels. Panel Settings scales with
        // screen size against 1920x1080 at match 0.5, so one mockup pixel is about 1.35 points on a
        // desktop window and about 2.5 on a portrait phone. Component sizes therefore live in
        // FtsTheme.uss, which has a per-breakpoint block; the constants here exist for the ~225
        // places in the screens that still compute a margin or a radius in C#.

        public const int RadiusSm = 11;
        public const int RadiusMd = 15;
        public const int RadiusLg = 20;
        /// <summary>Sheet / phone-card radius (task 14.1).</summary>
        public const int RadiusXl = 28;
        public const int SpaceXs = 5;
        public const int SpaceSm = 11;
        public const int SpaceMd = 20;
        public const int SpaceLg = 30;

        public const int FontTitle = 60;
        public const int FontHeader = 34;
        public const int FontBody = 19;
        public const int FontSmall = 15;
        /// <summary>The tracked-out kicker above a title (task 14.1).</summary>
        public const int FontEyebrow = 18;

        /// <summary>Minimum comfortable touch-target edge in UI points (Roadmap 6.4, re-measured in 14.1).</summary>
        public const int MinTouchPx = 62;

        // ---------------------------------------------------------------- width tiers (task 6.12)
        // Screens no longer pick an arbitrary cap each: they choose one of three tiers, so a
        // wide desktop window is actually filled and every screen lines up with its neighbours.

        /// <summary>Forms and short prompts (login, create league, confirmations).</summary>
        public const float WidthNarrow = 620f;
        /// <summary>Reading/detail screens with a single column of prose or controls.</summary>
        public const float WidthMedium = 1040f;
        /// <summary>Lists, tables and anything with columns (roster, market, league table).</summary>
        public const float WidthWide = 1560f;

        // ---------------------------------------------------------------- stylesheet attach (task 6.6)

        private static StyleSheet _theme;
        private static bool _loadAttempted;

        // ---------------------------------------------------------------- fonts (task 14.1)
        // Two faces, both loaded from Resources/Fonts so a WebGL/Android build carries them:
        //   Archivo    — everything you READ (rows, values, hints, buttons).
        //   Bebas Neue — everything that SHOUTS (titles, kickers, block captions, tier numbers).
        // They are assigned from C# rather than from USS on purpose: the sheet is loaded through
        // Resources.Load and a resource() font reference that fails to resolve takes the whole rule
        // with it, whereas a missing Font here just leaves Unity's default in place.

        private static Font _bodyFont;
        private static Font _bodyBoldFont;
        private static Font _displayFont;
        private static bool _fontsAttempted;

        /// <summary>Archivo Regular, the app's reading face. Null if the asset is missing.</summary>
        public static Font BodyFont { get { EnsureFonts(); return _bodyFont; } }
        /// <summary>Archivo Bold, for values and buttons.</summary>
        public static Font BodyBoldFont { get { EnsureFonts(); return _bodyBoldFont; } }
        /// <summary>Bebas Neue, the condensed display face.</summary>
        public static Font DisplayFont { get { EnsureFonts(); return _displayFont; } }

        private static void EnsureFonts()
        {
            if (_fontsAttempted) return;
            _fontsAttempted = true;
            _bodyFont = Resources.Load<Font>("Fonts/Archivo-Regular");
            _bodyBoldFont = Resources.Load<Font>("Fonts/Archivo-Bold");
            _displayFont = Resources.Load<Font>("Fonts/BebasNeue-Regular");
            if (_bodyFont == null || _displayFont == null)
                Debug.LogWarning("[UiKit] Resources/Fonts missing a face — falling back to the Unity default.");
        }

        /// <summary>Paints <paramref name="e"/> in the condensed display face (Bebas Neue).</summary>
        public static VisualElement UseDisplayFont(VisualElement e)
        {
            EnsureFonts();
            if (_displayFont != null)
                e.style.unityFontDefinition = new StyleFontDefinition(_displayFont);
            return e;
        }

        /// <summary>Paints <paramref name="e"/> in the bold reading face (Archivo Bold).</summary>
        public static VisualElement UseBodyBoldFont(VisualElement e)
        {
            EnsureFonts();
            if (_bodyBoldFont != null)
                e.style.unityFontDefinition = new StyleFontDefinition(_bodyBoldFont);
            return e;
        }

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

            // Archivo on the root; every child inherits it, and the handful of display elements
            // override themselves with Bebas in their own factory below (task 14.1).
            EnsureFonts();
            if (_bodyFont != null)
                root.style.unityFontDefinition = new StyleFontDefinition(_bodyFont);

            // The breakpoint class the whole stylesheet keys off. Attaching it here rather than in
            // AppEntryPoint means no caller can forget it (task 14.1).
            Responsive.Attach(root);
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
        /// The standard root for a screen pushed into the shell (task 6.12): fills the content
        /// host, paints the themed background and applies the one set of page margins used by
        /// every screen. Put a <see cref="PageColumn"/> inside it.
        /// </summary>
        public static VisualElement ScreenRoot()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-screen");
            e.style.flexGrow = 1f;
            e.style.backgroundColor = Background;
            e.style.paddingTop = SpaceMd;
            e.style.paddingBottom = SpaceSm;
            e.style.paddingLeft = SpaceLg;
            e.style.paddingRight = SpaceLg;
            return e;
        }

        /// <summary>
        /// A centred content column that GROWS to the window and stops at one of the width tiers
        /// (<see cref="WidthNarrow"/> / <see cref="WidthMedium"/> / <see cref="WidthWide"/>).
        /// This replaces the per-screen 560/640/680/760 caps that left desktop windows half empty.
        /// </summary>
        /// <param name="grow">
        /// true (default) → the column fills the page height, so headers sit at the top and the
        /// list/panel inside it takes every spare pixel. false → the column is only as tall as its
        /// content, for the short forms that read better vertically centred inside
        /// <see cref="Screen"/> (login, create league).
        /// </param>
        public static VisualElement PageColumn(float maxWidth, bool grow = true)
        {
            var e = new VisualElement();
            e.AddToClassList("fts-col");
            e.style.width = Length.Percent(100);
            e.style.maxWidth = maxWidth;
            e.style.alignSelf = Align.Center;
            // Shrinkable so a short window squeezes the growing panel inside the column instead of
            // pushing the footer off the bottom edge.
            e.style.flexShrink = 1f;
            if (grow) e.style.flexGrow = 1f;
            return e;
        }

        /// <summary>
        /// A centred, capped-width content column (task 6.9). Kept for compatibility; new code
        /// should call <see cref="PageColumn"/> with a width tier so screens stay aligned.
        /// </summary>
        public static VisualElement CenteredColumn(float maxWidth = WidthMedium)
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
                Round(e, RadiusLg);
                SetBorder(e, Border, 1);
            }
            return e;
        }

        /// <summary>
        /// A surface panel (task 6.12): like <see cref="Card"/> but styled inline so it renders
        /// identically with or without the stylesheet, and meant to WRAP a section of a screen
        /// (a list, a form block, a stat block). Set flexGrow on it to make it fill the page.
        /// </summary>
        public static VisualElement Panel(bool grow = false)
        {
            var e = new VisualElement();
            e.AddToClassList("fts-panel");
            e.style.flexShrink = grow ? 1f : 0f;
            if (grow) e.style.flexGrow = 1f;
            if (!StylesLoaded)
            {
                e.style.paddingLeft = SpaceMd;
                e.style.paddingRight = SpaceMd;
                e.style.paddingTop = SpaceMd;
                e.style.paddingBottom = SpaceMd;
                e.style.marginBottom = SpaceSm;
                e.style.backgroundColor = SurfaceDeep;
                Round(e, RadiusLg);
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

        /// <summary>
        /// A full-width list entry on a surface plate (task 6.12) — the shared look for the
        /// "one thing per line with an action on the right" rows on Club, Training, Season, ….
        /// </summary>
        public static VisualElement RowCard(float minHeight = 52f)
        {
            var e = new VisualElement();
            e.AddToClassList("fts-rowcard");
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            if (!StylesLoaded)
            {
                e.style.minHeight = minHeight;
                e.style.marginBottom = 8;
                e.style.paddingLeft = 14;
                e.style.paddingRight = 14;
                e.style.paddingTop = 8;
                e.style.paddingBottom = 8;
                e.style.backgroundColor = RowSurface;
                Round(e, RadiusSm + 2);
            }
            return e;
        }

        /// <summary>Paints a <see cref="RowCard"/> as the picked entry in a list.</summary>
        public static void SetRowCardSelected(VisualElement row, bool selected)
        {
            if (selected) row.AddToClassList("fts-rowcard--selected");
            else row.RemoveFromClassList("fts-rowcard--selected");
            if (!StylesLoaded)
                row.style.backgroundColor = selected ? SelectionSoft : RowSurface;
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
                e.style.backgroundColor = DividerLine;
            }
            return e;
        }

        /// <summary>The vertical list scroller used inside a <see cref="Panel"/> (task 6.12).</summary>
        public static ScrollView ListScroll()
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.style.flexGrow = 1f;
            s.style.flexShrink = 1f;
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            s.verticalScrollerVisibility = ScrollerVisibility.Auto;
            return s;
        }

        /// <summary>A never-shrinking horizontal bar for tabs / filters / actions (task 6.12).</summary>
        public static VisualElement Toolbar()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-toolbar");
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            e.style.flexWrap = Wrap.Wrap;
            e.style.flexShrink = 0f;
            e.style.marginBottom = SpaceSm;
            return e;
        }

        /// <summary>The footer strip that carries Back / Save (task 6.12): one look on every screen.</summary>
        public static VisualElement FooterBar()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-footer");
            e.style.flexDirection = FlexDirection.Row;
            e.style.justifyContent = Justify.Center;
            e.style.alignItems = Align.Center;
            e.style.flexWrap = Wrap.Wrap;
            e.style.flexShrink = 0f;
            e.style.marginTop = SpaceSm;
            e.style.paddingTop = SpaceSm;
            if (!StylesLoaded)
            {
                e.style.borderTopWidth = 1;
                e.style.borderTopColor = DividerLine;
            }
            return e;
        }

        /// <summary>A row that lays out <see cref="StatTile"/>s and wraps on narrow windows.</summary>
        public static VisualElement TileRow()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.flexWrap = Wrap.Wrap;
            e.style.flexShrink = 0f;
            e.style.marginBottom = SpaceXs;
            return e;
        }

        // ---------------------------------------------------------------- text

        public static Label Title(string text)
        {
            var label = new Label(text);
            label.AddToClassList("fts-title");
            UseDisplayFont(label);
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
            UseDisplayFont(label);
            if (!StylesLoaded)
            {
                label.style.fontSize = FontHeader;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = TextPrimary;
                label.style.marginBottom = SpaceSm;
            }
            return label;
        }

        /// <summary>
        /// The screen title used by every pushed screen (task 6.12): left-aligned, never shrinks,
        /// with the same spacing below it everywhere.
        /// </summary>
        public static Label ScreenTitle(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("fts-screentitle");
            UseDisplayFont(label);
            if (!StylesLoaded)
            {
                label.style.fontSize = FontHeader;
                label.style.color = TextPrimary;
            }
            label.style.flexShrink = 0f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
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
                label.style.color = TextLabel;
            }
            return label;
        }

        /// <summary>
        /// The small uppercase label that introduces a block (task 6.12) — replaces the private
        /// SectionLabel each view used to declare with slightly different sizes and colours.
        /// </summary>
        public static Label SectionLabel(string caption)
        {
            // USS has no text-transform, so the caps that give the mockup its rhythm happen here.
            var label = new Label((caption ?? string.Empty).ToUpperInvariant());
            label.AddToClassList("fts-section");
            UseDisplayFont(label);
            if (!StylesLoaded)
            {
                label.style.fontSize = 17;
                label.style.color = TextLabel;
                label.style.marginTop = SpaceSm;
                label.style.marginBottom = SpaceXs;
            }
            label.style.flexShrink = 0f;
            return label;
        }

        /// <summary>A muted helper line under a title (wraps, never shrinks).</summary>
        public static Label HelpText(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("fts-help");
            if (!StylesLoaded)
            {
                label.style.fontSize = 16;
                label.style.color = TextHint;
            }
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 0f;
            label.style.marginBottom = SpaceSm;
            return label;
        }

        /// <summary>A body line inside a panel (label/value text at a consistent size).</summary>
        public static Label PanelLine(string text = "")
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("fts-line");
            if (!StylesLoaded)
            {
                label.style.fontSize = 19;
                label.style.color = TextSecondary;
            }
            label.style.whiteSpace = WhiteSpace.Normal;
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

        /// <summary>The footer Back/Save button — one size everywhere (task 6.12).</summary>
        public static Button FooterButton(string text, Action onClick)
        {
            var button = MenuButton(text, onClick);
            button.style.width = StyleKeyword.Null;
            button.style.minWidth = 190;
            button.style.marginLeft = 6;
            button.style.marginRight = 6;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
            button.style.flexShrink = 0f;
            return button;
        }

        /// <summary>The footer's accent action (Save, Confirm) — same size as <see cref="FooterButton"/>.</summary>
        public static Button FooterPrimaryButton(string text, Action onClick)
        {
            var button = PrimaryButton(text, onClick);
            button.style.minWidth = 190;
            button.style.marginLeft = 6;
            button.style.marginRight = 6;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
            button.style.flexShrink = 0f;
            return button;
        }

        /// <summary>A segmented tab button; it shares the row's width with its siblings.</summary>
        public static Button TabButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("fts-tabbtn");
            button.style.flexGrow = 1f;
            button.style.flexBasis = 0f;
            button.style.marginLeft = 0;
            button.style.marginRight = 6;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
            if (!StylesLoaded)
            {
                button.style.height = 46;
                button.style.fontSize = 17;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                ClearButtonChrome(button);
                Round(button, RadiusMd);
            }
            SetTabActive(button, false);
            return button;
        }

        /// <summary>Paints a <see cref="TabButton"/> as selected / unselected.</summary>
        public static void SetTabActive(Button button, bool active)
        {
            if (active) button.AddToClassList("fts-tabbtn--active");
            else button.RemoveFromClassList("fts-tabbtn--active");
            if (!StylesLoaded)
            {
                button.style.backgroundColor = active ? Accent : SurfaceAlt;
                button.style.color = active ? TextOnAccent : TextMuted;
            }
        }

        /// <summary>A small rounded filter chip for a toolbar.</summary>
        public static Button ChipButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text ?? string.Empty };
            button.AddToClassList("fts-chip");
            button.style.marginLeft = 0;
            button.style.marginRight = 6;
            button.style.marginTop = 2;
            button.style.marginBottom = 2;
            button.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                button.style.height = 44;
                button.style.fontSize = 17;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.paddingLeft = 18;
                button.style.paddingRight = 18;
                button.style.color = TextMuted;
                button.style.backgroundColor = SurfaceAlt;
                ClearButtonChrome(button);
                Round(button, RadiusSm + 2);
            }
            return button;
        }

        /// <summary>
        /// Rebuilds a row of filter chips (task 6.12b): every option is visible at once and the
        /// picked one lights up — replacing the old single chip you had to click repeatedly to
        /// cycle through the values, which hid the other options entirely.
        /// A chip carrying a reparto group lights up in that reparto's colour, so the filter row
        /// speaks the same colour language as the role cells in the list below it.
        /// </summary>
        public static void FillFilterChips(
            VisualElement container, IReadOnlyList<FilterChipVm> chips, Action<int> onPick)
        {
            container.Clear();
            for (int i = 0; i < chips.Count; i++)
            {
                FilterChipVm vm = chips[i];
                int value = vm.Value;
                Button chip = ChipButton(vm.Label, () => onPick?.Invoke(value));
                if (vm.Selected)
                {
                    chip.AddToClassList("fts-chip--active");
                    // Inline so the chip can take the reparto colour; the resting (unselected)
                    // chips keep their stylesheet colours and therefore their hover state.
                    chip.style.backgroundColor = vm.RoleGroup >= 0 ? PlayerRowKit.RoleColor(vm.RoleGroup) : Accent;
                    chip.style.color = vm.RoleGroup >= 0 ? Hex(0x231A00) : TextOnAccent;
                }
                container.Add(chip);
            }
        }

        /// <summary>Paints a <see cref="ChipButton"/> as on / off.</summary>
        public static void SetChipActive(Button button, bool active)
        {
            if (active) button.AddToClassList("fts-chip--active");
            else button.RemoveFromClassList("fts-chip--active");
            if (!StylesLoaded)
            {
                button.style.backgroundColor = active ? Accent : SurfaceAlt;
                button.style.color = active ? TextOnAccent : TextMuted;
            }
        }

        /// <summary>A compact inline action button used inside list rows.</summary>
        public static Button SmallButton(string text, Action onClick, float minWidth = 84f)
        {
            var button = new Button(onClick) { text = text ?? string.Empty };
            button.AddToClassList("fts-smallbtn");
            button.style.minWidth = minWidth;
            button.style.marginLeft = 5;
            button.style.marginRight = 0;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
            button.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                button.style.height = 44;
                button.style.fontSize = 17;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.paddingLeft = 14;
                button.style.paddingRight = 14;
                button.style.color = TextMuted;
                button.style.backgroundColor = SurfaceAlt;
                ClearButtonChrome(button);
                Round(button, RadiusSm);
            }
            return button;
        }

        /// <summary>Paints a <see cref="SmallButton"/> as the row's accent action (Buy, Confirm).</summary>
        public static void SetSmallButtonAccent(Button button, bool accent)
        {
            if (accent) button.AddToClassList("fts-smallbtn--accent");
            else button.RemoveFromClassList("fts-smallbtn--accent");
            if (!StylesLoaded)
            {
                button.style.backgroundColor = accent ? Accent : SurfaceAlt;
                button.style.color = accent ? TextOnAccent : TextMuted;
            }
        }

        /// <summary>Paints a <see cref="SmallButton"/> as an engaged toggle (shortlisted, listed, scouting).</summary>
        public static void SetSmallButtonOn(Button button, bool on)
        {
            if (on) button.AddToClassList("fts-smallbtn--on");
            else button.RemoveFromClassList("fts-smallbtn--on");
            if (!StylesLoaded)
                button.style.backgroundColor = on ? SelectionSoft : SurfaceAlt;
        }

        /// <summary>A full-width "cycle to the next value" control (tactics, training, setup).</summary>
        public static Button CycleButton(Action onClick, string text = "")
        {
            var button = new Button(onClick) { text = text ?? string.Empty };
            button.AddToClassList("fts-cycle");
            button.style.width = Length.Percent(100);
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.marginLeft = 0;
            button.style.marginRight = 0;
            button.style.marginTop = 0;
            button.style.marginBottom = 6;
            if (!StylesLoaded)
            {
                button.style.height = 58;
                button.style.fontSize = 19;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.paddingLeft = 20;
                button.style.paddingRight = 20;
                button.style.color = TextSecondary;
                button.style.backgroundColor = RowSurface;
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

        /// <summary>
        /// A headline number on a surface plate (task 6.12): the block used to fill the top of
        /// Career / Club / Season screens instead of a lone line of 13px text.
        /// </summary>
        public static VisualElement StatTile(string caption, string value, Color? valueColor = null, float minWidth = 170f)
        {
            var tile = new VisualElement();
            tile.AddToClassList("fts-stat");
            tile.style.flexGrow = 1f;
            tile.style.flexShrink = 1f;
            tile.style.flexBasis = minWidth;
            tile.style.minWidth = minWidth;
            tile.style.marginRight = SpaceSm;
            tile.style.marginBottom = SpaceSm;
            if (!StylesLoaded)
            {
                tile.style.paddingLeft = SpaceMd;
                tile.style.paddingRight = SpaceMd;
                tile.style.paddingTop = SpaceSm + 5;
                tile.style.paddingBottom = SpaceSm + 5;
                tile.style.backgroundColor = Surface;
                Round(tile, RadiusLg);
                SetBorder(tile, Border, 1);
            }

            var k = new Label((caption ?? string.Empty).ToUpperInvariant());
            k.AddToClassList("fts-stat__k");
            UseDisplayFont(k);
            if (!StylesLoaded)
            {
                k.style.fontSize = 15;
                k.style.color = TextLabel;
                k.style.marginBottom = 4;
            }
            k.style.whiteSpace = WhiteSpace.NoWrap;
            k.style.overflow = Overflow.Hidden;
            k.style.textOverflow = TextOverflow.Ellipsis;
            tile.Add(k);

            var v = new Label(value ?? string.Empty);
            v.AddToClassList("fts-stat__v");
            if (!StylesLoaded)
            {
                v.style.fontSize = 28;
                v.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            if (valueColor.HasValue) v.style.color = valueColor.Value;
            else if (!StylesLoaded) v.style.color = TextPrimary;
            v.style.whiteSpace = WhiteSpace.Normal;
            tile.Add(v);

            tile.userData = v;
            return tile;
        }

        /// <summary>Updates the value line of a tile built by <see cref="StatTile"/>.</summary>
        public static void SetStatTileValue(VisualElement tile, string value, Color? valueColor = null)
        {
            if (tile?.userData is Label v)
            {
                v.text = value ?? string.Empty;
                if (valueColor.HasValue) v.style.color = valueColor.Value;
            }
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

        // ---------------------------------------------------------------- mockup components (task 14.1)
        // Everything below comes straight off docs/Nuova Carriera.html. They are here rather than in
        // CareerSetupView because the mockup is the whole app's new language: the same kicker, the
        // same option card, the same tappable row and the same accent CTA are what every remaining
        // screen gets migrated onto in 14.3 and after.

        /// <summary>The small tracked-out kicker that sits above a title ("NUOVA CARRIERA").</summary>
        public static Label Eyebrow(string text)
        {
            var label = new Label((text ?? string.Empty).ToUpperInvariant());
            label.AddToClassList("fts-eyebrow");
            UseDisplayFont(label);
            if (!StylesLoaded)
            {
                label.style.fontSize = FontEyebrow;
                label.style.color = TextLabel;
                label.style.letterSpacing = 4;
            }
            label.style.flexShrink = 0f;
            return label;
        }

        /// <summary>
        /// The page header strip: a left stack (kicker + title) and a right group of quiet actions,
        /// separated from the body by one hairline. Returns the strip; add your left stack and your
        /// buttons to <see cref="PageHeadParts.Left"/> / <see cref="PageHeadParts.Actions"/>.
        /// </summary>
        public static PageHeadParts PageHead()
        {
            var bar = new VisualElement();
            bar.AddToClassList("fts-pagehead");
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.FlexEnd;
            bar.style.justifyContent = Justify.SpaceBetween;
            bar.style.flexShrink = 0f;

            var left = new VisualElement();
            left.style.flexShrink = 1f;
            left.style.minWidth = 0f;
            bar.Add(left);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.alignItems = Align.Center;
            actions.style.flexShrink = 0f;
            bar.Add(actions);

            return new PageHeadParts { Root = bar, Left = left, Actions = actions };
        }

        /// <summary>A block of related settings on a card (the mockup's left column).</summary>
        public static VisualElement OptionCard()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-optioncard");
            e.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                e.style.backgroundColor = Surface;
                e.style.paddingLeft = SpaceMd;
                e.style.paddingRight = SpaceMd;
                e.style.paddingTop = SpaceMd;
                e.style.paddingBottom = SpaceMd;
                e.style.marginBottom = SpaceSm + SpaceXs;
                Round(e, RadiusLg);
                SetBorder(e, Border, 1);
            }
            return e;
        }

        /// <summary>The one lifted plate on a screen — the summary block.</summary>
        public static VisualElement RaisedCard()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-raised");
            e.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                e.style.backgroundColor = SurfaceRaised;
                e.style.paddingLeft = SpaceLg - 6;
                e.style.paddingRight = SpaceLg - 6;
                e.style.paddingTop = SpaceLg - 6;
                e.style.paddingBottom = SpaceLg - 6;
                Round(e, RadiusLg);
                SetBorder(e, BorderStrong, 1);
            }
            return e;
        }

        /// <summary>Small print on a plate — the "this choice is final" block.</summary>
        public static Label NoteCard(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("fts-note");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                label.style.backgroundColor = Surface;
                label.style.color = TextHint;
                label.style.fontSize = 16;
                label.style.paddingLeft = SpaceMd;
                label.style.paddingRight = SpaceMd;
                label.style.paddingTop = SpaceMd;
                label.style.paddingBottom = SpaceMd;
                Round(label, RadiusLg);
                SetBorder(label, Border, 1);
            }
            return label;
        }

        /// <summary>The quiet secondary action of the header / footer.</summary>
        public static Button GhostButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text ?? string.Empty };
            button.AddToClassList("fts-ghostbtn");
            button.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                button.style.minHeight = 48;
                button.style.fontSize = 18;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.color = TextMuted;
                button.style.backgroundColor = Ghost;
                button.style.paddingLeft = 22;
                button.style.paddingRight = 22;
                Round(button, RadiusSm);
                SetBorder(button, BorderStrong, 1);
            }
            return button;
        }

        /// <summary>
        /// The accent call to action, with a display headline and a quiet second line under it —
        /// the "Inizia carriera / Italia Prima Divisione · 20 club" block of the mockup.
        /// Set <see cref="SetCtaSub"/> to change the second line later.
        /// </summary>
        public static Button CtaButton(string label, string sub, Action onClick)
        {
            var button = new Button(onClick) { text = string.Empty };
            button.AddToClassList("fts-ctabtn");
            button.style.flexDirection = FlexDirection.Column;
            button.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                button.style.backgroundColor = Accent;
                button.style.paddingTop = SpaceMd;
                button.style.paddingBottom = SpaceMd;
                ClearButtonChrome(button);
                Round(button, RadiusMd);
            }

            var head = new Label(label ?? string.Empty);
            head.AddToClassList("fts-ctabtn__label");
            UseDisplayFont(head);
            head.style.color = TextOnAccent;
            button.Add(head);

            var line = new Label(sub ?? string.Empty);
            line.AddToClassList("fts-ctabtn__sub");
            line.style.color = TextOnAccent;
            line.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;
            button.Add(line);

            button.userData = line;
            return button;
        }

        /// <summary>Updates the quiet second line of a <see cref="CtaButton"/>.</summary>
        public static void SetCtaSub(Button cta, string sub)
        {
            if (cta?.userData is Label line)
            {
                line.text = sub ?? string.Empty;
                line.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>
        /// The mockup's tappable row: a whole-width button carrying a left stack (an optional
        /// leading element, a name and a meta line) and a right marker. It is a Button and not a
        /// clickable VisualElement so it keeps keyboard focus, the hover state and the tap target.
        /// </summary>
        public static SelectRowParts SelectRow(Action onClick)
        {
            var button = new Button(onClick) { text = string.Empty };
            button.AddToClassList("fts-selectrow");
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.SpaceBetween;
            button.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                button.style.minHeight = 66;
                button.style.backgroundColor = RowSurface;
                button.style.paddingLeft = 20;
                button.style.paddingRight = 20;
                Round(button, RadiusMd);
                SetBorder(button, BorderRow, 1);
            }

            var lead = new VisualElement();
            lead.style.flexDirection = FlexDirection.Row;
            lead.style.alignItems = Align.Center;
            lead.style.flexShrink = 1f;
            lead.style.minWidth = 0f;
            button.Add(lead);

            var stack = new VisualElement();
            stack.style.flexShrink = 1f;
            stack.style.minWidth = 0f;
            lead.Add(stack);

            var name = new Label(string.Empty);
            name.AddToClassList("fts-selectrow__name");
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            if (!StylesLoaded)
            {
                name.style.fontSize = 23;
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                name.style.color = TextPrimary;
            }
            stack.Add(name);

            var meta = new Label(string.Empty);
            meta.AddToClassList("fts-selectrow__meta");
            meta.style.display = DisplayStyle.None;
            meta.style.overflow = Overflow.Hidden;
            meta.style.textOverflow = TextOverflow.Ellipsis;
            if (!StylesLoaded)
            {
                meta.style.fontSize = 15;
                meta.style.color = TextLabel;
            }
            stack.Add(meta);

            var mark = new Label(string.Empty);
            mark.AddToClassList("fts-selectrow__mark");
            mark.style.flexShrink = 0f;
            mark.style.display = DisplayStyle.None;
            if (!StylesLoaded)
            {
                mark.style.fontSize = 16;
                mark.style.unityFontStyleAndWeight = FontStyle.Bold;
                mark.style.color = Accent;
            }
            button.Add(mark);

            return new SelectRowParts { Root = button, Lead = lead, Name = name, Meta = meta, Mark = mark };
        }

        /// <summary>Paints a <see cref="SelectRow"/> as picked / unpicked / not choosable.</summary>
        public static void SetSelectRowState(SelectRowParts row, bool active, bool locked = false)
        {
            row.Root.EnableInClassList("fts-selectrow--active", active && !locked);
            row.Root.EnableInClassList("fts-selectrow--locked", locked);
            row.Root.SetEnabled(!locked);
            if (!StylesLoaded)
            {
                row.Root.style.backgroundColor = active && !locked ? SelectionSoft : RowSurface;
                SetBorder(row.Root, active && !locked ? Accent : BorderRow, 1);
                row.Root.style.opacity = locked ? 0.4f : 1f;
            }
        }

        /// <summary>
        /// One option of a segmented control that shares its row equally (1 / 2 / 3 divisions,
        /// Piccolo / Medio / Grande). A caption under the label is optional.
        /// </summary>
        public static Button SegChip(string label, string caption, Action onClick)
        {
            var button = new Button(onClick) { text = string.Empty };
            button.AddToClassList("fts-segchip");
            button.style.flexDirection = FlexDirection.Column;
            button.style.flexGrow = 1f;
            button.style.flexBasis = 0f;
            button.style.marginLeft = 0;
            button.style.marginRight = 0;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
            if (!StylesLoaded)
            {
                button.style.minHeight = 60;
                button.style.backgroundColor = SurfaceAlt;
                Round(button, RadiusSm + 2);
                SetBorder(button, BorderStrong, 1);
            }

            var head = new Label(label ?? string.Empty);
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (!StylesLoaded) head.style.fontSize = 18;
            button.Add(head);

            var sub = new Label(caption ?? string.Empty);
            sub.AddToClassList("fts-caption");
            sub.style.display = string.IsNullOrEmpty(caption) ? DisplayStyle.None : DisplayStyle.Flex;
            button.Add(sub);

            button.userData = head;
            return button;
        }

        /// <summary>Paints a <see cref="SegChip"/> as picked / unpicked / unavailable.</summary>
        public static void SetSegChipState(Button chip, bool active, bool locked = false)
        {
            chip.EnableInClassList("fts-segchip--active", active && !locked);
            chip.EnableInClassList("fts-segchip--locked", locked);
            chip.SetEnabled(!locked);

            // The child labels do not inherit the button's :hover colour, so paint them here.
            Color text = active && !locked ? TextOnAccent : TextMuted;
            foreach (VisualElement child in chip.Children())
                child.style.color = text;

            if (!StylesLoaded)
            {
                chip.style.backgroundColor = active && !locked ? Accent : SurfaceAlt;
                chip.style.opacity = locked ? 0.35f : 1f;
            }
        }

        /// <summary>A row that lays segmented chips out evenly with a gap between them.</summary>
        public static VisualElement SegRow(params VisualElement[] items)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexShrink = 0f;
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) items[i].style.marginLeft = SpaceSm;
                row.Add(items[i]);
            }
            return row;
        }

        /// <summary>The short code tag on a nation row (ITA, ENG, ...).</summary>
        public static Label Tag(string text)
        {
            var label = new Label((text ?? string.Empty).ToUpperInvariant());
            label.AddToClassList("fts-tag");
            UseDisplayFont(label);
            label.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                label.style.backgroundColor = TagSurface;
                label.style.color = TagText;
                label.style.fontSize = 18;
                label.style.paddingLeft = 10;
                label.style.paddingRight = 10;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                Round(label, 7);
            }
            return label;
        }

        /// <summary>The square index badge on a division row.</summary>
        public static Label TierBadge(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("fts-tierbadge");
            UseDisplayFont(label);
            label.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                label.style.width = 52;
                label.style.height = 52;
                label.style.backgroundColor = TagSurface;
                label.style.color = TagText;
                label.style.fontSize = 30;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                Round(label, RadiusSm + 2);
            }
            return label;
        }

        /// <summary>Lights a <see cref="Tag"/> or a <see cref="TierBadge"/> in the accent.</summary>
        public static void SetBadgeActive(Label badge, bool active)
        {
            badge.EnableInClassList("fts-tag--active", active && badge.ClassListContains("fts-tag"));
            badge.EnableInClassList("fts-tierbadge--active", active && badge.ClassListContains("fts-tierbadge"));
            if (!StylesLoaded)
            {
                badge.style.backgroundColor = active ? Accent : TagSurface;
                badge.style.color = active ? TextOnAccent : TagText;
            }
        }

        /// <summary>One "KEY ......... value" line of the summary block. Returns the value label.</summary>
        public static Label SummaryRow(VisualElement parent, string key, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("fts-summaryrow");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexEnd;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.flexShrink = 0f;

            var k = new Label((key ?? string.Empty).ToUpperInvariant());
            k.AddToClassList("fts-summaryrow__k");
            k.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                k.style.fontSize = 15;
                k.style.color = TextLabel;
            }
            row.Add(k);

            var v = new Label(value ?? string.Empty);
            v.AddToClassList("fts-summaryrow__v");
            v.style.flexShrink = 1f;
            v.style.minWidth = 0f;
            v.style.overflow = Overflow.Hidden;
            v.style.textOverflow = TextOverflow.Ellipsis;
            v.style.unityTextAlign = TextAnchor.MiddleRight;
            if (!StylesLoaded)
            {
                v.style.fontSize = 20;
                v.style.unityFontStyleAndWeight = FontStyle.Bold;
                v.style.color = TextPrimary;
            }
            row.Add(v);

            parent.Add(row);
            return v;
        }

        /// <summary>A themed one-line search box. The caller wires <c>RegisterValueChangedCallback</c>.</summary>
        public static TextField SearchField(string placeholder)
        {
            var field = new TextField { isDelayed = false };
            field.AddToClassList("fts-search");
            field.textEdition.placeholder = placeholder ?? string.Empty;
            field.textEdition.hidePlaceholderOnFocus = true;
            field.style.flexGrow = 1f;
            field.style.flexShrink = 1f;
            field.style.minWidth = 0f;
            if (!StylesLoaded)
            {
                field.style.minHeight = 48;
                field.style.backgroundColor = InputSurface;
                Round(field, RadiusSm);
                SetBorder(field, BorderStrong, 1);
            }
            return field;
        }

        /// <summary>
        /// The phone's modal bottom sheet. Returns the dimmed overlay (hidden) plus the panel to
        /// fill; call <see cref="ShowSheet"/> to raise it.
        /// </summary>
        public static SheetParts Sheet()
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("fts-sheet");
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.top = 0;
            overlay.style.right = 0;
            overlay.style.bottom = 0;
            overlay.style.justifyContent = Justify.FlexEnd;
            overlay.style.display = DisplayStyle.None;
            if (!StylesLoaded)
                overlay.style.backgroundColor = new Color(6f / 255f, 10f / 255f, 20f / 255f, 0.72f);

            var panel = new VisualElement();
            panel.AddToClassList("fts-sheet__panel");
            panel.style.maxHeight = Length.Percent(78);
            if (!StylesLoaded)
            {
                panel.style.backgroundColor = SurfaceDeep;
                panel.style.paddingLeft = SpaceLg;
                panel.style.paddingRight = SpaceLg;
                panel.style.paddingTop = SpaceLg;
                panel.style.paddingBottom = SpaceLg;
                panel.style.borderTopLeftRadius = RadiusXl;
                panel.style.borderTopRightRadius = RadiusXl;
            }
            overlay.Add(panel);

            var grab = new VisualElement();
            grab.AddToClassList("fts-sheet__grab");
            grab.style.alignSelf = Align.Center;
            grab.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                grab.style.width = 104;
                grab.style.height = 10;
                grab.style.backgroundColor = Hex(0x2B3A58);
                grab.style.marginBottom = SpaceMd;
                Round(grab, 5);
            }
            panel.Add(grab);

            return new SheetParts { Root = overlay, Panel = panel };
        }

        /// <summary>Raises or hides a sheet built by <see cref="Sheet"/>.</summary>
        public static void ShowSheet(SheetParts sheet, bool visible) =>
            sheet.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>The phone's fixed bottom action bar (summary on the left, CTA on the right).</summary>
        public static VisualElement BottomBar()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-bottombar");
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            e.style.justifyContent = Justify.SpaceBetween;
            e.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                e.style.backgroundColor = SurfaceDeep;
                e.style.borderTopWidth = 1;
                e.style.borderTopColor = Border;
                e.style.paddingLeft = SpaceLg;
                e.style.paddingRight = SpaceLg;
                e.style.paddingTop = SpaceMd;
                e.style.paddingBottom = SpaceLg;
            }
            return e;
        }

        /// <summary>The phone's header block (kicker + title + meta on a lifted plate).</summary>
        public static VisualElement MobileHeader()
        {
            var e = new VisualElement();
            e.AddToClassList("fts-mheader");
            e.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                e.style.backgroundColor = SurfaceRaised;
                e.style.borderBottomWidth = 1;
                e.style.borderBottomColor = Border;
                e.style.paddingLeft = SpaceLg;
                e.style.paddingRight = SpaceLg;
                e.style.paddingTop = SpaceLg + SpaceMd;
                e.style.paddingBottom = SpaceMd;
            }
            return e;
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

    /// <summary>The three parts of a <see cref="UiKit.PageHead"/> (task 14.1).</summary>
    public struct PageHeadParts
    {
        /// <summary>The strip itself — add this to the screen.</summary>
        public VisualElement Root;
        /// <summary>The left stack: put the kicker and the title in here.</summary>
        public VisualElement Left;
        /// <summary>The right group: put the ghost buttons in here.</summary>
        public VisualElement Actions;
    }

    /// <summary>The parts of a <see cref="UiKit.SelectRow"/> (task 14.1).</summary>
    public struct SelectRowParts
    {
        /// <summary>The clickable row — add this to the list.</summary>
        public Button Root;
        /// <summary>The left group; insert a tag or a tier badge at index 0.</summary>
        public VisualElement Lead;
        /// <summary>The row's headline.</summary>
        public Label Name;
        /// <summary>The quiet line under the headline; hidden until it has text.</summary>
        public Label Meta;
        /// <summary>The right-hand marker ("Selezionata", a dot, an arrow); hidden until it has text.</summary>
        public Label Mark;
    }

    /// <summary>The parts of a <see cref="UiKit.Sheet"/> (task 14.1).</summary>
    public struct SheetParts
    {
        /// <summary>The dimmed overlay — add this LAST to an absolutely-positioned screen root.</summary>
        public VisualElement Root;
        /// <summary>The white-space inside the sheet; fill it with the picker.</summary>
        public VisualElement Panel;
    }

    /// <summary>One option in a <see cref="UiKit.FillFilterChips"/> row.</summary>
    public sealed class FilterChipVm
    {
        /// <summary>The value the presenter switches on (e.g. a role index, -1 = "all").</summary>
        public int Value;
        public string Label;
        /// <summary>0 GK · 1 def · 2 mid · 3 att → the colour the chip lights up in; -1 = neutral.</summary>
        public int RoleGroup = -1;
        public bool Selected;
    }

    /// <summary>
    /// A labelled horizontal meter (task 6.12): the shared bar used for board confidence,
    /// facility levels and any 0-100 gauge. The view keeps the instance and calls
    /// <see cref="Set"/> on refresh instead of rebuilding the element.
    /// </summary>
    public sealed class MeterBar
    {
        public VisualElement Root { get; }
        private readonly VisualElement _fill;

        public MeterBar(float height = 16f)
        {
            Root = new VisualElement();
            Root.style.height = height;
            Root.style.width = Length.Percent(100);
            Root.style.flexShrink = 0f;
            Root.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            Root.style.overflow = Overflow.Hidden;
            UiKit.Round(Root, height * 0.5f);

            _fill = new VisualElement();
            _fill.style.height = Length.Percent(100);
            _fill.style.width = Length.Percent(50);
            _fill.style.backgroundColor = UiKit.Accent;
            UiKit.Round(_fill, height * 0.5f);
            Root.Add(_fill);
        }

        /// <summary>Sets the fill to <paramref name="percent"/> (0-100) in <paramref name="color"/>.</summary>
        public void Set(float percent, Color color)
        {
            float p = percent < 0f ? 0f : (percent > 100f ? 100f : percent);
            _fill.style.width = Length.Percent(p);
            _fill.style.backgroundColor = color;
        }
    }
}
