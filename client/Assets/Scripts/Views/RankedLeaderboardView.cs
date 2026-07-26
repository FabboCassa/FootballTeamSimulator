using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the ladder's ranking screen (Phase 9.3): two tabs — the global LEADERBOARD (every coach
    /// by rating) and the caller's PALMARÈS (rating, career best, seasons played and the award history).
    /// Same shape as the ranked market screen: the presenter formats every string and hands over rows; the
    /// view only emits events and renders. No Sim.Core or Services references — primitives only.
    /// </summary>
    public sealed class RankedLeaderboardView
    {
        public event Action<int> TabSelected;   // 0 leaderboard, 1 palmarès
        public event Action RefreshClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _header;
        private readonly Label _summary;
        private readonly Button _tabBoard;
        private readonly Button _tabPalmares;
        private readonly Button _refreshButton;
        private readonly ScrollView _list;
        private readonly Label _status;
        private readonly Button _backButton;

        /// <summary>One rendered line: a title, an optional detail, and optional highlighting (the caller's
        /// own row on the leaderboard, or a trophy line in the palmarès).</summary>
        public sealed class RowVm
        {
            public string Title;
            public string Detail;
            public bool Highlight;   // the caller's own row → accent
            public bool Trophy;      // a title/promotion line → positive colour
        }

        public RankedLeaderboardView(Func<string, string> tr)
        {
            _tr = tr;

            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;

            var col = UiKit.CenteredColumn(720f);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.Header(string.Empty);
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            col.Add(_header);

            _summary = UiKit.Caption(string.Empty);
            _summary.style.unityTextAlign = TextAnchor.MiddleCenter;
            _summary.style.whiteSpace = WhiteSpace.Normal;
            col.Add(_summary);

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.justifyContent = Justify.Center;
            tabs.style.flexShrink = 0f;
            tabs.style.marginTop = UiKit.SpaceXs;
            _tabBoard = TabButton(() => TabSelected?.Invoke(0));
            _tabPalmares = TabButton(() => TabSelected?.Invoke(1));
            _refreshButton = TabButton(() => RefreshClicked?.Invoke());
            tabs.Add(_tabBoard);
            tabs.Add(_tabPalmares);
            tabs.Add(_refreshButton);
            col.Add(tabs);

            _list = new ScrollView();
            _list.style.flexGrow = 1f;
            _list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(_list);

            _status = UiKit.Caption(string.Empty);
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceSm;
            col.Add(_backButton);

            UpdateTexts();
        }

        private static Button TabButton(Action onClick)
        {
            var b = UiKit.MenuButton(string.Empty, onClick);
            b.style.marginLeft = 4;
            b.style.marginRight = 4;
            b.style.paddingLeft = UiKit.SpaceSm;
            b.style.paddingRight = UiKit.SpaceSm;
            return b;
        }

        public void SetSummary(string text) => _summary.text = text;

        public void SetActiveTab(int tab)
        {
            _tabBoard.style.backgroundColor = tab == 0 ? UiKit.AccentDark : UiKit.SurfaceAlt;
            _tabPalmares.style.backgroundColor = tab == 1 ? UiKit.AccentDark : UiKit.SurfaceAlt;
        }

        public void SetRows(IReadOnlyList<RowVm> rows)
        {
            _list.Clear();
            if (rows == null || rows.Count == 0)
            {
                _list.Add(UiKit.Caption(_tr("ranked.board.empty")));
                return;
            }

            foreach (var row in rows)
            {
                var card = UiKit.Card();

                var title = UiKit.Caption(row.Title);
                title.style.whiteSpace = WhiteSpace.Normal;
                title.style.color = row.Highlight ? UiKit.Accent : row.Trophy ? UiKit.Positive : UiKit.TextPrimary;
                card.Add(title);

                if (!string.IsNullOrEmpty(row.Detail))
                {
                    var detail = UiKit.Caption(row.Detail);
                    detail.style.whiteSpace = WhiteSpace.Normal;
                    card.Add(detail);
                }

                _list.Add(card);
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
            _tabBoard.SetEnabled(!busy);
            _tabPalmares.SetEnabled(!busy);
        }

        public void UpdateTexts()
        {
            _header.text = _tr("ranked.board.title");
            _tabBoard.text = _tr("ranked.board.tab_leaderboard");
            _tabPalmares.text = _tr("ranked.board.tab_palmares");
            _refreshButton.text = _tr("ranked.refresh");
            _backButton.text = _tr("common.back");
        }
    }
}
