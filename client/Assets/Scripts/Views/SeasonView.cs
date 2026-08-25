using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for a private league's season (task 8.3b), rebuilt on the shared page scaffold so the
    /// ONLINE league reads like the single-player league screen: a status panel with the matchday + the
    /// round actions, two segmented tabs (standings / schedule) each filling the page in their own panel,
    /// and one footer. Played fixtures open the replay; your current-round fixture opens the live match.
    /// No logic — the presenter fills it from the season DTO and drives the API.
    /// </summary>
    public sealed class SeasonView
    {
        public event Action ReadyToggleClicked;
        public event Action AdvanceClicked;
        public event Action RefreshClicked;
        public event Action EditLineupClicked;
        public event Action VerifyStateClicked;
        /// <summary>Opens the end-of-season summary + awards (8.7b) — shown once the season is complete.</summary>
        public event Action SeasonEndClicked;
        public event Action<string> FixtureClicked; // fixture id (played only)
        public event Action<string> PlayLiveClicked; // fixture id (your current-round unplayed fixture, 8.6b)
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Label _banner;
        private readonly Button _readyButton;
        private readonly Button _advanceButton;
        private readonly Button _editButton;
        private readonly Button _refreshButton;
        private readonly Button _seasonEndButton;
        private readonly Label _stateHashValue;
        private readonly Button _verifyButton;
        private readonly Button[] _tabs;
        private readonly VisualElement[] _sections;
        private readonly VisualElement _standingsHeader;
        private readonly ScrollView _standings;
        private readonly ScrollView _schedule;
        private readonly Label _status;
        private readonly Button _backButton;

        public SeasonView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            _title.style.marginBottom = UiKit.SpaceSm;
            col.Add(_title);

            // ---- status + round actions ------------------------------------------------------
            VisualElement head = UiKit.Panel();
            col.Add(head);

            _banner = UiKit.PanelLine(string.Empty);
            _banner.style.fontSize = 15;
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _banner.style.marginBottom = UiKit.SpaceSm;
            head.Add(_banner);

            VisualElement actions = UiKit.Toolbar();
            actions.style.marginBottom = 0;
            head.Add(actions);

            _readyButton = UiKit.SmallButton(string.Empty, () => ReadyToggleClicked?.Invoke(), 150f);
            _readyButton.style.marginLeft = 0;
            _readyButton.style.marginRight = 6;
            _readyButton.style.marginBottom = 4;
            UiKit.SetSmallButtonAccent(_readyButton, true);
            actions.Add(_readyButton);

            _advanceButton = Action(actions, () => AdvanceClicked?.Invoke());
            _editButton = Action(actions, () => EditLineupClicked?.Invoke());
            _refreshButton = Action(actions, () => RefreshClicked?.Invoke());

            _seasonEndButton = Action(actions, () => SeasonEndClicked?.Invoke());
            UiKit.SetSmallButtonAccent(_seasonEndButton, true);
            _seasonEndButton.style.display = DisplayStyle.None;

            // State-hash agreement line (8.4b): the server's canonical whole-world hash, kept as one
            // quiet diagnostic line under the actions instead of a card of its own.
            VisualElement hashRow = UiKit.Row();
            hashRow.style.marginTop = UiKit.SpaceSm;
            head.Add(hashRow);
            _stateHashValue = UiKit.Caption(string.Empty);
            _stateHashValue.style.fontSize = 11;
            _stateHashValue.style.flexGrow = 1f;
            _stateHashValue.style.flexShrink = 1f;
            _stateHashValue.style.overflow = Overflow.Hidden;
            _stateHashValue.style.textOverflow = TextOverflow.Ellipsis;
            _stateHashValue.style.whiteSpace = WhiteSpace.NoWrap;
            hashRow.Add(_stateHashValue);
            _verifyButton = UiKit.SmallButton(string.Empty, () => VerifyStateClicked?.Invoke(), 100f);
            hashRow.Add(_verifyButton);

            // ---- tabs -------------------------------------------------------------------------
            VisualElement tabRow = UiKit.Toolbar();
            string[] tabKeys = { "league.tab.table", "league.tab.fixtures" };
            _tabs = new Button[tabKeys.Length];
            for (int i = 0; i < tabKeys.Length; i++)
            {
                int index = i;
                _tabs[i] = UiKit.TabButton(_tr(tabKeys[i]), () => ShowTab(index));
                tabRow.Add(_tabs[i]);
            }
            _tabs[_tabs.Length - 1].style.marginRight = 0;
            col.Add(tabRow);

            // ---- standings --------------------------------------------------------------------
            VisualElement standingsSection = UiKit.Panel(grow: true);
            _standingsHeader = new VisualElement();
            _standingsHeader.style.flexShrink = 0f;
            standingsSection.Add(_standingsHeader);
            _standings = UiKit.ListScroll();
            standingsSection.Add(_standings);

            // ---- schedule ---------------------------------------------------------------------
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

        public void SetHeader(string text) => _title.text = text;
        public void SetBanner(string text) => _banner.text = text;

        public void SetReadyButton(string text, bool enabled)
        {
            _readyButton.text = text;
            _readyButton.SetEnabled(enabled);
        }

        public void SetAdvance(bool visible, bool enabled)
        {
            _advanceButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _advanceButton.SetEnabled(enabled);
        }

        public void SetEditEnabled(bool enabled) => _editButton.SetEnabled(enabled);

        /// <summary>The end-of-season summary button (8.7b) — shown once every fixture has been played.</summary>
        public void SetSeasonEnd(bool visible, bool enabled)
        {
            _seasonEndButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _seasonEndButton.SetEnabled(enabled);
        }

        /// <summary>The state-hash agreement line (hash · players · rounds), preformatted by the presenter.</summary>
        public void SetStateHash(string text) => _stateHashValue.text = text;

        /// <summary>The lineup/tactic/plan editor button is wired in the editor increment (8.3b task 10);
        /// hidden until then.</summary>
        public void SetEditVisible(bool visible) =>
            _editButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetStandings(IReadOnlyList<StandingRowVm> rows)
        {
            _standings.Clear();
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
                _standings.Add(OnlineTableKit.StandingsRow(rows[i], i));
        }

        public void SetFixtures(IReadOnlyList<FixtureGroupVm> groups)
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
                        r, index++, _tr("season.vs"), _tr("season.play_live"),
                        id => FixtureClicked?.Invoke(id),
                        id => PlayLiveClicked?.Invoke(id)));
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
            _readyButton.SetEnabled(!busy);
            _advanceButton.SetEnabled(!busy);
            _editButton.SetEnabled(!busy);
            _refreshButton.SetEnabled(!busy);
            _verifyButton.SetEnabled(!busy);
            _seasonEndButton.SetEnabled(!busy);
        }

        public void UpdateTexts()
        {
            _advanceButton.text = _tr("season.advance");
            _editButton.text = _tr("season.edit_lineup");
            _refreshButton.text = _tr("season.refresh");
            _seasonEndButton.text = _tr("season.season_end");
            _verifyButton.text = _tr("season.verify_state");
            _backButton.text = _tr("common.back");
            for (int i = 0; i < _tabs.Length; i++)
                _tabs[i].text = _tr(i == 0 ? "league.tab.table" : "league.tab.fixtures");
            RebuildStandingsHeader();
        }

        private void RebuildStandingsHeader()
        {
            _standingsHeader.Clear();
            _standingsHeader.Add(OnlineTableKit.StandingsHeader(_tr));
        }
    }
}
