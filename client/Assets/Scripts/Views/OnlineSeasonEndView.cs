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
    /// Dumb view for the ONLINE (private-league) season-end summary (task 8.7b): a completion banner, the
    /// awards (champion / top scorer / best defence / wooden spoon), the final table, and — for the league
    /// creator once the season is complete — a "new season" button that resets the league back into the
    /// draft. Distinct from the single-player <see cref="SeasonEndView"/> (2.7). No logic: the presenter
    /// fills it from the summary DTO and drives the API.
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
        private readonly VisualElement _standings;
        private readonly Button _newSeasonButton;
        private readonly Label _status;
        private readonly Button _backButton;

        public OnlineSeasonEndView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.PageColumn(UiKit.WidthMedium);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            col.Add(_title);

            var card = UiKit.Card();
            col.Add(card);
            _banner = UiKit.Subtitle(string.Empty);
            _banner.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_banner);
            _totals = UiKit.Caption(string.Empty);
            _totals.style.color = UiKit.TextMuted;
            _totals.style.marginTop = UiKit.SpaceXs;
            _totals.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_totals);

            _awardsCaption = UiKit.Subtitle(string.Empty);
            _awardsCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_awardsCaption);
            _awards = new VisualElement();
            col.Add(_awards);

            _standingsCaption = UiKit.Subtitle(string.Empty);
            _standingsCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_standingsCaption);
            _standings = new VisualElement();
            col.Add(_standings);

            _newSeasonButton = UiKit.PrimaryButton(string.Empty, () => NewSeasonClicked?.Invoke());
            _newSeasonButton.style.marginTop = UiKit.SpaceMd;
            _newSeasonButton.style.display = DisplayStyle.None;
            col.Add(_newSeasonButton);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceMd;
            col.Add(_backButton);

            UpdateTexts();
        }

        public void SetHeader(string text) => _title.text = text;
        public void SetBanner(string text) => _banner.text = text;
        public void SetTotals(string text) => _totals.text = text;

        public void SetAwards(IReadOnlyList<AwardRowVm> rows)
        {
            _awards.Clear();
            if (rows == null) return;
            foreach (AwardRowVm vm in rows)
                _awards.Add(AwardRow(vm));
        }

        public void SetStandings(IReadOnlyList<StandingRowVm> rows)
        {
            _standings.Clear();
            _standings.Add(StandingHeader());
            if (rows == null) return;
            foreach (StandingRowVm vm in rows)
                _standings.Add(StandingRow(vm));
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
        }

        // --- rows -------------------------------------------------------------------------------

        private static VisualElement AwardRow(AwardRowVm vm)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 34;
            row.style.marginBottom = 3;
            row.style.paddingLeft = 10;
            row.style.paddingRight = 10;
            row.style.backgroundColor = vm.Highlight ? UiKit.SurfaceAlt : new Color(1f, 1f, 1f, 0.04f);
            UiKit.Round(row, 6);

            var label = new Label(vm.Label);
            label.style.fontSize = 12;
            label.style.color = UiKit.TextMuted;
            label.style.width = 150;
            label.style.flexShrink = 0f;
            row.Add(label);

            var winner = new Label(vm.Winner);
            winner.style.fontSize = 13;
            winner.style.color = vm.Highlight ? UiKit.Accent : UiKit.TextPrimary;
            winner.style.unityFontStyleAndWeight = FontStyle.Bold;
            winner.style.flexGrow = 1f;
            winner.style.flexShrink = 1f;
            winner.style.overflow = Overflow.Hidden;
            winner.style.textOverflow = TextOverflow.Ellipsis;
            winner.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(winner);

            var detail = new Label(vm.Detail);
            detail.style.fontSize = 12;
            detail.style.color = UiKit.TextMuted;
            detail.style.unityTextAlign = TextAnchor.MiddleRight;
            detail.style.flexShrink = 0f;
            row.Add(detail);

            return row;
        }

        private VisualElement StandingHeader()
        {
            var row = TableRow(false);
            row.Add(Cell("#", 24, TextAnchor.MiddleLeft, UiKit.TextMuted, bold: true));
            row.Add(Cell(_tr("season.col_club"), 0, TextAnchor.MiddleLeft, UiKit.TextMuted, bold: true, grow: true));
            row.Add(Cell(_tr("season.col_p"), 24, TextAnchor.MiddleRight, UiKit.TextMuted, bold: true));
            row.Add(Cell(_tr("season.col_w"), 22, TextAnchor.MiddleRight, UiKit.TextMuted, bold: true));
            row.Add(Cell(_tr("season.col_d"), 22, TextAnchor.MiddleRight, UiKit.TextMuted, bold: true));
            row.Add(Cell(_tr("season.col_l"), 22, TextAnchor.MiddleRight, UiKit.TextMuted, bold: true));
            row.Add(Cell(_tr("season.col_gd"), 30, TextAnchor.MiddleRight, UiKit.TextMuted, bold: true));
            row.Add(Cell(_tr("season.col_pts"), 30, TextAnchor.MiddleRight, UiKit.TextMuted, bold: true));
            return row;
        }

        private static VisualElement StandingRow(StandingRowVm vm)
        {
            var row = TableRow(vm.IsYours);
            row.Add(Cell(vm.Pos.ToString(), 24, TextAnchor.MiddleLeft, UiKit.TextMuted));
            row.Add(Cell(vm.ClubName, 0, TextAnchor.MiddleLeft, UiKit.TextPrimary, grow: true));
            row.Add(Cell(vm.Played.ToString(), 24, TextAnchor.MiddleRight, UiKit.TextPrimary));
            row.Add(Cell(vm.Won.ToString(), 22, TextAnchor.MiddleRight, UiKit.TextMuted));
            row.Add(Cell(vm.Drawn.ToString(), 22, TextAnchor.MiddleRight, UiKit.TextMuted));
            row.Add(Cell(vm.Lost.ToString(), 22, TextAnchor.MiddleRight, UiKit.TextMuted));
            row.Add(Cell(Signed(vm.GoalDifference), 30, TextAnchor.MiddleRight, UiKit.TextMuted));
            row.Add(Cell(vm.Points.ToString(), 30, TextAnchor.MiddleRight, UiKit.TextPrimary, bold: true));
            return row;
        }

        private static VisualElement TableRow(bool highlight)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 28;
            row.style.marginBottom = 2;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.backgroundColor = highlight ? UiKit.SurfaceAlt : new Color(1f, 1f, 1f, 0.04f);
            UiKit.Round(row, 6);
            return row;
        }

        private static Label Cell(string text, float width, TextAnchor align, Color color, bool bold = false, bool grow = false)
        {
            var label = new Label(text);
            label.style.fontSize = 12;
            label.style.color = color;
            label.style.unityTextAlign = align;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (grow)
            {
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.whiteSpace = WhiteSpace.NoWrap;
            }
            else
            {
                label.style.width = width;
                label.style.flexShrink = 0f;
            }
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    }
}
