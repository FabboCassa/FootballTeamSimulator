using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the ranked season (Phase 9.2): a banner (matchday progress + next kickoff + market
    /// window), the standings table, and the schedule grouped by round. Read-only in this increment (replay
    /// tap + lineup submit land next). No logic; the presenter formats the rows and drives the refresh.
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
        private readonly Label _standingsCaption;
        private readonly VisualElement _standings;
        private readonly Label _scheduleCaption;
        private readonly VisualElement _schedule;
        private readonly Label _status;
        private readonly Button _lineupButton;
        private readonly Button _marketButton;
        private readonly Button _refreshButton;
        private readonly Button _advanceDevButton; // dev-only
        private readonly Button _backButton;

        /// <summary>One standings row (preformatted) + whether it's the caller's club (highlighted).</summary>
        public readonly struct StandingRow
        {
            public readonly string Text;
            public readonly bool IsYou;
            public StandingRow(string text, bool isYou) { Text = text; IsYou = isYou; }
        }

        /// <summary>One schedule line: a round header, an unplayed fixture (label), or a played fixture
        /// (tappable → its <see cref="FixtureId"/> for the replay).</summary>
        public readonly struct ScheduleLine
        {
            public readonly string Text;
            public readonly bool IsHeader;
            public readonly string FixtureId; // non-null = a played fixture, rendered tappable
            public ScheduleLine(string text, bool isHeader, string fixtureId = null)
            {
                Text = text;
                IsHeader = isHeader;
                FixtureId = fixtureId;
            }
        }

        public RankedSeasonView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            col.Add(_title);

            _banner = UiKit.Caption(string.Empty);
            _banner.style.whiteSpace = WhiteSpace.Normal;
            col.Add(_banner);

            _windowBanner = UiKit.Caption(string.Empty);
            _windowBanner.style.whiteSpace = WhiteSpace.Normal;
            _windowBanner.style.color = UiKit.Positive;
            _windowBanner.style.display = DisplayStyle.None;
            col.Add(_windowBanner);

            // Season-end summary (Phase 9.3): during the between-seasons break the server keeps the final
            // table readable, so this card sits above it with the finish + rating swing + any tier move.
            _seasonEndCard = UiKit.Card();
            _seasonEndCard.style.display = DisplayStyle.None;
            col.Add(_seasonEndCard);
            _seasonEndTitle = UiKit.Subtitle(string.Empty);
            _seasonEndTitle.style.whiteSpace = WhiteSpace.Normal;
            _seasonEndCard.Add(_seasonEndTitle);
            _seasonEndDetail = UiKit.Caption(string.Empty);
            _seasonEndDetail.style.whiteSpace = WhiteSpace.Normal;
            _seasonEndCard.Add(_seasonEndDetail);

            _lineupButton = UiKit.PrimaryButton(string.Empty, () => LineupClicked?.Invoke());
            _lineupButton.style.marginTop = UiKit.SpaceSm;
            col.Add(_lineupButton);

            _marketButton = UiKit.MenuButton(string.Empty, () => MarketClicked?.Invoke());
            _marketButton.style.marginTop = UiKit.SpaceXs;
            col.Add(_marketButton);

            _refreshButton = UiKit.MenuButton(string.Empty, () => RefreshClicked?.Invoke());
            _refreshButton.style.marginTop = UiKit.SpaceXs;
            col.Add(_refreshButton);

            _standingsCaption = UiKit.Subtitle(string.Empty);
            _standingsCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_standingsCaption);
            _standings = new VisualElement();
            col.Add(_standings);

            _scheduleCaption = UiKit.Subtitle(string.Empty);
            _scheduleCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_scheduleCaption);
            _schedule = new VisualElement();
            col.Add(_schedule);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            // Dev-only: fast-forward the ranked calendar (hidden unless DevFlags).
            _advanceDevButton = UiKit.MenuButton(string.Empty, () => AdvanceDevClicked?.Invoke());
            _advanceDevButton.style.marginTop = UiKit.SpaceMd;
            _advanceDevButton.style.display = DisplayStyle.None;
            col.Add(_advanceDevButton);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceSm;
            col.Add(_backButton);

            UpdateTexts();
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

        public void SetStandings(IReadOnlyList<StandingRow> rows)
        {
            _standings.Clear();
            if (rows == null) return;
            foreach (var r in rows)
            {
                var label = UiKit.Caption(r.Text);
                label.style.whiteSpace = WhiteSpace.Normal;
                if (r.IsYou) label.style.color = UiKit.Accent;
                _standings.Add(label);
            }
        }

        public void SetSchedule(IReadOnlyList<ScheduleLine> lines)
        {
            _schedule.Clear();
            if (lines == null) return;
            foreach (var line in lines)
            {
                // A played fixture is a tappable button (opens the replay); everything else is a label.
                if (!line.IsHeader && line.FixtureId != null)
                {
                    var id = line.FixtureId;
                    var button = UiKit.MenuButton(line.Text, () => FixtureSelected?.Invoke(id));
                    button.style.marginBottom = UiKit.SpaceXs;
                    _schedule.Add(button);
                    continue;
                }

                var label = line.IsHeader ? UiKit.Subtitle(line.Text) : UiKit.Caption(line.Text);
                label.style.whiteSpace = WhiteSpace.Normal;
                if (line.IsHeader) label.style.marginTop = UiKit.SpaceSm;
                _schedule.Add(label);
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
            _standingsCaption.text = _tr("ranked.standings_caption");
            _scheduleCaption.text = _tr("ranked.schedule_caption");
            _lineupButton.text = _tr("ranked.lineup.open");
            _marketButton.text = _tr("ranked.market.open");
            _refreshButton.text = _tr("ranked.refresh");
            _advanceDevButton.text = _tr("ranked.dev_advance");
            _backButton.text = _tr("common.back");
        }
    }
}
