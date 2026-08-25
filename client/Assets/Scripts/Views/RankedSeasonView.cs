using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the ranked season (Phase 9.2), rebuilt on the shared page scaffold: a status panel
    /// (matchday · next kickoff · market window) with the screen's actions, then the standings and the
    /// schedule as two segmented tabs — the same table the private league and the single-player league
    /// use, minus the bits the ladder does not have. Played fixtures open the replay.
    /// </summary>
    public sealed class RankedSeasonView
    {
        public event Action RefreshClicked;
        public event Action LineupClicked;
        public event Action MarketClicked;
        public event Action AdvanceDevClicked; // dev-only
        public event Action BackClicked;
        public event Action<string> FixtureSelected; // a played fixture tapped → its id

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Label _banner;
        private readonly Label _windowBanner;
        private readonly VisualElement _seasonEndCard;
        private readonly Label _seasonEndTitle;
        private readonly Label _seasonEndDetail;
        private readonly Button[] _tabs;
        private readonly VisualElement[] _sections;
        private readonly VisualElement _standingsHeader;
        private readonly ScrollView _standings;
        private readonly ScrollView _schedule;
        private readonly Label _status;
        private readonly Button _lineupButton;
        private readonly Button _marketButton;
        private readonly Button _refreshButton;
        private readonly Button _advanceDevButton; // dev-only
        private readonly Button _backButton;

        public RankedSeasonView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            _title.style.marginBottom = UiKit.SpaceSm;
            col.Add(_title);

            // ---- status + actions -------------------------------------------------------------
            VisualElement head = UiKit.Panel();
            col.Add(head);

            _banner = UiKit.PanelLine(string.Empty);
            _banner.style.fontSize = 15;
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.Add(_banner);

            _windowBanner = UiKit.Caption(string.Empty);
            _windowBanner.style.color = UiKit.Positive;
            _windowBanner.style.whiteSpace = WhiteSpace.Normal;
            _windowBanner.style.marginBottom = UiKit.SpaceXs;
            _windowBanner.style.display = DisplayStyle.None;
            head.Add(_windowBanner);

            VisualElement actions = UiKit.Toolbar();
            actions.style.marginTop = UiKit.SpaceSm;
            actions.style.marginBottom = 0;
            head.Add(actions);

            _lineupButton = Action(actions, () => LineupClicked?.Invoke());
            UiKit.SetSmallButtonAccent(_lineupButton, true);
            _marketButton = Action(actions, () => MarketClicked?.Invoke());
            _refreshButton = Action(actions, () => RefreshClicked?.Invoke());
            // Dev-only: fast-forward the ranked calendar (hidden unless DevFlags).
            _advanceDevButton = Action(actions, () => AdvanceDevClicked?.Invoke());
            _advanceDevButton.style.display = DisplayStyle.None;

            // ---- season-end summary (Phase 9.3) ------------------------------------------------
            _seasonEndCard = UiKit.Panel();
            _seasonEndCard.style.display = DisplayStyle.None;
            col.Add(_seasonEndCard);
            _seasonEndTitle = UiKit.PanelLine(string.Empty);
            _seasonEndTitle.style.fontSize = 16;
            _seasonEndTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _seasonEndCard.Add(_seasonEndTitle);
            _seasonEndDetail = UiKit.Caption(string.Empty);
            _seasonEndDetail.style.whiteSpace = WhiteSpace.Normal;
            _seasonEndCard.Add(_seasonEndDetail);

            // ---- tabs ---------------------------------------------------------------------------
            VisualElement tabRow = UiKit.Toolbar();
            _tabs = new Button[2];
            for (int i = 0; i < _tabs.Length; i++)
            {
                int index = i;
                _tabs[i] = UiKit.TabButton(string.Empty, () => ShowTab(index));
                tabRow.Add(_tabs[i]);
            }
            _tabs[_tabs.Length - 1].style.marginRight = 0;
            col.Add(tabRow);

            VisualElement standingsSection = UiKit.Panel(grow: true);
            _standingsHeader = new VisualElement();
            _standingsHeader.style.flexShrink = 0f;
            standingsSection.Add(_standingsHeader);
            _standings = UiKit.ListScroll();
            standingsSection.Add(_standings);

            VisualElement scheduleSection = UiKit.Panel(grow: true);
            _schedule = UiKit.ListScroll();
            scheduleSection.Add(_schedule);

            _sections = new[] { standingsSection, scheduleSection };
            foreach (VisualElement section in _sections)
                col.Add(section);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 0f;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            VisualElement footer = UiKit.FooterBar();
            _backButton = UiKit.FooterButton(string.Empty, () => BackClicked?.Invoke());
            footer.Add(_backButton);
            col.Add(footer);

            RebuildStandingsHeader();
            ShowTab(0);
            UpdateTexts();
        }

        private static Button Action(VisualElement parent, Action onClick)
        {
            Button button = UiKit.SmallButton(string.Empty, onClick, 140f);
            button.style.marginLeft = 0;
            button.style.marginRight = 6;
            button.style.marginBottom = 4;
            parent.Add(button);
            return button;
        }

        private void ShowTab(int index)
        {
            for (int i = 0; i < _sections.Length; i++)
            {
                _sections[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                UiKit.SetTabActive(_tabs[i], i == index);
            }
        }

        public void SetTitle(string text) => _title.text = text;
        public void SetBanner(string text) => _banner.text = text;

        public void SetWindowBanner(string text, bool visible)
        {
            _windowBanner.text = text;
            _windowBanner.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Shows (or hides) the season-end summary card. <paramref name="celebrate"/> tints the
        /// headline green for a title/promotion and red for a relegation.</summary>
        public void SetSeasonEnd(string title, string detail, bool visible, bool celebrate, bool setback)
        {
            _seasonEndCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _seasonEndTitle.text = title;
            _seasonEndDetail.text = detail;
            _seasonEndTitle.style.color = celebrate ? UiKit.Positive : setback ? UiKit.Danger : UiKit.TextPrimary;
        }

        public void SetStandings(IReadOnlyList<StandingRowVm> rows)
        {
            _standings.Clear();
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
                _standings.Add(OnlineTableKit.StandingsRow(rows[i], i));
        }

        public void SetSchedule(IReadOnlyList<FixtureGroupVm> groups)
        {
            _schedule.Clear();
            if (groups == null) return;

            int index = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                FixtureGroupVm group = groups[g];
                _schedule.Add(OnlineTableKit.RoundHeader(group.RoundLabel, g == 0));
                if (group.Rows == null) continue;
                foreach (SeasonFixtureRowVm r in group.Rows)
                {
                    _schedule.Add(OnlineTableKit.FixtureRow(
                        r, index++, _tr("season.vs"), null,
                        id => FixtureSelected?.Invoke(id), null));
                }
            }
        }

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message;
            _status.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _status.style.display = DisplayStyle.Flex;
        }

        public void ClearStatus() => _status.style.display = DisplayStyle.None;

        public void SetBusy(bool busy)
        {
            _refreshButton.SetEnabled(!busy);
            _advanceDevButton.SetEnabled(!busy);
        }

        /// <summary>Shows the dev-only "fast-forward the calendar" button (DevFlags-gated by the presenter).</summary>
        public void SetDevToolsVisible(bool visible) =>
            _advanceDevButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void UpdateTexts()
        {
            _tabs[0].text = _tr("ranked.standings_caption");
            _tabs[1].text = _tr("ranked.schedule_caption");
            _lineupButton.text = _tr("ranked.lineup.open");
            _marketButton.text = _tr("ranked.market.open");
            _refreshButton.text = _tr("ranked.refresh");
            _advanceDevButton.text = _tr("ranked.dev_advance");
            _backButton.text = _tr("common.back");
            RebuildStandingsHeader();
        }

        private void RebuildStandingsHeader()
        {
            _standingsHeader.Clear();
            _standingsHeader.Add(OnlineTableKit.StandingsHeader(_tr));
        }
    }
}
