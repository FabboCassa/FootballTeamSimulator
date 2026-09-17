using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A navigable section shown in the shell's sidebar / bottom tab bar.</summary>
    public sealed class ShellSectionVm
    {
        public string Id;       // stable id the controller switches on ("squad", "inbox", ...)
        public string Label;    // translated label
        public string IconId;   // IconKit icon id
        /// <summary>
        /// A key section. Every section stays reachable from the phone's tab bar — the bar
        /// scrolls — but the key ones are laid out FIRST so they fit on screen without scrolling.
        /// </summary>
        public bool InBottomBar;
    }

    /// <summary>
    /// Persistent app chrome (task 6.6, redrawn on the "Nuova Carriera" mockup in 14.3): a top bar
    /// (back, crest, the club line as KICKER OVER VALUE, the budget as a second kicker-over-value
    /// block, advance-day and Continue), a left sidebar on wide viewports and a bottom tab bar on a
    /// phone. Screens are pushed into <see cref="ContentHost"/> by the navigator. Dumb view —
    /// primitives only; the ShellController owns all wiring, translation and refresh logic.
    ///
    /// SIZES LIVE IN THE STYLESHEET, NOT HERE. An inline size beats USS, so anything this class
    /// measured in points would stay frozen at its desktop value on a handset (the 14.1 corollary).
    /// That is why the nav icons take a CLASS rather than a size, and why the tab's minimum width is
    /// a rule and no longer an inline style: a phone needs a 150-point tab, a desktop window 64.
    /// </summary>
    public sealed class AppShell : IDisposable
    {
        // Task 14.1 took the breakpoint away from this class. The old test was `width < 720`, and
        // it never fired on a real phone: Panel Settings scales against a 1920x1080 reference, so a
        // 1080x2400 handset reports about 970 POINTS of width and the shell kept showing the desktop
        // sidebar in every portrait build. Responsive decides now, on the aspect ratio.

        public event Action BackClicked;
        public event Action AdvanceDayClicked;
        public event Action ContinueClicked;
        public event Action<string> SectionClicked;

        public VisualElement Root { get; }
        /// <summary>The element the ScreenNavigator uses as its root while a career is active.</summary>
        public VisualElement ContentHost { get; }

        private readonly VisualElement _topBar;
        private readonly Button _backButton;
        private readonly VisualElement _crestSlot;
        private readonly Label _kickerLabel;
        private readonly Label _clubLabel;
        private readonly VisualElement _accentBar;
        private readonly VisualElement _moneyBlock;
        private readonly Label _moneyCaption;
        private readonly Label _moneyValue;
        private readonly Button _advanceButton;
        private readonly Button _continueButton;
        private readonly ScrollView _sidebar;
        private readonly ScrollView _tabBar;

        private readonly Dictionary<string, Button> _sideItems = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _tabItems = new Dictionary<string, Button>();
        private readonly Dictionary<string, Label> _sideBadges = new Dictionary<string, Label>();
        private readonly Dictionary<string, Label> _tabBadges = new Dictionary<string, Label>();
        private readonly Dictionary<string, VisualElement> _sideIcons = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, VisualElement> _tabIcons = new Dictionary<string, VisualElement>();
        private string _activeId;
        private bool _chromeVisible = true;

        public AppShell()
        {
            Root = new VisualElement();
            Root.AddToClassList("fts-shell");
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background; // fallback if USS missing

            // ---------------- top bar
            _topBar = new VisualElement();
            _topBar.AddToClassList("fts-topbar");
            _topBar.style.flexDirection = FlexDirection.Row;
            _topBar.style.alignItems = Align.Center;
            Root.Add(_topBar);

            _backButton = IconButton("back", () => BackClicked?.Invoke());
            _topBar.Add(_backButton);

            _crestSlot = new VisualElement();
            _crestSlot.AddToClassList("fts-topbar__crest");
            _topBar.Add(_crestSlot);

            // The mockup's signature: a small tracked-out kicker ABOVE the value it describes.
            // Here the kicker is where we are in the season and the value is who we are.
            var clubBlock = new VisualElement();
            clubBlock.AddToClassList("fts-topbar__clubblock");
            clubBlock.style.flexShrink = 1f;
            clubBlock.style.minWidth = 0f;

            _kickerLabel = new Label(string.Empty);
            _kickerLabel.AddToClassList("fts-topbar__kicker");
            UiKit.UseDisplayFont(_kickerLabel);
            clubBlock.Add(_kickerLabel);

            _clubLabel = new Label(string.Empty);
            _clubLabel.AddToClassList("fts-topbar__club");
            UiKit.UseDisplayFont(_clubLabel);
            clubBlock.Add(_clubLabel);

            // Club-colour accent: a small underline, NOT the text itself — a dark club
            // primary tinting the name was unreadable on the navy bar (user feedback).
            _accentBar = new VisualElement();
            _accentBar.AddToClassList("fts-topbar__accent");
            if (!UiKit.StylesLoaded)
            {
                _accentBar.style.height = 3;
                _accentBar.style.width = 56;
                _accentBar.style.marginTop = 2;
                UiKit.Round(_accentBar, 2);
            }
            clubBlock.Add(_accentBar);
            _topBar.Add(clubBlock);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            _topBar.Add(spacer);

            // The same kicker-over-value block again, right-aligned, for the number a manager
            // checks most often. Hidden on a phone by the stylesheet — the overview carries it
            // there, and the bar has no room for a third block beside two 110-point buttons.
            _moneyBlock = new VisualElement();
            _moneyBlock.AddToClassList("fts-topbar__money");
            _moneyCaption = new Label(string.Empty);
            _moneyCaption.AddToClassList("fts-topbar__moneyk");
            UiKit.UseDisplayFont(_moneyCaption);
            _moneyBlock.Add(_moneyCaption);
            _moneyValue = new Label(string.Empty);
            _moneyValue.AddToClassList("fts-topbar__moneyv");
            UiKit.UseDisplayFont(_moneyValue);
            _moneyBlock.Add(_moneyValue);
            _topBar.Add(_moneyBlock);

            _advanceButton = IconButton("advance", () => AdvanceDayClicked?.Invoke());
            _topBar.Add(_advanceButton);

            _continueButton = UiKit.PrimaryButton(string.Empty, () => ContinueClicked?.Invoke());
            _continueButton.AddToClassList("fts-topbar__continue");
            _topBar.Add(_continueButton);

            // ---------------- body: sidebar + content
            var body = new VisualElement();
            body.AddToClassList("fts-body");
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;
            Root.Add(body);

            _sidebar = new ScrollView(ScrollViewMode.Vertical);
            _sidebar.AddToClassList("fts-sidebar");
            _sidebar.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(_sidebar);

            ContentHost = new VisualElement();
            ContentHost.AddToClassList("fts-content");
            ContentHost.style.flexGrow = 1f;
            body.Add(ContentHost);

            // ---------------- bottom tab bar (phone) — horizontally scrollable so every section
            // stays reachable without a "more" popup; the key ones are laid out first so they fit.
            // NOTE the class: it used to be `fts-bottombar`, which is the MOCKUP'S CTA BAR (UiKit
            // .BottomBar), so the tab bar was silently inheriting that bar's padding. Its own now.
            _tabBar = new ScrollView(ScrollViewMode.Horizontal);
            _tabBar.AddToClassList("fts-tabbar");
            _tabBar.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _tabBar.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _tabBar.contentContainer.style.flexDirection = FlexDirection.Row;
            _tabBar.contentContainer.style.flexGrow = 1f;
            Root.Add(_tabBar);

            Root.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsiveLayout());
            Responsive.Changed += OnViewportChanged;
            ApplyResponsiveLayout();
        }

        /// <summary>Drops the static Responsive subscription. Called when the career closes.</summary>
        public void Dispose()
        {
            Responsive.Changed -= OnViewportChanged;
        }

        // ---------------------------------------------------------------- population

        public void SetSections(IReadOnlyList<ShellSectionVm> sections)
        {
            _sidebar.Clear();
            _tabBar.Clear();
            _sideItems.Clear();
            _tabItems.Clear();
            _sideBadges.Clear();
            _tabBadges.Clear();
            _sideIcons.Clear();
            _tabIcons.Clear();

            foreach (ShellSectionVm vm in sections)
                AddSideItem(vm);

            // Key sections first in the tab bar, the rest after: a phone shows about six tabs
            // before it has to scroll, and those six should be the ones worth reaching.
            foreach (ShellSectionVm vm in sections)
                if (vm.InBottomBar) AddTabItem(vm);
            foreach (ShellSectionVm vm in sections)
                if (!vm.InBottomBar) AddTabItem(vm);

            ApplyActiveTint();
        }

        private void AddSideItem(ShellSectionVm vm)
        {
            string id = vm.Id;

            var side = new Button(() => SectionClicked?.Invoke(id));
            side.AddToClassList("fts-navitem");
            side.text = string.Empty;

            // A left rail that lights up with the accent on the active item — the soft tint alone
            // read as "slightly different navy" on this ground.
            var rail = new VisualElement();
            rail.AddToClassList("fts-navitem__rail");
            side.Add(rail);

            VisualElement icon = IconKit.Icon(vm.IconId, "fts-navicon", 22f, UiKit.TextMuted);
            side.Add(icon);

            var label = new Label(vm.Label);
            label.AddToClassList("fts-navitem__label");
            side.Add(label);

            var badge = new Label(string.Empty);
            badge.AddToClassList("fts-navitem__badge");
            badge.style.display = DisplayStyle.None;
            side.Add(badge);

            _sidebar.Add(side);
            _sideItems[id] = side;
            _sideBadges[id] = badge;
            _sideIcons[id] = icon;
        }

        private void AddTabItem(ShellSectionVm vm)
        {
            string id = vm.Id;

            var tab = new Button(() => SectionClicked?.Invoke(id));
            tab.AddToClassList("fts-tab");
            tab.text = string.Empty;

            var iconWrap = new VisualElement();
            iconWrap.AddToClassList("fts-tab__iconwrap");
            VisualElement icon = IconKit.Icon(vm.IconId, "fts-tabicon", 24f, UiKit.TextMuted);
            iconWrap.Add(icon);

            var tabBadge = new Label(string.Empty);
            tabBadge.AddToClassList("fts-navitem__badge");
            tabBadge.AddToClassList("fts-tab__badge");
            tabBadge.style.display = DisplayStyle.None;
            iconWrap.Add(tabBadge);
            tab.Add(iconWrap);

            var tabLabel = new Label(vm.Label);
            tabLabel.AddToClassList("fts-tab__label");
            tab.Add(tabLabel);

            _tabBar.Add(tab);
            _tabItems[id] = tab;
            _tabBadges[id] = tabBadge;
            _tabIcons[id] = icon;
        }

        // ---------------------------------------------------------------- state setters

        public void SetCrest(VisualElement crest)
        {
            _crestSlot.Clear();
            if (crest != null) _crestSlot.Add(crest);
        }

        /// <summary>The club line: a small tracked-out kicker over the club name.</summary>
        public void SetClubLine(string kicker, string name)
        {
            // USS has no text-transform, so the caps happen here — the UiKit.Eyebrow convention.
            _kickerLabel.text = (kicker ?? string.Empty).ToUpperInvariant();
            _clubLabel.text = name ?? string.Empty;
        }

        /// <summary>The right-hand kicker-over-value block (budget). Hidden on a phone by the sheet.</summary>
        public void SetMoney(string caption, string value)
        {
            _moneyCaption.text = (caption ?? string.Empty).ToUpperInvariant();
            _moneyValue.text = value ?? string.Empty;
        }

        public void SetAccent(Color primary) => _accentBar.style.backgroundColor = primary;

        public void SetContinueLabel(string label) => _continueButton.text = label;

        public void SetAdvanceVisible(bool visible) =>
            _advanceButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetBackVisible(bool visible) =>
            _backButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetActiveSection(string id)
        {
            if (_activeId != null)
            {
                if (_sideItems.TryGetValue(_activeId, out var oldSide)) oldSide.RemoveFromClassList("fts-navitem--active");
                if (_tabItems.TryGetValue(_activeId, out var oldTab)) oldTab.RemoveFromClassList("fts-tab--active");
            }
            _activeId = id;
            if (id != null)
            {
                if (_sideItems.TryGetValue(id, out var side)) side.AddToClassList("fts-navitem--active");
                if (_tabItems.TryGetValue(id, out var tab)) tab.AddToClassList("fts-tab--active");
            }
            ApplyActiveTint();
        }

        public void SetBadge(string id, int count)
        {
            string text = count > 99 ? "99+" : count.ToString();
            if (_sideBadges.TryGetValue(id, out var b))
            {
                b.text = text;
                b.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_tabBadges.TryGetValue(id, out var t))
            {
                t.text = text;
                t.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Hides all chrome (top bar + nav) — used while watching a match full-screen.</summary>
        public void SetChromeVisible(bool visible)
        {
            _chromeVisible = visible;
            _topBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            ApplyResponsiveLayout();
        }

        // ---------------------------------------------------------------- responsive

        private void OnViewportChanged(Viewport viewport) => ApplyResponsiveLayout();

        private void ApplyResponsiveLayout()
        {
            bool portrait = Responsive.IsMobile;
            bool showSidebar = _chromeVisible && !portrait;
            bool showTabBar = _chromeVisible && portrait;

            _sidebar.style.display = showSidebar ? DisplayStyle.Flex : DisplayStyle.None;
            _tabBar.style.display = showTabBar ? DisplayStyle.Flex : DisplayStyle.None;

            if (showSidebar)
                // Task 14.9: icon-only only when there is no room — a landscape tablet keeps its labels.
                _sidebar.EnableInClassList("fts-sidebar--compact", !Responsive.HasWideColumns);
        }

        /// <summary>
        /// Paints the active section's glyph with the accent and the rest muted. The label colour is
        /// a USS rule, but a painter2D glyph carries its own tint, so it has to be told.
        /// </summary>
        private void ApplyActiveTint()
        {
            Tint(_sideIcons);
            Tint(_tabIcons);
        }

        private void Tint(Dictionary<string, VisualElement> icons)
        {
            foreach (KeyValuePair<string, VisualElement> pair in icons)
            {
                // A PNG override (the mod hook) is an Image and carries no tint — leave it alone.
                if (pair.Value is VectorIcon glyph)
                    glyph.SetTint(pair.Key == _activeId ? UiKit.Accent : UiKit.TextMuted);
            }
        }

        private static Button IconButton(string iconId, Action onClick)
        {
            var button = new Button(onClick) { text = string.Empty };
            button.AddToClassList("fts-iconbtn");
            button.Add(IconKit.Icon(iconId, "fts-btnicon", 22f, UiKit.TextPrimary));
            return button;
        }
    }
}
