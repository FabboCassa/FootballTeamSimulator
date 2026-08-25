using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One award line, fully preformatted by the presenter (task 8.7b): the award's name, who
    /// won it, and the metric that earned it (points / goals conceded / goals scored).</summary>
    public sealed class AwardRowVm
    {
        public string Label;
        public string Winner;
        public string Detail;
        /// <summary>Tint the row with the accent colour (used for the champion).</summary>
        public bool Highlight;
    }

    /// <summary>
    /// Dumb view for the ONLINE (private-league) season-end summary (task 8.7b), on the shared page
    /// scaffold: a completion panel, the awards, the final table (same table as the season screen) and a
    /// footer carrying Back plus — for the creator — the "new season" action. Distinct from the
    /// single-player <see cref="SeasonEndView"/> (2.7). No logic: the presenter fills it.
    /// </summary>
    public sealed class OnlineSeasonEndView
    {
        public event Action NewSeasonClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Label _banner;
        private readonly Label _totals;
        private readonly Label _awardsCaption;
        private readonly VisualElement _awards;
        private readonly Label _standingsCaption;
        private readonly VisualElement _standingsHeader;
        private readonly ScrollView _standings;
        private readonly Button _newSeasonButton;
        private readonly Label _status;
        private readonly Button _backButton;

        public OnlineSeasonEndView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            _title.style.marginBottom = UiKit.SpaceSm;
            col.Add(_title);

            // ---- headline -----------------------------------------------------------------------
            VisualElement head = UiKit.Panel();
            col.Add(head);
            _banner = UiKit.PanelLine(string.Empty);
            _banner.style.fontSize = 16;
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.Add(_banner);
            _totals = UiKit.Caption(string.Empty);
            _totals.style.whiteSpace = WhiteSpace.Normal;
            head.Add(_totals);

            // ---- awards -------------------------------------------------------------------------
            VisualElement awardsPanel = UiKit.Panel();
            col.Add(awardsPanel);
            _awardsCaption = UiKit.SectionLabel(string.Empty);
            _awardsCaption.style.marginTop = 0;
            awardsPanel.Add(_awardsCaption);
            _awards = new VisualElement();
            awardsPanel.Add(_awards);

            // ---- final table --------------------------------------------------------------------
            VisualElement tablePanel = UiKit.Panel(grow: true);
            col.Add(tablePanel);
            _standingsCaption = UiKit.SectionLabel(string.Empty);
            _standingsCaption.style.marginTop = 0;
            tablePanel.Add(_standingsCaption);
            _standingsHeader = new VisualElement();
            _standingsHeader.style.flexShrink = 0f;
            tablePanel.Add(_standingsHeader);
            _standings = UiKit.ListScroll();
            tablePanel.Add(_standings);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 0f;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            VisualElement footer = UiKit.FooterBar();
            _backButton = UiKit.FooterButton(string.Empty, () => BackClicked?.Invoke());
            footer.Add(_backButton);
            _newSeasonButton = UiKit.FooterPrimaryButton(string.Empty, () => NewSeasonClicked?.Invoke());
            _newSeasonButton.style.display = DisplayStyle.None;
            footer.Add(_newSeasonButton);
            col.Add(footer);

            RebuildStandingsHeader();
            UpdateTexts();
        }

        public void SetHeader(string text) => _title.text = text;
        public void SetBanner(string text) => _banner.text = text;
        public void SetTotals(string text) => _totals.text = text;

        public void SetAwards(IReadOnlyList<AwardRowVm> rows)
        {
            _awards.Clear();
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
                _awards.Add(AwardRow(rows[i], i));
        }

        public void SetStandings(IReadOnlyList<StandingRowVm> rows)
        {
            _standings.Clear();
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
                _standings.Add(OnlineTableKit.StandingsRow(rows[i], i));
        }

        /// <summary>The creator-only "new season" action — shown once the season is complete.</summary>
        public void SetNewSeason(bool visible, bool enabled)
        {
            _newSeasonButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _newSeasonButton.SetEnabled(enabled);
        }

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message;
            _status.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _status.style.display = DisplayStyle.Flex;
        }

        public void ClearStatus() => _status.style.display = DisplayStyle.None;

        public void SetBusy(bool busy) => _newSeasonButton.SetEnabled(!busy);

        public void UpdateTexts()
        {
            _awardsCaption.text = _tr("seasonend.awards");
            _standingsCaption.text = _tr("seasonend.final_table");
            _newSeasonButton.text = _tr("seasonend.new_season");
            _backButton.text = _tr("common.back");
            RebuildStandingsHeader();
        }

        private void RebuildStandingsHeader()
        {
            _standingsHeader.Clear();
            _standingsHeader.Add(OnlineTableKit.StandingsHeader(_tr));
        }

        // --- rows -------------------------------------------------------------------------------

        private static VisualElement AwardRow(AwardRowVm vm, int index)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = OnlineTableKit.RowHeight + 2f;
            row.style.flexShrink = 0f;
            row.style.paddingLeft = UiKit.SpaceSm;
            row.style.paddingRight = UiKit.SpaceSm;
            OnlineTableKit.Stripe(row, vm.Highlight, index);

            row.Add(OnlineTableKit.Cell(vm.Label, 190f, UiKit.TextMuted, TextAnchor.MiddleLeft, 12, false));

            Label winner = OnlineTableKit.Cell(
                vm.Winner, 0f, vm.Highlight ? UiKit.Accent : UiKit.TextPrimary, TextAnchor.MiddleLeft, 14, true);
            winner.style.flexGrow = 1f;
            winner.style.flexShrink = 1f;
            winner.style.minWidth = 0f;
            row.Add(winner);

            row.Add(OnlineTableKit.Cell(vm.Detail, 140f, UiKit.TextMuted, TextAnchor.MiddleRight, 13, false));
            return row;
        }
    }
}
