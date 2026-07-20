using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using Sim.Core.Domain;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Online auction screen (task 8.5b): the current window's free-agent lots for the user's private
    /// league, with the caller's budget, the creator's open/close controls, and an inline bid panel. Live
    /// with minimal delay via a ~1s REST poll while the screen is open (chosen over the SignalR client so
    /// it works on WebGL too; the server's AuctionHub stays ready for 8.6), plus an immediate refresh after
    /// every action. App scope; reuses the dumb <see cref="AuctionView"/>. The authoritative bid/settlement
    /// logic lives on the server — this screen only submits inputs and renders the returned state.
    /// </summary>
    public sealed class OnlineAuctionScreenPresenter : IScreenPresenter
    {
        private const int PollMillis = 1000;
        private const long MinIncrementFloor = 25_000; // mirror the server's bid rules for the pre-fill

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly LeagueSelection _selection;
        private readonly AuctionView _view;

        private string _leagueId;
        private bool _isCreator;
        private readonly Dictionary<int, string> _clubNames = new Dictionary<int, string>();

        private AuctionsDto _snapshot;
        private bool _busy;
        private CancellationTokenSource _cts;

        public VisualElement View => _view.Root;

        public OnlineAuctionScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues, LeagueSelection selection)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _view = new AuctionView(loc.Tr);
        }

        public void Enter()
        {
            _view.OpenWindowClicked += OnOpenWindow;
            _view.CloseWindowClicked += OnCloseWindow;
            _view.RefreshClicked += OnRefresh;
            _view.BidClicked += OnBidClicked;
            _view.BidConfirmClicked += OnBidConfirm;
            _view.BotBidClicked += OnBotBid;
            _view.BackClicked += OnBack;

            _leagueId = _selection.LeagueId;
            _view.SetHeader(_loc.Tr("auction.title"));
            _view.SetDevToolsVisible(DevFlags.OnlineTestTools);
            _cts = new CancellationTokenSource();
            InitAsync(_cts.Token).Forget();
        }

        public void Exit()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _view.OpenWindowClicked -= OnOpenWindow;
            _view.CloseWindowClicked -= OnCloseWindow;
            _view.RefreshClicked -= OnRefresh;
            _view.BidClicked -= OnBidClicked;
            _view.BidConfirmClicked -= OnBidConfirm;
            _view.BotBidClicked -= OnBotBid;
            _view.BackClicked -= OnBack;
        }

        // --- loading / polling ---------------------------------------------------------------------

        private async UniTaskVoid InitAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_leagueId))
            {
                _view.SetStatus(_loc.Tr("leagues.error.not_found"));
                return;
            }

            _view.SetStatus(_loc.Tr("leagues.loading"));

            // One detail read for the creator flag + club-name lookup (names don't change).
            var detail = await _leagues.GetAsync(_leagueId);
            if (!detail.Success)
            {
                _view.SetStatus(_loc.Tr(LeagueErrorFormat.Key(detail.Error)));
                return;
            }
            _isCreator = detail.Value.league.isCreator;
            _clubNames.Clear();
            foreach (LeagueClubDto c in detail.Value.clubs) _clubNames[c.externalId] = c.name;
            _view.SetHeader(_loc.Tr("auction.header", detail.Value.league.name));
            _view.SetStatus(string.Empty);

            await RefreshAsync(silent: false);
            PollAsync(ct).Forget();
        }

        private async UniTaskVoid PollAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                bool cancelled = await UniTask.Delay(
                    TimeSpan.FromMilliseconds(PollMillis), cancellationToken: ct).SuppressCancellationThrow();
                if (cancelled) return;
                if (!_busy) await RefreshAsync(silent: true);
            }
        }

        private async UniTask RefreshAsync(bool silent)
        {
            var result = await _leagues.GetAuctionsAsync(_leagueId);
            if (!result.Success)
            {
                if (!silent) _view.SetStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)));
                return;
            }
            _snapshot = result.Value;
            Render();
        }

        // --- rendering -----------------------------------------------------------------------------

        private void Render()
        {
            if (_snapshot == null) return;

            _view.SetBudget(_loc.Tr("auction.budget",
                MoneyFormat.Short(_snapshot.budget), MoneyFormat.Short(_snapshot.available)));

            string banner = _snapshot.windowOpen
                ? _loc.Tr("auction.window_open")
                : (_snapshot.lots.Count > 0 ? _loc.Tr("auction.window_closed") : _loc.Tr("auction.no_window"));
            _view.SetWindow(banner, _isCreator, _snapshot.windowOpen);

            int? myClub = _snapshot.yourClubExternalId;
            var rows = new List<AuctionRowVm>(_snapshot.lots.Count);
            foreach (AuctionLotDto lot in _snapshot.lots)
            {
                bool open = lot.status == (int)AuctionStatus.Open;
                bool leading = myClub.HasValue && lot.highBidClubExternalId == myClub.Value;
                rows.Add(new AuctionRowVm
                {
                    AuctionId = lot.auctionId,
                    Title = $"{lot.playerName} · {RoleName(lot.role)} · OVR {lot.overall}",
                    PriceInfo = lot.highBid > 0
                        ? _loc.Tr("auction.price_bid", MoneyFormat.Short(lot.startPrice), MoneyFormat.Short(lot.highBid))
                        : _loc.Tr("auction.price_start", MoneyFormat.Short(lot.startPrice)),
                    LeaderInfo = LeaderText(lot),
                    Countdown = ClockText(lot),
                    CanBid = _snapshot.windowOpen && open && myClub.HasValue && !leading,
                    Dimmed = !open,
                });
            }
            _view.SetLots(rows);
        }

        private string LeaderText(AuctionLotDto lot)
        {
            if (!lot.highBidClubExternalId.HasValue) return _loc.Tr("auction.no_leader");
            string name = _clubNames.TryGetValue(lot.highBidClubExternalId.Value, out var n)
                ? n : ("#" + lot.highBidClubExternalId.Value);
            return _loc.Tr("auction.leader", name);
        }

        private string ClockText(AuctionLotDto lot)
        {
            if (lot.status == (int)AuctionStatus.Settled) return _loc.Tr("auction.settled");
            if (lot.status == (int)AuctionStatus.Unsold) return _loc.Tr("auction.unsold");
            int s = lot.secondsRemaining < 0 ? 0 : lot.secondsRemaining;
            return $"{s / 60}:{(s % 60):00}";
        }

        // --- actions -------------------------------------------------------------------------------

        private void OnBidClicked(string auctionId)
        {
            AuctionLotDto lot = FindLot(auctionId);
            if (lot == null) return;
            long min = MinBid(lot);
            long step = System.Math.Max(MinIncrementFloor, (lot.highBid > 0 ? lot.highBid : lot.startPrice) / 20);
            string info = _loc.Tr("auction.min_info",
                MoneyFormat.Short(min), MoneyFormat.Short(_snapshot?.available ?? 0));
            _view.ShowBidPanel(auctionId, lot.playerName, info, min, step);
        }

        private void OnBidConfirm(string auctionId, long amount) => BidAsync(auctionId, amount).Forget();

        private async UniTaskVoid BidAsync(string auctionId, long amount)
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetStatus(_loc.Tr("auction.status.bidding"));

            var result = await _leagues.PlaceBidAsync(_leagueId, auctionId, amount);
            if (result.Success)
            {
                _view.HideBidPanel();
                _view.SetStatus(result.Value.extended
                    ? _loc.Tr("auction.status.bid_extended")
                    : _loc.Tr("auction.status.bid_placed"));
                await RefreshAsync(silent: true);
            }
            else
            {
                _view.SetStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)));
            }
            _busy = false;
        }

        private void OnOpenWindow() => WindowAsync(open: true).Forget();
        private void OnCloseWindow() => WindowAsync(open: false).Forget();

        private async UniTaskVoid WindowAsync(bool open)
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetStatus(_loc.Tr(open ? "auction.status.opening" : "auction.status.closing"));

            var result = open
                ? await _leagues.OpenWindowAsync(_leagueId)
                : await _leagues.CloseWindowAsync(_leagueId);

            if (result.Success)
            {
                _snapshot = result.Value;
                Render();
                _view.SetStatus(string.Empty);
            }
            else
            {
                _view.SetStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)));
            }
            _busy = false;
        }

        private void OnRefresh() => RefreshAsync(silent: false).Forget();

        // Dev-only: make the league's bots place a round of bids so you can watch live outbidding solo.
        private void OnBotBid() => BotBidAsync().Forget();

        private async UniTaskVoid BotBidAsync()
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetStatus(_loc.Tr("auction.status.botbid"));

            var result = await _leagues.BotBidAsync(_leagueId, 1);
            _view.SetStatus(result.Success ? string.Empty : _loc.Tr(LeagueErrorFormat.Key(result.Error)));
            await RefreshAsync(silent: true);
            _busy = false;
        }

        private void OnBack() => _navigator.Pop();

        // --- helpers -------------------------------------------------------------------------------

        private AuctionLotDto FindLot(string auctionId)
        {
            if (_snapshot?.lots == null) return null;
            foreach (AuctionLotDto lot in _snapshot.lots)
                if (lot.auctionId == auctionId) return lot;
            return null;
        }

        private static long MinBid(AuctionLotDto lot) =>
            lot.highBid <= 0 ? lot.startPrice : lot.highBid + System.Math.Max(MinIncrementFloor, lot.highBid / 20);

        private string RoleName(int role) =>
            _loc.Tr("role." + ((PositionRole)role).ToString().ToLowerInvariant());
    }
}
