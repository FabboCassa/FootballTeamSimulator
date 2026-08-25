using System;
using System.Collections.Generic;
using UnityEngine;
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
    /// Rebuilt on the shared page scaffold (UI pass on the online flow): the digest is a scrolling stack of
    /// panels, the navigation is one wrapping row of compact buttons instead of a column of full-width
    /// menu bars, and Back / Refresh / Enrol live in the footer like every other screen.
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

        private readonly VisualElement _navCard;
        private readonly Label _navCaption;
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

        // What the navigation plate currently offers — the plate hides itself when nothing is on it.
        private bool _navActionsVisible;
        private bool _autoEnrolVisible;
        private bool _devToolsVisible;

        public RankedHomeView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthMedium);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            col.Add(_title);
            _subtitle = UiKit.HelpText(string.Empty);
            col.Add(_subtitle);

            ScrollView body = UiKit.ListScroll();
            col.Add(body);

            // Info / not-enrolled panel.
            _infoCard = UiKit.Panel();
            body.Add(_infoCard);
            _info = UiKit.PanelLine(string.Empty);
            _infoCard.Add(_info);

            // --- your day -------------------------------------------------------------------
            _todayCard = UiKit.Panel();
            _todayCard.style.display = DisplayStyle.None;
            body.Add(_todayCard);
            _todayCaption = Caption(_todayCard);
            _position = Line(_todayCard);
            _position.style.fontSize = 16;
            _position.style.unityFontStyleAndWeight = FontStyle.Bold;
            _lastResult = Line(_todayCard);
            _nextMatch = Line(_todayCard);
            _countdown = Line(_todayCard);
            _countdown.style.color = UiKit.Amber;
            _countdown.style.unityFontStyleAndWeight = FontStyle.Bold;

            // --- to do ----------------------------------------------------------------------
            _todoCard = UiKit.Panel();
            _todoCard.style.display = DisplayStyle.None;
            body.Add(_todoCard);
            _todoCaption = Caption(_todoCard);
            _todoList = new VisualElement();
            _todoCard.Add(_todoList);
            _confirmButton = UiKit.SmallButton(string.Empty, () => ConfirmClicked?.Invoke(), 220f);
            _confirmButton.style.marginLeft = 0;
            _confirmButton.style.marginTop = UiKit.SpaceSm;
            _confirmButton.style.alignSelf = Align.FlexStart;
            UiKit.SetSmallButtonAccent(_confirmButton, true);
            _confirmButton.style.display = DisplayStyle.None;
            _todoCard.Add(_confirmButton);

            // --- your inputs ----------------------------------------------------------------
            _inputsCard = UiKit.Panel();
            _inputsCard.style.display = DisplayStyle.None;
            body.Add(_inputsCard);
            _inputsCaption = Caption(_inputsCard);
            _lineupLine = Line(_inputsCard);
            _trainingLine = Line(_inputsCard);

            // --- market ---------------------------------------------------------------------
            _marketCard = UiKit.Panel();
            _marketCard.style.display = DisplayStyle.None;
            body.Add(_marketCard);
            _marketCaption = Caption(_marketCard);
            _windowLine = Line(_marketCard);
            _budgetLine = Line(_marketCard);
            _offersLine = Line(_marketCard);

            // --- navigation -----------------------------------------------------------------
            _navCard = UiKit.Panel();
            body.Add(_navCard);
            _navCaption = Caption(_navCard);
            VisualElement nav = UiKit.Toolbar();
            nav.style.marginBottom = 0;
            _navCard.Add(nav);

            _seasonButton = Nav(nav, () => SeasonClicked?.Invoke());
            UiKit.SetSmallButtonAccent(_seasonButton, true);
            _lineupButton = Nav(nav, () => LineupClicked?.Invoke());
            _trainingButton = Nav(nav, () => TrainingClicked?.Invoke());
            _marketButton = Nav(nav, () => MarketClicked?.Invoke());
            _leaderboardButton = Nav(nav, () => LeaderboardClicked?.Invoke());
            _autoEnrolButton = Nav(nav, () => AutoEnrolClicked?.Invoke());
            // Dev-only: fill the placement group with bots + start the season (hidden unless DevFlags).
            _fillDevButton = Nav(nav, () => FillDevClicked?.Invoke());
            _navCard.style.display = DisplayStyle.None;

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 0f;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            VisualElement footer = UiKit.FooterBar();
            _backButton = UiKit.FooterButton(string.Empty, () => BackClicked?.Invoke());
            footer.Add(_backButton);
            _refreshButton = UiKit.FooterButton(string.Empty, () => RefreshClicked?.Invoke());
            _refreshButton.style.display = DisplayStyle.None;
            footer.Add(_refreshButton);
            _enrolButton = UiKit.FooterPrimaryButton(string.Empty, () => EnrolClicked?.Invoke());
            footer.Add(_enrolButton);
            col.Add(footer);

            UpdateTexts();
        }

        private static Label Caption(VisualElement parent)
        {
            Label label = UiKit.SectionLabel(string.Empty);
            label.style.marginTop = 0;
            parent.Add(label);
            return label;
        }

        private static Label Line(VisualElement parent)
        {
            Label label = UiKit.PanelLine(string.Empty);
            parent.Add(label);
            return label;
        }

        private static Button Nav(VisualElement parent, Action onClick)
        {
            Button b = UiKit.SmallButton(string.Empty, onClick, 150f);
            b.style.marginLeft = 0;
            b.style.marginRight = 6;
            b.style.marginBottom = 4;
            b.style.display = DisplayStyle.None;
            parent.Add(b);
            return b;
        }

        // --- content ---------------------------------------------------------------------------

        public void SetSubtitle(string text)
        {
            _subtitle.text = text ?? string.Empty;
            _subtitle.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetInfo(string text)
        {
            _info.text = text;
            _infoCard.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>The "your day" panel: where you stand, how the last match went, who is next and when.
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
                Label done = UiKit.PanelLine(allDoneText);
                done.style.color = UiKit.Positive;
                _todoList.Add(done);
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                RankedTodoRowVm row = rows[i];

                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.Center;
                line.style.minHeight = 38;
                line.style.flexShrink = 0f;
                line.style.paddingLeft = UiKit.SpaceSm;
                line.style.paddingRight = UiKit.SpaceSm;
                OnlineTableKit.Stripe(line, false, i);

                var label = new Label(row.Label ?? string.Empty);
                label.style.fontSize = 14;
                label.style.color = UiKit.TextPrimary;
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.minWidth = 0f;
                label.style.whiteSpace = WhiteSpace.Normal;
                line.Add(label);

                if (!string.IsNullOrEmpty(row.ActionLabel))
                {
                    int kind = row.Kind;
                    Button go = UiKit.SmallButton(row.ActionLabel, () => TodoClicked?.Invoke(kind), 90f);
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

        /// <summary>The stored-inputs panel: lineup readiness (green when confirmed) + training focus.</summary>
        public void SetInputs(bool visible, string lineupLine, bool lineupConfirmed, string trainingLine)
        {
            _inputsCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _lineupLine.text = lineupLine;
            _lineupLine.style.color = lineupConfirmed ? UiKit.Positive : UiKit.TextMuted;
            _trainingLine.text = trainingLine;
        }

        /// <summary>The market panel: window state (green open / muted shut), budget, offers + lots.</summary>
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
            _navActionsVisible = visible;
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _seasonButton.style.display = display;
            _lineupButton.style.display = display;
            _trainingButton.style.display = display;
            _marketButton.style.display = display;
            _leaderboardButton.style.display = display;
            _refreshButton.style.display = display;
            RefreshNavVisibility();
        }

        public void SetAutoEnrol(bool visible, string label)
        {
            _autoEnrolVisible = visible;
            _autoEnrolButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _autoEnrolButton.text = label;
            RefreshNavVisibility();
        }

        /// <summary>The navigation panel disappears entirely when it has nothing to offer (not enrolled yet,
        /// dev tools off) instead of leaving an empty plate on the page.</summary>
        private void RefreshNavVisibility()
        {
            bool any = _navActionsVisible || _autoEnrolVisible || _devToolsVisible;
            _navCard.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
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
        public void SetDevToolsVisible(bool visible)
        {
            _devToolsVisible = visible;
            _fillDevButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshNavVisibility();
        }

        public void UpdateTexts()
        {
            _title.text = _tr("ranked.title");
            _todayCaption.text = _tr("ranked.today.caption");
            _todoCaption.text = _tr("ranked.today.todo_caption");
            _inputsCaption.text = _tr("ranked.today.inputs_caption");
            _marketCaption.text = _tr("ranked.today.market_caption");
            _navCaption.text = _tr("ranked.nav_caption");
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
