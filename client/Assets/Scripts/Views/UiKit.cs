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
        public static readonly Color Background = Hex(0x141C30);
        public static readonly Color Surface = Hex(0x1E2A44);
        public static readonly Color SurfaceAlt = Hex(0x2A3A5C);
        public static readonly Color Border = Hex(0x3A4D74);

        public static readonly Color Accent = Hex(0x27C281);
        public static readonly Color AccentDark = Hex(0x1C9B66);
        public static readonly Color Amber = Hex(0xF2B33D);

        /// <summary>Selected/active blue used by tabs, chips and picked rows (task 6.12).</summary>
        public static readonly Color Selection = Hex(0x3D6DB0);
        /// <summary>The quieter tint of <see cref="Selection"/> for selected list rows.</summary>
        public static readonly Color SelectionSoft = Hex(0x2C4573);

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

        // ---------------------------------------------------------------- width tiers (task 6.12)
        // Screens no longer pick an arbitrary cap each: they choose one of three tiers, so a
        // wide desktop window is actually filled and every screen lines up with its neighbours.

        /// <summary>Forms and short prompts (login, create league, confirmations).</summary>
        public const float WidthNarrow = 520f;
        /// <summary>Reading/detail screens with a single column of prose or controls.</summary>
        public const float WidthMedium = 900f;
        /// <summary>Lists, tables and anything with columns (roster, market, league table).</summary>
        public const float WidthWide = 1280f;

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
                Round(e, RadiusMd);
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
            e.style.paddingLeft = SpaceMd;
            e.style.paddingRight = SpaceMd;
            e.style.paddingTop = SpaceSm + SpaceXs;
            e.style.paddingBottom = SpaceSm + SpaceXs;
            e.style.marginBottom = SpaceSm;
            e.style.flexShrink = grow ? 1f : 0f;
            if (grow) e.style.flexGrow = 1f;
            if (!StylesLoaded)
            {
                e.style.backgroundColor = Surface;
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
            e.style.minHeight = minHeight;
            e.style.marginBottom = 6;
            e.style.paddingLeft = SpaceMd - 4;
            e.style.paddingRight = SpaceSm;
            e.style.paddingTop = 6;
            e.style.paddingBottom = 6;
            if (!StylesLoaded)
            {
                e.style.backgroundColor = Surface;
                Round(e, RadiusSm);
            }
            return e;
        }

        /// <summary>Paints a <see cref="RowCard"/> as the picked entry in a list.</summary>
        public static void SetRowCardSelected(VisualElement row, bool selected)
        {
            if (selected) row.AddToClassList("fts-rowcard--selected");
            else row.RemoveFromClassList("fts-rowcard--selected");
            if (!StylesLoaded)
                row.style.backgroundColor = selected ? SelectionSoft : Surface;
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
                e.style.borderTopColor = Border;
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

        /// <summary>
        /// The screen title used by every pushed screen (task 6.12): left-aligned, never shrinks,
        /// with the same spacing below it everywhere.
        /// </summary>
        public static Label ScreenTitle(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("fts-screentitle");
            label.style.fontSize = 24;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (!StylesLoaded) label.style.color = TextPrimary;
            label.style.marginBottom = 2;
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
                label.style.color = TextMuted;
            }
            return label;
        }

        /// <summary>
        /// The small uppercase label that introduces a block (task 6.12) — replaces the private
        /// SectionLabel each view used to declare with slightly different sizes and colours.
        /// </summary>
        public static Label SectionLabel(string caption)
        {
            var label = new Label(caption ?? string.Empty);
            label.AddToClassList("fts-section");
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (!StylesLoaded) label.style.color = new Color(0.96f, 0.97f, 0.99f, 0.55f);
            label.style.marginTop = SpaceSm;
            label.style.marginBottom = SpaceXs;
            label.style.flexShrink = 0f;
            return label;
        }

        /// <summary>A muted helper line under a title (wraps, never shrinks).</summary>
        public static Label HelpText(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("fts-help");
            label.style.fontSize = 13;
            if (!StylesLoaded) label.style.color = TextMuted;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 0f;
            label.style.marginBottom = SpaceSm;
            return label;
        }

        /// <summary>A body line inside a panel (label/value text at a consistent size).</summary>
        public static Label PanelLine(string text = "")
        {
            var label = new Label(text ?? string.Empty);
            label.style.fontSize = 14;
            label.style.color = TextPrimary;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 2;
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
            button.style.width = 170;
            button.style.height = 44;
            button.style.fontSize = 15;
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
            button.style.width = 170;
            button.style.height = 44;
            button.style.fontSize = 15;
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
            button.style.height = 40;
            button.style.fontSize = 14;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.marginLeft = 0;
            button.style.marginRight = 6;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
            button.style.paddingLeft = SpaceSm;
            button.style.paddingRight = SpaceSm;
            if (!StylesLoaded)
            {
                ClearButtonChrome(button);
                Round(button, RadiusSm + 2);
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
                button.style.backgroundColor = active ? Selection : SurfaceAlt;
                button.style.color = active ? TextPrimary : TextMuted;
            }
        }

        /// <summary>A small rounded filter chip for a toolbar.</summary>
        public static Button ChipButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text ?? string.Empty };
            button.AddToClassList("fts-chip");
            button.style.height = 32;
            button.style.fontSize = 12;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.marginLeft = 0;
            button.style.marginRight = 6;
            button.style.marginTop = 2;
            button.style.marginBottom = 2;
            button.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                button.style.color = TextPrimary;
                button.style.backgroundColor = SurfaceAlt;
                ClearButtonChrome(button);
                Round(button, 16);
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
                    chip.style.backgroundColor = vm.RoleGroup >= 0 ? PlayerRowKit.RoleColor(vm.RoleGroup) : Selection;
                    chip.style.color = vm.RoleGroup == 0 ? Hex(0x231A00) : TextPrimary;
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
                button.style.backgroundColor = active ? Selection : SurfaceAlt;
                button.style.color = active ? TextPrimary : TextMuted;
            }
        }

        /// <summary>A compact inline action button used inside list rows.</summary>
        public static Button SmallButton(string text, Action onClick, float minWidth = 84f)
        {
            var button = new Button(onClick) { text = text ?? string.Empty };
            button.AddToClassList("fts-smallbtn");
            button.style.height = 34;
            button.style.minWidth = minWidth;
            button.style.fontSize = 13;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.marginLeft = 5;
            button.style.marginRight = 0;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
            button.style.flexShrink = 0f;
            if (!StylesLoaded)
            {
                button.style.color = TextPrimary;
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
                button.style.color = accent ? TextOnAccent : TextPrimary;
            }
        }

        /// <summary>Paints a <see cref="SmallButton"/> as an engaged toggle (shortlisted, listed, scouting).</summary>
        public static void SetSmallButtonOn(Button button, bool on)
        {
            if (on) button.AddToClassList("fts-smallbtn--on");
            else button.RemoveFromClassList("fts-smallbtn--on");
            if (!StylesLoaded)
                button.style.backgroundColor = on ? Selection : SurfaceAlt;
        }

        /// <summary>A full-width "cycle to the next value" control (tactics, training, setup).</summary>
        public static Button CycleButton(Action onClick, string text = "")
        {
            var button = new Button(onClick) { text = text ?? string.Empty };
            button.AddToClassList("fts-cycle");
            button.style.height = 44;
            button.style.width = Length.Percent(100);
            button.style.fontSize = 15;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.paddingLeft = 14;
            button.style.paddingRight = 14;
            button.style.marginLeft = 0;
            button.style.marginRight = 0;
            button.style.marginTop = 0;
            button.style.marginBottom = 6;
            if (!StylesLoaded)
            {
                button.style.color = TextPrimary;
                button.style.backgroundColor = SurfaceAlt;
                ClearButtonChrome(button);
                Round(button, RadiusSm);
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
            tile.style.paddingLeft = SpaceMd - 2;
            tile.style.paddingRight = SpaceMd - 2;
            tile.style.paddingTop = SpaceSm + 2;
            tile.style.paddingBottom = SpaceSm + 2;
            if (!StylesLoaded)
            {
                tile.style.backgroundColor = Surface;
                Round(tile, RadiusMd);
                SetBorder(tile, Border, 1);
            }

            var k = new Label((caption ?? string.Empty).ToUpperInvariant());
            k.AddToClassList("fts-stat__k");
            k.style.fontSize = 11;
            k.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (!StylesLoaded) k.style.color = new Color(0.96f, 0.97f, 0.99f, 0.52f);
            k.style.marginBottom = 3;
            k.style.whiteSpace = WhiteSpace.NoWrap;
            k.style.overflow = Overflow.Hidden;
            k.style.textOverflow = TextOverflow.Ellipsis;
            tile.Add(k);

            var v = new Label(value ?? string.Empty);
            v.AddToClassList("fts-stat__v");
            v.style.fontSize = 20;
            v.style.unityFontStyleAndWeight = FontStyle.Bold;
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
