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
        public bool InBottomBar; // portrait bottom bar shows only the key sections
    }

    /// <summary>
    /// Persistent FM-style app chrome (task 6.6): a top bar (back, crest, club + day/budget line,
    /// Continue), a left icon sidebar on wide viewports and a bottom tab bar on narrow/portrait
    /// ones. Screens are pushed into <see cref="ContentHost"/> by the navigator. Dumb view —
    /// primitives only; the ShellController owns all wiring, translation and refresh logic.
    /// </summary>
    public sealed class AppShell
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
        private readonly Label _clubLabel;
        private readonly VisualElement _accentBar;
        private readonly Label _sublineLabel;
        private readonly Button _advanceButton;
        private readonly Button _continueButton;
        private readonly ScrollView _sidebar;
        private readonly ScrollView _bottomBar;

        private readonly Dictionary<string, Button> _sideItems = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _tabItems = new Dictionary<string, Button>();
        private readonly Dictionary<string, Label> _sideBadges = new Dictionary<string, Label>();
        private readonly Dictionary<string, Label> _tabBadges = new Dictionary<string, Label>();
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
            _crestSlot.style.marginLeft = UiKit.SpaceSm;
            _crestSlot.style.marginRight = UiKit.SpaceSm;
            _topBar.Add(_crestSlot);

            var clubBlock = new VisualElement();
            clubBlock.style.flexShrink = 1f;
            _clubLabel = new Label(string.Empty);
            _clubLabel.AddToClassList("fts-topbar__club");
            clubBlock.Add(_clubLabel);
            // Club-colour accent: a small underline, NOT the text itself — a dark club
            // primary tinting the name was unreadable on the navy bar (user feedback).
            _accentBar = new VisualElement();
            _accentBar.style.height = 3;
            _accentBar.style.width = 56;
            _accentBar.style.marginTop = 1;
            _accentBar.style.marginBottom = 2;
            UiKit.Round(_accentBar, 2);
            clubBlock.Add(_accentBar);
            _sublineLabel = new Label(string.Empty);
            _sublineLabel.AddToClassList("fts-topbar__sub");
            clubBlock.Add(_sublineLabel);
            _topBar.Add(clubBlock);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            _topBar.Add(spacer);

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

            // ---------------- bottom tab bar (portrait) — horizontally scrollable so every
            // section stays reachable on a phone without a "more" popup (v1 simplification).
            _bottomBar = new ScrollView(ScrollViewMode.Horizontal);
            _bottomBar.AddToClassList("fts-bottombar");
            _bottomBar.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _bottomBar.contentContainer.style.flexDirection = FlexDirection.Row;
            _bottomBar.contentContainer.style.flexGrow = 1f;
            Root.Add(_bottomBar);

            Root.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsiveLayout());
            ApplyResponsiveLayout();
        }

        // ---------------------------------------------------------------- population

        public void SetSections(IReadOnlyList<ShellSectionVm> sections)
        {
            _sidebar.Clear();
            _bottomBar.Clear();
            _sideItems.Clear();
            _tabItems.Clear();
            _sideBadges.Clear();
            _tabBadges.Clear();

            foreach (ShellSectionVm vm in sections)
            {
                string id = vm.Id;

                var side = new Button(() => SectionClicked?.Invoke(id));
                side.AddToClassList("fts-navitem");
                side.text = string.Empty;
                side.Add(IconKit.Icon(vm.IconId, 22f, UiKit.TextPrimary));
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

                var tab = new Button(() => SectionClicked?.Invoke(id));
                tab.AddToClassList("fts-tab");
                tab.text = string.Empty;
                tab.style.minWidth = 64;
                var iconWrap = new VisualElement();
                iconWrap.Add(IconKit.Icon(vm.IconId, 22f, UiKit.TextPrimary));
                var tabBadge = new Label(string.Empty);
                tabBadge.AddToClassList("fts-navitem__badge");
                tabBadge.style.position = Position.Absolute;
                tabBadge.style.top = -6;
                tabBadge.style.right = -14;
                tabBadge.style.display = DisplayStyle.None;
                iconWrap.Add(tabBadge);
                tab.Add(iconWrap);
                var tabLabel = new Label(vm.Label);
                tabLabel.AddToClassList("fts-tab__label");
                tab.Add(tabLabel);
                _bottomBar.Add(tab);
                _tabItems[id] = tab;
                _tabBadges[id] = tabBadge;
            }
        }

        // ---------------------------------------------------------------- state setters

        public void SetCrest(VisualElement crest)
        {
            _crestSlot.Clear();
            if (crest != null) _crestSlot.Add(crest);
        }

        public void SetClub(string name) => _clubLabel.text = name;

        /// <summary>The day/budget line under the club name (already translated/composed).</summary>
        public void SetSubline(string text) => _sublineLabel.text = text;

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
            if (id == null) return;
            if (_sideItems.TryGetValue(id, out var side)) side.AddToClassList("fts-navitem--active");
            if (_tabItems.TryGetValue(id, out var tab)) tab.AddToClassList("fts-tab--active");
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

        private void ApplyResponsiveLayout()
        {
            bool portrait = Responsive.IsMobile;
            bool showSidebar = _chromeVisible && !portrait;
            bool showBottomBar = _chromeVisible && portrait;

            _sidebar.style.display = showSidebar ? DisplayStyle.Flex : DisplayStyle.None;
            _bottomBar.style.display = showBottomBar ? DisplayStyle.Flex : DisplayStyle.None;

            if (showSidebar)
                _sidebar.EnableInClassList("fts-sidebar--compact", Responsive.Current == Viewport.Tablet);
        }

        private static Button IconButton(string iconId, Action onClick)
        {
            var button = new Button(onClick) { text = string.Empty };
            button.AddToClassList("fts-iconbtn");
            button.Add(IconKit.Icon(iconId, 22f, UiKit.TextPrimary));
            return button;
        }
    }
}
