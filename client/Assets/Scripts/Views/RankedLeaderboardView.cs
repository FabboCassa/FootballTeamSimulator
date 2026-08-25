using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the ladder's ranking screen (Phase 9.3): two tabs — the global LEADERBOARD (every coach
    /// by rating) and the caller's PALMARÈS (rating, career best, seasons played and the award history).
    /// On the shared page scaffold: segmented tabs, one panel of striped rows filling the page, and
    /// Back / Refresh in the footer. No Sim.Core or Services references — primitives only.
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

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _header = UiKit.ScreenTitle(string.Empty);
            col.Add(_header);

            _summary = UiKit.HelpText(string.Empty);
            col.Add(_summary);

            VisualElement tabs = UiKit.Toolbar();
            _tabBoard = UiKit.TabButton(string.Empty, () => TabSelected?.Invoke(0));
            _tabPalmares = UiKit.TabButton(string.Empty, () => TabSelected?.Invoke(1));
            _tabPalmares.style.marginRight = 0;
            tabs.Add(_tabBoard);
            tabs.Add(_tabPalmares);
            col.Add(tabs);

            VisualElement panel = UiKit.Panel(grow: true);
            col.Add(panel);
            _list = UiKit.ListScroll();
            panel.Add(_list);

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
            footer.Add(_refreshButton);
            col.Add(footer);

            SetActiveTab(0);
            UpdateTexts();
        }

        public void SetSummary(string text)
        {
            _summary.text = text ?? string.Empty;
            _summary.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetActiveTab(int tab)
        {
            UiKit.SetTabActive(_tabBoard, tab == 0);
            UiKit.SetTabActive(_tabPalmares, tab == 1);
        }

        public void SetRows(IReadOnlyList<RowVm> rows)
        {
            _list.Clear();
            if (rows == null || rows.Count == 0)
            {
                Label empty = UiKit.PanelLine(_tr("ranked.board.empty"));
                empty.style.color = UiKit.TextMuted;
                _list.Add(empty);
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                RowVm vm = rows[i];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.minHeight = 38;
                row.style.flexShrink = 0f;
                row.style.paddingLeft = UiKit.SpaceSm;
                row.style.paddingRight = UiKit.SpaceSm;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;
                OnlineTableKit.Stripe(row, vm.Highlight, i);

                var title = new Label(vm.Title ?? string.Empty);
                title.style.fontSize = 14;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.color = vm.Highlight ? UiKit.Accent : vm.Trophy ? UiKit.Positive : UiKit.TextPrimary;
                title.style.flexGrow = 1f;
                title.style.flexShrink = 1f;
                title.style.minWidth = 0f;
                title.style.whiteSpace = WhiteSpace.Normal;
                row.Add(title);

                if (!string.IsNullOrEmpty(vm.Detail))
                {
                    var detail = new Label(vm.Detail);
                    detail.style.fontSize = 13;
                    detail.style.color = UiKit.TextMuted;
                    detail.style.unityTextAlign = TextAnchor.MiddleRight;
                    detail.style.flexShrink = 1f;
                    detail.style.maxWidth = Length.Percent(45);
                    detail.style.whiteSpace = WhiteSpace.Normal;
                    detail.style.marginLeft = UiKit.SpaceSm;
                    row.Add(detail);
                }

                _list.Add(row);
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
