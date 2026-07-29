using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One row of the "what do I have to do today" list. <see cref="Kind"/> mirrors the server's
    /// RankedTodoKind so the presenter knows which screen the row's button opens.</summary>
    public sealed class RankedTodoRowVm
    {
        public int Kind;
        public string Label;
        public string ActionLabel;
    }

    /// <summary>
    /// Dumb view for the ranked home, which since Phase 9.4 IS the DAILY DIGEST: one screen that answers
    /// "what do I need to do today?" — your standing, the last result, the next kickoff, whether your inputs
    /// are ready, the market, and a short prioritised to-do list with a one-tap "confirm matchday".
    ///
    /// Still the entry point for someone who has never joined (the enrol button) and the hub for the other
    /// ranked screens (season, lineup, training, market, leaderboard). No logic, no Sim.Core, no Services:
    /// events + a translate delegate, driven by <c>RankedHomeScreenPresenter</c>.
    /// </summary>
    public sealed class RankedHomeView
    {
        public event Action EnrolClicked;
        public event Action ConfirmClicked;
        public event Action SeasonClicked;
        public event Action LineupClicked;
        public event Action TrainingClicked;
        public event Action MarketClicked;
        public event Action LeaderboardClicked;
        public event Action AutoEnrolClicked;
        public event Action RefreshClicked;
        public event Action<int> TodoClicked;      // RankedTodoKind
        public event Action FillDevClicked;        // dev-only
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Label _subtitle;

        private readonly VisualElement _infoCard;
        private readonly Label _info;

        private readonly VisualElement _todayCard;
        private readonly Label _todayCaption;
        private readonly Label _position;
        private readonly Label _lastResult;
        private readonly Label _nextMatch;
        private readonly Label _countdown;

        private readonly VisualElement _todoCard;
        private readonly Label _todoCaption;
        private readonly VisualElement _todoList;
        private readonly Button _confirmButton;

        private readonly VisualElement _inputsCard;
        private readonly Label _inputsCaption;
        private readonly Label _lineupLine;
        private readonly Label _trainingLine;

        private readonly VisualElement _marketCard;
        private readonly Label _marketCaption;
        private readonly Label _windowLine;
        private readonly Label _budgetLine;
        private readonly Label _offersLine;

        private readonly Button _enrolButton;
        private readonly Button _seasonButton;
        private readonly Button _lineupButton;
        private readonly Button _trainingButton;
        private readonly Button _marketButton;
        private readonly Button _leaderboardButton;
        private readonly Button _autoEnrolButton;
        private readonly Button _refreshButton;
        private readonly Label _status;
        private readonly Button _fillDevButton;    // dev-only
        private readonly Button _backButton;

        public RankedHomeView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.CenteredColumn(620f);
            Root.Add(col);

            _title = UiKit.Header(string.Empty);
            col.Add(_title);
            _subtitle = UiKit.Caption(string.Empty);
            _subtitle.style.whiteSpace = WhiteSpace.Normal;
            col.Add(_subtitle);

            // Info / not-enrolled card.
            _infoCard = UiKit.Card();
            col.Add(_infoCard);
            _info = UiKit.Caption(string.Empty);
            _info.style.whiteSpace = WhiteSpace.Normal;
            _infoCard.Add(_info);

            // --- your day -------------------------------------------------------------------
            _todayCard = UiKit.Card();
            _todayCard.style.display = DisplayStyle.None;
            col.Add(_todayCard);
            _todayCaption = UiKit.Subtitle(string.Empty);
            _todayCard.Add(_todayCaption);
            _position = UiKit.Caption(string.Empty);
            _todayCard.Add(_position);
            _lastResult = UiKit.Caption(string.Empty);
            _todayCard.Add(_lastResult);
            _nextMatch = UiKit.Caption(string.Empty);
            _nextMatch.style.whiteSpace = WhiteSpace.Normal;
            _todayCard.Add(_nextMatch);
            _countdown = UiKit.Caption(string.Empty);
            _countdown.style.color = UiKit.Amber;
            _todayCard.Add(_countdown);

            // --- to do ----------------------------------------------------------------------
            _todoCard = UiKit.Card();
            _todoCard.style.display = DisplayStyle.None;
            col.Add(_todoCard);
            _todoCaption = UiKit.Subtitle(string.Empty);
            _todoCard.Add(_todoCaption);
            _todoList = new VisualElement();
            _todoCard.Add(_todoList);
            _confirmButton = UiKit.PrimaryButton(string.Empty, () => ConfirmClicked?.Invoke());
            _confirmButton.style.marginTop = UiKit.SpaceSm;
            _confirmButton.style.display = DisplayStyle.None;
            _todoCard.Add(_confirmButton);

            // --- your inputs ----------------------------------------------------------------
            _inputsCard = UiKit.Card();
            _inputsCard.style.display = DisplayStyle.None;
            col.Add(_inputsCard);
            _inputsCaption = UiKit.Subtitle(string.Empty);
            _inputsCard.Add(_inputsCaption);
            _lineupLine = UiKit.Caption(string.Empty);
            _inputsCard.Add(_lineupLine);
            _trainingLine = UiKit.Caption(string.Empty);
            _inputsCard.Add(_trainingLine);

            // --- market ---------------------------------------------------------------------
            _marketCard = UiKit.Card();
            _marketCard.style.display = DisplayStyle.None;
            col.Add(_marketCard);
            _marketCaption = UiKit.Subtitle(string.Empty);
            _marketCard.Add(_marketCaption);
            _windowLine = UiKit.Caption(string.Empty);
            _marketCard.Add(_windowLine);
            _budgetLine = UiKit.Caption(string.Empty);
            _marketCard.Add(_budgetLine);
            _offersLine = UiKit.Caption(string.Empty);
            _offersLine.style.whiteSpace = WhiteSpace.Normal;
            _marketCard.Add(_offersLine);

            // --- actions --------------------------------------------------------------------
            _enrolButton = UiKit.PrimaryButton(string.Empty, () => EnrolClicked?.Invoke());
            col.Add(_enrolButton);

            _seasonButton = Nav(col, () => SeasonClicked?.Invoke());
            _lineupButton = Nav(col, () => LineupClicked?.Invoke());
            _trainingButton = Nav(col, () => TrainingClicked?.Invoke());
            _marketButton = Nav(col, () => MarketClicked?.Invoke());
            _leaderboardButton = Nav(col, () => LeaderboardClicked?.Invoke());

            _autoEnrolButton = Nav(col, () => AutoEnrolClicked?.Invoke());
            _refreshButton = Nav(col, () => RefreshClicked?.Invoke());

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            // Dev-only: fill the placement group with bots + start the season (hidden unless DevFlags).
            _fillDevButton = UiKit.MenuButton(string.Empty, () => FillDevClicked?.Invoke());
            _fillDevButton.style.marginTop = UiKit.SpaceMd;
            _fillDevButton.style.display = DisplayStyle.None;
            col.Add(_fillDevButton);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceSm;
            col.Add(_backButton);

            UpdateTexts();
        }

        private static Button Nav(VisualElement parent, Action onClick)
        {
            var b = UiKit.MenuButton(string.Empty, onClick);
            b.style.marginTop = UiKit.SpaceXs;
            b.style.display = DisplayStyle.None;
            parent.Add(b);
            return b;
        }

        // --- content ---------------------------------------------------------------------------

        public void SetSubtitle(string text) => _subtitle.text = text;

        public void SetInfo(string text)
        {
            _info.text = text;
            _infoCard.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>The "your day" card: where you stand, how the last match went, who is next and when.
        /// Any empty string hides that line.</summary>
        public void SetToday(bool visible, string position, string lastResult, string nextMatch, string countdown)
        {
            _todayCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            SetLine(_position, position);
            SetLine(_lastResult, lastResult);
            SetLine(_nextMatch, nextMatch);
            SetLine(_countdown, countdown);
        }

        /// <summary>The to-do list. An empty list shows the "all done" line instead of rows.</summary>
        public void SetTodo(bool visible, IReadOnlyList<RankedTodoRowVm> rows, string allDoneText)
        {
            _todoCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _todoList.Clear();

            if (rows == null || rows.Count == 0)
            {
                var done = UiKit.Caption(allDoneText);
                done.style.color = UiKit.Positive;
                done.style.whiteSpace = WhiteSpace.Normal;
                _todoList.Add(done);
                return;
            }

            foreach (RankedTodoRowVm row in rows)
            {
                var line = UiKit.Row();
                line.style.marginTop = UiKit.SpaceXs;

                var label = UiKit.Caption(row.Label);
                label.style.flexGrow = 1f;
                label.style.whiteSpace = WhiteSpace.Normal;
                line.Add(label);

                if (!string.IsNullOrEmpty(row.ActionLabel))
                {
                    int kind = row.Kind;
                    var go = UiKit.MenuButton(row.ActionLabel, () => TodoClicked?.Invoke(kind));
                    go.style.marginLeft = UiKit.SpaceSm;
                    line.Add(go);
                }

                _todoList.Add(line);
            }
        }

        /// <summary>The one-tap "confirm matchday" button.</summary>
        public void SetConfirm(bool visible, string label)
        {
            _confirmButton.text = label;
            _confirmButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The stored-inputs card: lineup readiness (green when confirmed) + training focus.</summary>
        public void SetInputs(bool visible, string lineupLine, bool lineupConfirmed, string trainingLine)
        {
            _inputsCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _lineupLine.text = lineupLine;
            _lineupLine.style.color = lineupConfirmed ? UiKit.Positive : UiKit.TextMuted;
            _trainingLine.text = trainingLine;
        }

        /// <summary>The market card: window state (green open / muted shut), budget, offers + lots.</summary>
        public void SetMarket(bool visible, string windowLine, bool windowOpen, string budgetLine, string offersLine)
        {
            _marketCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _windowLine.text = windowLine;
            _windowLine.style.color = windowOpen ? UiKit.Positive : UiKit.TextMuted;
            _budgetLine.text = budgetLine;
            SetLine(_offersLine, offersLine);
        }

        public void SetEnrolVisible(bool visible) =>
            _enrolButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Shows/hides the whole navigation block (season, lineup, training, market, leaderboard,
        /// refresh) — hidden until the caller is enrolled.</summary>
        public void SetActionsVisible(bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _seasonButton.style.display = display;
            _lineupButton.style.display = display;
            _trainingButton.style.display = display;
            _marketButton.style.display = display;
            _leaderboardButton.style.display = display;
            _refreshButton.style.display = display;
        }

        public void SetAutoEnrol(bool visible, string label)
        {
            _autoEnrolButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _autoEnrolButton.text = label;
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
            _enrolButton.SetEnabled(!busy);
            _confirmButton.SetEnabled(!busy);
            _seasonButton.SetEnabled(!busy);
            _lineupButton.SetEnabled(!busy);
            _trainingButton.SetEnabled(!busy);
            _marketButton.SetEnabled(!busy);
            _leaderboardButton.SetEnabled(!busy);
            _autoEnrolButton.SetEnabled(!busy);
            _refreshButton.SetEnabled(!busy);
            _fillDevButton.SetEnabled(!busy);
        }

        /// <summary>Shows the dev-only "fill with bots" button (DevFlags-gated by the presenter).</summary>
        public void SetDevToolsVisible(bool visible) =>
            _fillDevButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void UpdateTexts()
        {
            _title.text = _tr("ranked.title");
            _todayCaption.text = _tr("ranked.today.caption");
            _todoCaption.text = _tr("ranked.today.todo_caption");
            _inputsCaption.text = _tr("ranked.today.inputs_caption");
            _marketCaption.text = _tr("ranked.today.market_caption");
            _enrolButton.text = _tr("ranked.enrol");
            _seasonButton.text = _tr("ranked.open_season");
            _lineupButton.text = _tr("ranked.lineup.open");
            _trainingButton.text = _tr("ranked.training.open");
            _marketButton.text = _tr("ranked.market.open");
            _leaderboardButton.text = _tr("ranked.board.open");
            _refreshButton.text = _tr("ranked.today.refresh");
            _fillDevButton.text = _tr("ranked.dev_fill");
            _backButton.text = _tr("common.back");
        }

        private static void SetLine(Label label, string text)
        {
            label.text = text ?? string.Empty;
            label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
