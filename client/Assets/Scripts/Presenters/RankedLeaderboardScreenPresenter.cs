using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// The ladder's ranking screen (Phase 9.3), opened from the ranked home: tab 0 is the GLOBAL LEADERBOARD
    /// (every coach by rating, with the caller's own row pinned at the bottom when they sit outside the top
    /// slice), tab 1 is the caller's PALMARÈS (career best, seasons played, and every title / promotion /
    /// relegation the ladder has recorded).
    ///
    /// Both come straight from the server (PostgreSQL is authoritative; Redis only accelerates the top-N read),
    /// so this presenter just formats. Loaded once per tab and cached until Refresh — nothing here changes
    /// second to second, unlike the season screen which polls.
    /// </summary>
    public sealed class RankedLeaderboardScreenPresenter : IScreenPresenter
    {
        private const int TopCount = 50;

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly RankedLeaderboardView _view;

        private int _tab;
        private bool _busy;
        private RankedLeaderboardDto _board;
        private RankedPalmaresDto _palmares;

        public VisualElement View => _view.Root;

        public RankedLeaderboardScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
            _view = new RankedLeaderboardView(loc.Tr);
        }

        public void Enter()
        {
            _view.TabSelected += OnTab;
            _view.RefreshClicked += OnRefresh;
            _view.BackClicked += OnBack;
            _view.SetActiveTab(_tab);
            LoadAsync().Forget();
        }

        public void Exit()
        {
            _view.TabSelected -= OnTab;
            _view.RefreshClicked -= OnRefresh;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => LoadAsync().Forget();

        private void OnTab(int tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            _view.SetActiveTab(tab);
            LoadAsync().Forget();
        }

        private void OnRefresh()
        {
            _board = null;
            _palmares = null;
            LoadAsync().Forget();
        }

        private async UniTask LoadAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.loading"), isError: false);

            if (_tab == 0)
            {
                if (_board == null)
                {
                    var result = await _ranked.GetLeaderboardAsync(TopCount);
                    if (!Finish(result.Success, result.Error)) return;
                    _board = result.Value;
                }
                RenderLeaderboard(_board);
            }
            else
            {
                if (_palmares == null)
                {
                    var result = await _ranked.GetPalmaresAsync();
                    if (!Finish(result.Success, result.Error)) return;
                    _palmares = result.Value;
                }
                RenderPalmares(_palmares);
            }

            _busy = false;
            _view.SetBusy(false);
            _view.ClearStatus();
        }

        /// <summary>Clears the busy state and reports a failed call; true = carry on rendering.</summary>
        private bool Finish(bool success, RankedApiError error)
        {
            if (success) return true;
            _busy = false;
            _view.SetBusy(false);
            _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(error)), isError: true);
            _view.SetRows(null);
            return false;
        }

        // --- rendering -----------------------------------------------------------------------------

        private void RenderLeaderboard(RankedLeaderboardDto board)
        {
            _view.SetSummary(_loc.Tr("ranked.board.total", board?.totalCoaches ?? 0));

            var rows = new List<RankedLeaderboardView.RowVm>();
            bool youListed = false;

            if (board?.entries != null)
            {
                foreach (var e in board.entries)
                {
                    if (e.isYou) youListed = true;
                    rows.Add(EntryRow(e));
                }
            }

            // Outside the top slice: pin the caller's own row at the bottom so they always see where they are.
            if (!youListed && board?.you != null) rows.Add(EntryRow(board.you));

            _view.SetRows(rows);
        }

        private RankedLeaderboardView.RowVm EntryRow(RankedLeaderboardEntryDto e)
        {
            string where = e.tier.HasValue && !string.IsNullOrEmpty(e.groupName)
                ? _loc.Tr("ranked.board.where", e.groupName, e.tier.Value)
                : _loc.Tr("ranked.board.where_unknown");

            return new RankedLeaderboardView.RowVm
            {
                Title = _loc.Tr("ranked.board.entry", e.rank, e.displayName, e.rating),
                Detail = _loc.Tr("ranked.board.entry_detail", where, e.seasonsPlayed, e.titles, e.peakRating),
                Highlight = e.isYou,
            };
        }

        private void RenderPalmares(RankedPalmaresDto p)
        {
            if (p == null)
            {
                _view.SetSummary(string.Empty);
                _view.SetRows(null);
                return;
            }

            _view.SetSummary(_loc.Tr("ranked.palmares.summary",
                p.rating, p.peakRating, p.seasonsPlayed, p.titles, p.promotions, p.relegations));

            var rows = new List<RankedLeaderboardView.RowVm>();
            if (p.awards != null)
            {
                foreach (var a in p.awards)
                {
                    bool trophy = a.kind == (int)RankedAwardKind.Champion
                                  || a.kind == (int)RankedAwardKind.TopFlightTitle
                                  || a.kind == (int)RankedAwardKind.Promotion;

                    rows.Add(new RankedLeaderboardView.RowVm
                    {
                        Title = _loc.Tr("ranked.palmares.award",
                            _loc.Tr(AwardKindKey(a.kind)), a.groupName, a.seasonNumber),
                        Detail = a.kind == (int)RankedAwardKind.PlacementCompleted
                            ? _loc.Tr("ranked.palmares.award_placement", a.position, a.ratingAfter)
                            : _loc.Tr("ranked.palmares.award_detail",
                                a.position, a.ratingAfter, Signed(a.ratingDelta)),
                        Trophy = trophy,
                    });
                }
            }
            _view.SetRows(rows);
        }

        private static string AwardKindKey(int kind) => kind switch
        {
            (int)RankedAwardKind.Champion => "ranked.palmares.kind.champion",
            (int)RankedAwardKind.TopFlightTitle => "ranked.palmares.kind.top_flight",
            (int)RankedAwardKind.Promotion => "ranked.palmares.kind.promotion",
            (int)RankedAwardKind.Relegation => "ranked.palmares.kind.relegation",
            (int)RankedAwardKind.PlacementCompleted => "ranked.palmares.kind.placement",
            _ => "ranked.palmares.kind.season",
        };

        private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();

        private void OnBack() => _navigator.Pop();
    }
}
