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
    /// league. Live with minimal delay via a ~1s REST poll while the screen is open (chosen over the
    /// SignalR client so it works on WebGL too), plus an immediate refresh after every action.
    ///
    /// The screen answers the three questions a bidder actually has: WHO bid (the leader is named, and
    /// your own bid says so), on WHAT (role colour, age, OVR, base price and current bid), and HOW MUCH
    /// CAN I STILL SPEND (budget · committed on the lots you lead · available). Three tabs separate the
    /// full lot list from your own activity (leading / outbid / bought) and from the players you follow.
    /// Following and "I bid on this lot" are personal notes kept locally (<see cref="AuctionWatchlist"/>) —
    /// the server's lot only carries the current leader. Raising happens in round steps sized to the lot.
    /// The authoritative bid/settlement logic lives on the server; this screen submits and renders.
    /// </summary>
    public sealed class OnlineAuctionScreenPresenter : IScreenPresenter
    {
        private const int PollMillis = 1000;
        private const long MinIncrementFloor = 25_000; // mirror the server's bid rules for the pre-fill

        private const int TabLots = 0;
        private const int TabMine = 1;
        private const int TabFollowed = 2;

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
        private int _tab = TabLots;
        private CancellationTokenSource _cts;

        // The lot the bid panel is open on, and the price it was opened against — if someone raises while
        // the panel is up, the panel is re-armed on the new minimum instead of sending a doomed bid.
        private string _bidLotId;
        private long _bidLotPrice;

        public VisualElement View => _view.Root;

        public OnlineAuctionScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues, LeagueSelection selection)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _view = new AuctionView(loc.Tr, MoneyFormat.Short);
        }

        public void Enter()
        {
            _view.OpenWindowClicked += OnOpenWindow;
            _view.CloseWindowClicked += OnCloseWindow;
            _view.RefreshClicked += OnRefresh;
            _view.TabSelected += OnTabSelected;
            _view.BidClicked += OnBidClicked;
            _view.FavoriteToggled += OnFavoriteToggled;
            _view.BidConfirmClicked += OnBidConfirm;
            _view.BidCancelClicked += OnBidCancel;
            _view.BotBidClicked += OnBotBid;
            _view.BackClicked += OnBack;

            _leagueId = _selection.LeagueId;
            _view.SetHeader(_loc.Tr("auction.title"));
            _view.SetActiveTab(_tab);
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
            _view.TabSelected -= OnTabSelected;
            _view.BidClicked -= OnBidClicked;
            _view.FavoriteToggled -= OnFavoriteToggled;
            _view.BidConfirmClicked -= OnBidConfirm;
            _view.BidCancelClicked -= OnBidCancel;
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

            _view.SetBudget(
                MoneyFormat.Short(_snapshot.budget),
                MoneyFormat.Short(_snapshot.committed),
                MoneyFormat.Short(_snapshot.available));

            string banner = _snapshot.windowOpen
                ? _loc.Tr("auction.window_open")
                : (_snapshot.lots.Count > 0 ? _loc.Tr("auction.window_closed") : _loc.Tr("auction.no_window"));
            _view.SetWindow(banner, _isCreator, _snapshot.windowOpen);

            switch (_tab)
            {
                case TabMine: RenderMine(); break;
                case TabFollowed: RenderFollowed(); break;
                default: RenderLots(); break;
            }

            KeepBidPanelHonest();
        }

        private void RenderLots()
        {
            var rows = new List<AuctionRowVm>();
            foreach (AuctionLotDto lot in _snapshot.lots) rows.Add(Row(lot));
            _view.SetGroups(new[]
            {
                new AuctionGroupVm { Caption = null, Rows = rows, EmptyText = _loc.Tr("auction.none_lots") },
            });
        }

        private void RenderMine()
        {
            int? myClub = _snapshot.yourClubExternalId;
            var leading = new List<AuctionRowVm>();
            var outbid = new List<AuctionRowVm>();
            var bought = new List<AuctionRowVm>();

            foreach (AuctionLotDto lot in _snapshot.lots)
            {
                bool open = lot.status == (int)AuctionStatus.Open;
                bool mine = myClub.HasValue && lot.highBidClubExternalId == myClub.Value;

                if (open && mine) leading.Add(Row(lot));
                else if (open && AuctionWatchlist.HasBid(_leagueId, lot.auctionId)) outbid.Add(Row(lot));
                else if (!open && mine && lot.status == (int)AuctionStatus.Settled) bought.Add(Row(lot));
            }

            _view.SetGroups(new[]
            {
                new AuctionGroupVm
                {
                    Caption = _loc.Tr("auction.group_leading"), Rows = leading,
                    EmptyText = _loc.Tr("auction.none_leading"),
                },
                new AuctionGroupVm
                {
                    Caption = _loc.Tr("auction.group_outbid"), Rows = outbid,
                    EmptyText = _loc.Tr("auction.none_outbid"),
                },
                new AuctionGroupVm
                {
                    Caption = _loc.Tr("auction.group_bought"), Rows = bought,
                    EmptyText = _loc.Tr("auction.none_bought"),
                },
            });
        }

        private void RenderFollowed()
        {
            var rows = new List<AuctionRowVm>();
            foreach (AuctionLotDto lot in _snapshot.lots)
                if (AuctionWatchlist.IsFavorite(_leagueId, lot.playerExternalId))
                    rows.Add(Row(lot));

            _view.SetGroups(new[]
            {
                new AuctionGroupVm { Caption = null, Rows = rows, EmptyText = _loc.Tr("auction.none_followed") },
            });
        }

        /// <summary>Builds one lot row: who leads it, at what price, and what you may do about it.</summary>
        private AuctionRowVm Row(AuctionLotDto lot)
        {
            int? myClub = _snapshot.yourClubExternalId;
            bool open = lot.status == (int)AuctionStatus.Open;
            bool leading = myClub.HasValue && lot.highBidClubExternalId == myClub.Value;
            bool won = !open && leading && lot.status == (int)AuctionStatus.Settled;
            bool wasOutbid = open && !leading && AuctionWatchlist.HasBid(_leagueId, lot.auctionId);

            var role = (PositionRole)lot.role;

            return new AuctionRowVm
            {
                AuctionId = lot.auctionId,
                PlayerExternalId = lot.playerExternalId,
                PlayerName = lot.playerName,
                RoleGroup = RoleFormat.Group(role),
                RoleAbbr = RoleName(lot.role),
                Age = lot.age,
                Overall = lot.overall,
                PriceInfo = lot.highBid > 0
                    ? _loc.Tr("auction.price_bid", MoneyFormat.Short(lot.startPrice), MoneyFormat.Short(lot.highBid))
                    : _loc.Tr("auction.price_start", MoneyFormat.Short(lot.startPrice)),
                LeaderInfo = LeaderText(lot, leading),
                Countdown = ClockText(lot),
                Badge =
                    won ? _loc.Tr("auction.badge_won") :
                    leading ? _loc.Tr("auction.badge_leading") :
                    wasOutbid ? _loc.Tr("auction.badge_outbid") : null,
                BadgeKind = won ? 3 : leading ? 1 : wasOutbid ? 2 : 0,
                CanBid = _snapshot.windowOpen && open && myClub.HasValue && !leading,
                Dimmed = !open,
                Favorite = AuctionWatchlist.IsFavorite(_leagueId, lot.playerExternalId),
                CanFavorite = open,
            };
        }

        private string LeaderText(AuctionLotDto lot, bool leading)
        {
            if (!lot.highBidClubExternalId.HasValue) return _loc.Tr("auction.no_leader");
            if (leading) return _loc.Tr("auction.leader_you");
            string name = !string.IsNullOrEmpty(lot.highBidClubName)
                ? lot.highBidClubName
                : (_clubNames.TryGetValue(lot.highBidClubExternalId.Value, out var n)
                    ? n : ("#" + lot.highBidClubExternalId.Value));
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

        private void OnTabSelected(int tab)
        {
            _tab = tab;
            _view.SetActiveTab(tab);
            Render();
        }

        private void OnFavoriteToggled(int playerExternalId)
        {
            if (string.IsNullOrEmpty(_leagueId)) return;
            bool now = AuctionWatchlist.ToggleFavorite(_leagueId, playerExternalId);
            _view.SetStatus(_loc.Tr(now ? "auction.status.followed" : "auction.status.unfollowed"));
            Render();
        }

        private void OnBidClicked(string auctionId) => OpenBidPanel(auctionId, announce: true);

        /// <summary>Arms the bid control on a lot: minimum legal raise, ceiling = what is still available,
        /// and four round steps sized to the lot's own price.</summary>
        private void OpenBidPanel(string auctionId, bool announce)
        {
            AuctionLotDto lot = FindLot(auctionId);
            if (lot == null || _snapshot == null) return;

            long current = lot.highBid > 0 ? lot.highBid : lot.startPrice;
            long min = MinBid(lot);
            long available = _snapshot.available;

            _bidLotId = auctionId;
            _bidLotPrice = current;

            _view.ShowBidPanel(new BidPanelVm
            {
                Id = auctionId,
                Title = lot.playerName,
                RoleGroup = RoleFormat.Group((PositionRole)lot.role),
                RoleAbbr = RoleName(lot.role),
                Subtitle = _loc.Tr("auction.min_info", MoneyFormat.Short(min), MoneyFormat.Short(available)),
                Min = min,
                Max = available,
                Steps = BidSteps.For(current, MinIncrement(lot)),
                ConfirmFormat = _loc.Tr("auction.confirm_amount"),
                MinLabel = _loc.Tr("auction.set_min"),
                MaxLabel = _loc.Tr("auction.set_max"),
                CancelLabel = _loc.Tr("auction.cancel"),
                OverBudget = _loc.Tr("auction.over_budget"),
            });

            if (announce) _view.SetStatus(string.Empty);
        }

        /// <summary>
        /// The poll can move the lot under an open bid panel (someone raised, or the lot closed). Re-arm the
        /// panel on the new minimum — or close it if the lot is gone — instead of letting the user confirm a
        /// bid the server is bound to refuse.
        /// </summary>
        private void KeepBidPanelHonest()
        {
            if (!_view.BidPanelOpen || string.IsNullOrEmpty(_bidLotId)) return;

            AuctionLotDto lot = FindLot(_bidLotId);
            if (lot == null || lot.status != (int)AuctionStatus.Open || !_snapshot.windowOpen)
            {
                _view.HideBidPanel();
                _bidLotId = null;
                return;
            }

            long current = lot.highBid > 0 ? lot.highBid : lot.startPrice;
            if (current == _bidLotPrice) return;

            OpenBidPanel(_bidLotId, announce: false);
            _view.SetStatus(_loc.Tr("auction.status.price_moved"));
        }

        private void OnBidCancel()
        {
            _bidLotId = null;
            _view.SetStatus(string.Empty);
        }

        private void OnBidConfirm(string auctionId, long amount) => BidAsync(auctionId, amount).Forget();

        private async UniTaskVoid BidAsync(string auctionId, long amount)
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetBusy(true);
            _view.SetStatus(_loc.Tr("auction.status.bidding"));

            var result = await _leagues.PlaceBidAsync(_leagueId, auctionId, amount);
            if (result.Success)
            {
                // Remember that this lot is one of mine, so being outbid later is visible as such.
                AuctionWatchlist.RecordBid(_leagueId, auctionId);
                _bidLotId = null;
                _view.HideBidPanel();
                _view.SetStatus(result.Value.extended
                    ? _loc.Tr("auction.status.bid_extended")
                    : _loc.Tr("auction.status.bid_placed"));
                await RefreshAsync(silent: true);
            }
            else
            {
                _view.SetStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)));
                // A refused bid usually means the price moved — re-arm on the current one.
                await RefreshAsync(silent: true);
            }
            _busy = false;
            _view.SetBusy(false);
        }

        private void OnOpenWindow() => WindowAsync(open: true).Forget();
        private void OnCloseWindow() => WindowAsync(open: false).Forget();

        private async UniTaskVoid WindowAsync(bool open)
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetBusy(true);
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
            _view.SetBusy(false);
        }

        private void OnRefresh() => RefreshAsync(silent: false).Forget();

        // Dev-only: make the league's bots place a round of bids so you can watch live outbidding solo.
        private void OnBotBid() => BotBidAsync().Forget();

        private async UniTaskVoid BotBidAsync()
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetBusy(true);
            _view.SetStatus(_loc.Tr("auction.status.botbid"));

            var result = await _leagues.BotBidAsync(_leagueId, 1);
            _view.SetStatus(result.Success ? string.Empty : _loc.Tr(LeagueErrorFormat.Key(result.Error)));
            await RefreshAsync(silent: true);
            _busy = false;
            _view.SetBusy(false);
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

        /// <summary>The server's own rule: 5% of the standing bid, never less than the floor.</summary>
        private static long MinIncrement(AuctionLotDto lot) =>
            System.Math.Max(MinIncrementFloor, (lot.highBid > 0 ? lot.highBid : lot.startPrice) / 20);

        private static long MinBid(AuctionLotDto lot) =>
            lot.highBid <= 0 ? lot.startPrice : lot.highBid + System.Math.Max(MinIncrementFloor, lot.highBid / 20);

        private string RoleName(int role) =>
            _loc.Tr("role." + ((PositionRole)role).ToString().ToLowerInvariant());
    }
}
