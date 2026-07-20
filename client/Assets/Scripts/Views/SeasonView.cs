using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A standings row, preformatted by the presenter (task 8.3b).</summary>
    public sealed class StandingRowVm
    {
        public int Pos;
        public string ClubName;
        public int Played, Won, Drawn, Lost, GoalDifference, Points;
        public bool IsYours;
    }

    /// <summary>A fixture row: id (for the replay), the two clubs, and the score once played.</summary>
    public sealed class SeasonFixtureRowVm
    {
        public string FixtureId;
        public string HomeName;
        public string AwayName;
        public bool Played;
        public int HomeGoals, AwayGoals;
        public bool IsYours;
    }

    /// <summary>A matchday: a label + its fixtures.</summary>
    public sealed class FixtureGroupVm
    {
        public string RoundLabel;
        public IReadOnlyList<SeasonFixtureRowVm> Rows;
    }

    /// <summary>
    /// Dumb view for a private league's season (task 8.3b): a state banner, the ready/advance/refresh
    /// actions, the standings table and the full schedule (played fixtures are tappable to open the
    /// replay). No logic — the presenter fills it from the season DTO and drives the API.
    /// </summary>
    public sealed class SeasonView
    {
        public event Action ReadyToggleClicked;
        public event Action AdvanceClicked;
        public event Action RefreshClicked;
        public event Action EditLineupClicked;
        public event Action VerifyStateClicked;
        public event Action<string> FixtureClicked; // fixture id (played only)
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Label _banner;
        private readonly Button _readyButton;
        private readonly Button _advanceButton;
        private readonly Button _editButton;
        private readonly Button _refreshButton;
        private readonly Label _stateHashCaption;
        private readonly Label _stateHashValue;
        private readonly Button _verifyButton;
        private readonly Label _standingsCaption;
        private readonly VisualElement _standings;
        private readonly Label _scheduleCaption;
        private readonly VisualElement _schedule;
        private readonly Label _status;
        private readonly Button _backButton;

        public SeasonView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.CenteredColumn(720f);
            Root.Add(col);

            _title = UiKit.Header(string.Empty);
            col.Add(_title);

            var card = UiKit.Card();
            col.Add(card);
            _banner = UiKit.Subtitle(string.Empty);
            _banner.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_banner);

            var actions = UiKit.Row();
            actions.style.flexWrap = Wrap.Wrap;
            actions.style.marginTop = UiKit.SpaceXs;
            card.Add(actions);
            _readyButton = UiKit.PrimaryButton(string.Empty, () => ReadyToggleClicked?.Invoke());
            actions.Add(_readyButton);
            _advanceButton = UiKit.MenuButton(string.Empty, () => AdvanceClicked?.Invoke());
            _advanceButton.style.marginLeft = UiKit.SpaceXs;
            actions.Add(_advanceButton);
            _editButton = UiKit.MenuButton(string.Empty, () => EditLineupClicked?.Invoke());
            _editButton.style.marginLeft = UiKit.SpaceXs;
            actions.Add(_editButton);
            _refreshButton = UiKit.MenuButton(string.Empty, () => RefreshClicked?.Invoke());
            _refreshButton.style.marginLeft = UiKit.SpaceXs;
            actions.Add(_refreshButton);

            // State-hash agreement panel (8.4b): the server's canonical whole-world hash. It CHANGES
            // after a round of play (condition + development evolved) — the client↔server ✅.
            var hashCard = UiKit.Card();
            hashCard.style.marginTop = UiKit.SpaceMd;
            col.Add(hashCard);
            _stateHashCaption = UiKit.Caption(string.Empty);
            _stateHashCaption.style.color = UiKit.TextMuted;
            hashCard.Add(_stateHashCaption);
            _stateHashValue = UiKit.Caption(string.Empty);
            _stateHashValue.style.whiteSpace = WhiteSpace.Normal;
            _stateHashValue.style.marginTop = UiKit.SpaceXs;
            hashCard.Add(_stateHashValue);
            _verifyButton = UiKit.MenuButton(string.Empty, () => VerifyStateClicked?.Invoke());
            _verifyButton.style.marginTop = UiKit.SpaceXs;
            hashCard.Add(_verifyButton);

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

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceMd;
            col.Add(_backButton);

            UpdateTexts();
            _standingsCaption.text = _tr("season.standings");
            _scheduleCaption.text = _tr("season.schedule");
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

        /// <summary>The state-hash agreement line (hash · players · rounds), preformatted by the presenter.</summary>
        public void SetStateHash(string text) => _stateHashValue.text = text;

        /// <summary>The lineup/tactic/plan editor button is wired in the editor increment (8.3b task 10);
        /// hidden until then.</summary>
        public void SetEditVisible(bool visible) =>
            _editButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetStandings(IReadOnlyList<StandingRowVm> rows)
        {
            _standings.Clear();
            _standings.Add(StandingHeader());
            if (rows == null) return;
            foreach (StandingRowVm vm in rows)
                _standings.Add(StandingRow(vm));
        }

        public void SetFixtures(IReadOnlyList<FixtureGroupVm> groups)
        {
            _schedule.Clear();
            if (groups == null) return;
            foreach (FixtureGroupVm g in groups)
            {
                var header = UiKit.Caption(g.RoundLabel);
                header.style.marginTop = UiKit.SpaceSm;
                header.style.color = UiKit.TextMuted;
                _schedule.Add(header);
                if (g.Rows == null) continue;
                foreach (SeasonFixtureRowVm r in g.Rows)
                    _schedule.Add(FixtureRow(r));
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
        }

        public void UpdateTexts()
        {
            _advanceButton.text = _tr("season.advance");
            _editButton.text = _tr("season.edit_lineup");
            _refreshButton.text = _tr("season.refresh");
            _stateHashCaption.text = _tr("season.state_hash_caption");
            _verifyButton.text = _tr("season.verify_state");
            _backButton.text = _tr("common.back");
        }

        // --- rows -------------------------------------------------------------------------------

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

        private VisualElement StandingRow(StandingRowVm vm)
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

        private VisualElement FixtureRow(SeasonFixtureRowVm vm)
        {
            var row = TableRow(vm.IsYours);
            if (vm.Played) UiKit.EnsureTapTarget(row);

            var home = Cell(vm.HomeName, 0, TextAnchor.MiddleRight, UiKit.TextPrimary, grow: true);
            row.Add(home);

            string mid = vm.Played ? $"{vm.HomeGoals}–{vm.AwayGoals}" : _tr("season.vs");
            var score = Cell(mid, 52, TextAnchor.MiddleCenter, vm.Played ? UiKit.TextPrimary : UiKit.TextMuted, bold: vm.Played);
            row.Add(score);

            var away = Cell(vm.AwayName, 0, TextAnchor.MiddleLeft, UiKit.TextPrimary, grow: true);
            row.Add(away);

            if (vm.Played && !string.IsNullOrEmpty(vm.FixtureId))
            {
                string id = vm.FixtureId;
                row.RegisterCallback<ClickEvent>(_ => FixtureClicked?.Invoke(id));
            }
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
            label.pickingMode = PickingMode.Ignore; // the row handles the click
            return label;
        }

        private static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    }
}
