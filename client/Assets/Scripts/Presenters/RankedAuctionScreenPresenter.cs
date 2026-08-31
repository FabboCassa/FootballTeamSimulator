using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The ranked auction board (task 12.2). Until now the ladder's auctions were a list inside the market
    /// screen and every lot of a window closed with the window; a coach can now put one of HIS OWN players
    /// up with a timer he chooses, each lot runs on its own clock, and the fee is paid to the seller. That
    /// deserved the auction ROOM the private leagues already had, so this screen drives the same
    /// <see cref="AuctionView"/> — budget tiles (total · committed on the lots you lead · what is actually
    /// left), rows that say who bid and how much, stepped raises instead of typing a figure — with a fourth
    /// tab the private leagues have no use for: SELL.
    ///
    /// Live by a short REST poll while the screen is open (the same choice as 8.5b: it works on WebGL, where
    /// a SignalR client does not). The server is authoritative for every rule here; the screen mirrors the
    /// bid arithmetic and the integrity band only to keep the controls from offering a move that can only
    /// come back refused.
    /// </summary>
    public sealed class RankedAuctionScreenPresenter : IScreenPresenter
    {
        /// <summary>A ranked lot can run for hours, so this polls slower than the private leagues' 1s: the
        /// last-second fight is protected by the server's anti-snipe extension, not by the refresh rate.</summary>
        private const int PollMillis = 2000;

        /// <summary>The server's own bid floor (5% of the standing bid, never below this).</summary>
        private const long MinIncrementFloor = 25_000;

        /// <summary>Mirrors the server's Phase 9.5 band: a reserve below 40% of market value is a gift and
        /// above 250% is a bribe — both refused, so the control never offers them.</summary>
        private const int MinFeePercentOfValue = 40;
        private const int MaxFeePercentOfValue = 250;

        /// <summary>Mirrors <c>RankedOptions.MinSquadSizeForSale</c> — only to tell the coach how many more
        /// he may put up; the server is what actually refuses the one that would break the floor.</summary>
        private const int SquadFloor = 16;

        /// <summary>The durations offered, when the window still has room for them.</summary>
        private static readonly int[] DurationHours = { 1, 3, 6, 12, 24 };

        private const int TabLots = 0;
        private const int TabMine = 1;
        private const int TabFollowed = 2;
        private const int TabSell = 3;

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly AuctionView _view;

        private RankedAuctionsDto _board;
        private RankedSquadDto _squad;
        /// <summary>The group's clubs by external id — read once, so a bid can say WHO made it instead of
        /// showing a bare number.</summary>
        private readonly Dictionary<int, string> _clubNames = new Dictionary<int, string>();
        private string _groupId = string.Empty;
        private int _myClubExt = -1;
        private bool _busy;
        private int _tab = TabLots;
        private CancellationTokenSource _cts;

        // The lot the bid panel is armed on, and the price it was armed against: the poll can move it under
        // the coach's finger, and a doomed bid is worse than a re-armed panel.
        private string _bidLotId;
        private long _bidLotPrice;

        // The sell flow, in two steps: pick the duration, then price him.
        private int _sellPlayerId = -1;
        private int _sellSeconds;

        public VisualElement View => _view.Root;

        public RankedAuctionScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
            _view = new AuctionView(loc.Tr, MoneyFormat.Short, new[]
            {
                "auction.tab_lots", "auction.tab_mine", "auction.tab_followed", "ranked.auction.tab_sell",
            });
        }

        public void Enter()
        {
            _view.RefreshClicked += OnRefresh;
            _view.TabSelected += OnTabSelected;
            _view.BidClicked += OnBidClicked;
            _view.FavoriteToggled += OnFavoriteToggled;
            _view.BidConfirmClicked += OnAmountConfirm;
            _view.BidCancelClicked += OnAmountCancel;
            _view.ChoiceClicked += OnDurationPicked;
            _view.ChoiceCancelClicked += OnSellCancel;
            _view.BackClicked += OnBack;

            _view.SetHeader(_loc.Tr("ranked.auction.title"));
            _view.SetActiveTab(_tab);
            _view.SetDevToolsVisible(false);   // the ladder's board has no per-league dev buttons
            _cts = new CancellationTokenSource();
            InitAsync(_cts.Token).Forget();
        }

        public void Exit()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _view.RefreshClicked -= OnRefresh;
            _view.TabSelected -= OnTabSelected;
            _view.BidClicked -= OnBidClicked;
            _view.FavoriteToggled -= OnFavoriteToggled;
            _view.BidConfirmClicked -= OnAmountConfirm;
            _view.BidCancelClicked -= OnAmountCancel;
            _view.ChoiceClicked -= OnDurationPicked;
            _view.ChoiceCancelClicked -= OnSellCancel;
            _view.BackClicked -= OnBack;
        }

        // --- loading / polling ---------------------------------------------------------------------

        private async UniTaskVoid InitAsync(CancellationToken ct)
        {
            _view.SetStatus(_loc.Tr("ranked.loading"));

            var mine = await _ranked.GetMineAsync();
            if (mine.Success && mine.Value != null)
            {
                _groupId = mine.Value.groupId ?? string.Empty;
                _myClubExt = mine.Value.clubExternalId ?? -1;
            }
            else if (!mine.Success)
            {
                _view.SetStatus(_loc.Tr(RankedErrorFormat.Key(mine.Error)));
                return;
            }

            // The group's club directory (the standings are it) — names don't change during a season.
            var season = await _ranked.GetSeasonAsync();
            if (season.Success && season.Value?.standings != null)
                foreach (var s in season.Value.standings) _clubNames[s.clubExternalId] = s.clubName;

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
            var board = await _ranked.GetAuctionsAsync();
            if (!board.Success)
            {
                if (!silent) _view.SetStatus(_loc.Tr(RankedErrorFormat.Key(board.Error)));
                return;
            }
            _board = board.Value;
            if (_myClubExt <= 0 && _board.yourClubExternalId > 0) _myClubExt = _board.yourClubExternalId;

            // The sell tab is the only one that needs the squad, and a squad does not change between polls
            // unless a lot settles — so it is fetched when that tab is opened, and after every action.
            if (_tab == TabSell && _squad == null) await LoadSquadAsync();

            Render();
        }

        private async UniTask LoadSquadAsync()
        {
            if (_myClubExt <= 0) return;
            var squad = await _ranked.GetClubSquadAsync(_myClubExt);
            if (squad.Success) _squad = squad.Value;
        }

        // --- rendering -----------------------------------------------------------------------------

        private void Render()
        {
            if (_board == null) return;

            _view.SetBudget(
                MoneyFormat.Short(_board.budget),
                MoneyFormat.Short(_board.committed),
                MoneyFormat.Short(_board.available));

            // THE QUESTION THE LADDER COULD NOT ANSWER IN-APP (task 12.2): when does this shut, and when do
            // auctions come back? Both instants come from the server and are rendered in the DEVICE's time.
            string banner;
            if (_board.windowOpen)
                banner = string.IsNullOrEmpty(_board.windowClosesUtc)
                    ? _loc.Tr("auction.window_open")
                    : _loc.Tr("ranked.auction.window_open", FormatTime(_board.windowClosesUtc));
            else
                banner = string.IsNullOrEmpty(_board.nextWindowOpensUtc)
                    ? _loc.Tr("auction.window_closed")
                    : _loc.Tr("ranked.auction.window_next", FormatTime(_board.nextWindowOpensUtc));
            _view.SetWindow(banner, creator: false, windowOpen: _board.windowOpen);

            switch (_tab)
            {
                case TabMine: RenderMine(); break;
                case TabFollowed: RenderFollowed(); break;
                case TabSell: RenderSell(); break;
                default: RenderLots(); break;
            }

            KeepBidPanelHonest();
        }

        private IEnumerable<RankedAuctionLotDto> OpenLots =>
            _board?.lots?.Where(l => l.status == (int)RankedAuctionStatus.Open)
            ?? Enumerable.Empty<RankedAuctionLotDto>();

        private void RenderLots()
        {
            var rows = OpenLots
                .OrderBy(l => l.secondsRemaining)
                .ThenByDescending(l => l.overall)
                .Select(Row)
                .ToList();
            _view.SetGroups(new[]
            {
                new AuctionGroupVm { Caption = null, Rows = rows, EmptyText = _loc.Tr("auction.none_lots") },
            });
        }

        private void RenderMine()
        {
            var leading = new List<AuctionRowVm>();
            var outbid = new List<AuctionRowVm>();
            var selling = new List<AuctionRowVm>();

            foreach (var lot in OpenLots)
            {
                if (lot.youAreSeller) selling.Add(Row(lot));
                else if (lot.youAreLeading) leading.Add(Row(lot));
                else if (AuctionWatchlist.HasBid(_groupId, lot.id)) outbid.Add(Row(lot));
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
                    Caption = _loc.Tr("ranked.auction.group_selling"), Rows = selling,
                    EmptyText = _loc.Tr("ranked.auction.none_selling"),
                },
            });
        }

        private void RenderFollowed()
        {
            var rows = OpenLots
                .Where(l => AuctionWatchlist.IsFavorite(_groupId, l.playerExternalId))
                .Select(Row)
                .ToList();
            _view.SetGroups(new[]
            {
                new AuctionGroupVm { Caption = null, Rows = rows, EmptyText = _loc.Tr("auction.none_followed") },
            });
        }

        /// <summary>
        /// The sell tab: what you already have on the board (with the way back off it, while nobody has bid)
        /// and the rest of your squad, each one a tap away from an auction. The squad-floor arithmetic is
        /// shown rather than discovered — the server refuses the listing that would break it, but a coach
        /// should not have to find that out by being refused.
        /// </summary>
        private void RenderSell()
        {
            var onTheBoard = OpenLots.Where(l => l.youAreSeller).ToList();
            var listedIds = new HashSet<int>(onTheBoard.Select(l => l.playerExternalId));

            var mine = onTheBoard.Select(Row).ToList();
            var squad = new List<AuctionRowVm>();
            int room = 0;

            if (_squad?.players != null)
            {
                room = _squad.players.Count - onTheBoard.Count - SquadFloor;
                foreach (var p in _squad.players.OrderByDescending(x => x.overall).ThenBy(x => x.externalId))
                {
                    if (listedIds.Contains(p.externalId)) continue;
                    int id = p.externalId;
                    bool canList = _board != null && _board.windowOpen && room > 0;
                    squad.Add(new AuctionRowVm
                    {
                        AuctionId = null,
                        PlayerExternalId = id,
                        PlayerName = p.name,
                        RoleGroup = RoleFormat.Group((PositionRole)p.role),
                        RoleAbbr = RoleName(p.role),
                        Age = p.age,
                        Overall = p.overall,
                        PriceInfo = _loc.Tr("ranked.market.value", MoneyFormat.Short(p.marketValue)),
                        LeaderInfo = string.Empty,
                        Countdown = string.Empty,
                        CanBid = false,
                        CanFavorite = false,
                        ActionLabel = canList ? _loc.Tr("ranked.auction.sell") : null,
                        RowAction = () => OnSellClicked(id),
                        Dimmed = !canList,
                    });
                }
            }

            _view.SetGroups(new[]
            {
                new AuctionGroupVm
                {
                    Caption = _loc.Tr("ranked.auction.group_selling"), Rows = mine,
                    EmptyText = _loc.Tr("ranked.auction.none_selling"),
                },
                new AuctionGroupVm
                {
                    Caption = _loc.Tr("ranked.auction.group_squad", Math.Max(0, room)), Rows = squad,
                    EmptyText = _loc.Tr("ranked.auction.none_squad"),
                },
            });
        }

        /// <summary>One lot row: who leads it, at what price, on whose clock, and what you may do about it.</summary>
        private AuctionRowVm Row(RankedAuctionLotDto lot)
        {
            bool open = lot.status == (int)RankedAuctionStatus.Open;
            bool seller = lot.youAreSeller;
            bool leading = lot.youAreLeading;
            bool wasOutbid = open && !leading && !seller && AuctionWatchlist.HasBid(_groupId, lot.id);
            // Your own lot can be taken back only while nobody has bid: once there is money on the table the
            // timer is the only thing that ends it (the server says so too).
            bool canUnlist = seller && open && lot.highBid <= 0;

            return new AuctionRowVm
            {
                AuctionId = lot.id,
                PlayerExternalId = lot.playerExternalId,
                PlayerName = lot.playerName,
                RoleGroup = RoleFormat.Group((PositionRole)lot.role),
                RoleAbbr = RoleName(lot.role),
                Age = lot.age,
                Overall = lot.overall,
                PriceInfo = lot.highBid > 0
                    ? _loc.Tr("auction.price_bid", MoneyFormat.Short(lot.startPrice), MoneyFormat.Short(lot.highBid))
                    : _loc.Tr("auction.price_start", MoneyFormat.Short(lot.startPrice)),
                LeaderInfo = LeaderText(lot),
                Countdown = ClockText(lot),
                Badge =
                    seller ? _loc.Tr("ranked.auction.badge_yours") :
                    leading ? _loc.Tr("auction.badge_leading") :
                    wasOutbid ? _loc.Tr("auction.badge_outbid") : null,
                BadgeKind = seller ? 3 : leading ? 1 : wasOutbid ? 2 : 0,
                CanBid = _board != null && _board.windowOpen && open && !leading && !seller,
                Dimmed = !open,
                Favorite = AuctionWatchlist.IsFavorite(_groupId, lot.playerExternalId),
                CanFavorite = open && !seller,
                ActionLabel = canUnlist ? _loc.Tr("ranked.auction.unlist") : null,
                RowAction = () => OnUnlistClicked(lot.id),
            };
        }

        /// <summary>Who is on the other side of this lot — and, on a seller's lot, who is selling. A free
        /// agent belongs to nobody, which is worth saying rather than leaving blank.</summary>
        private string LeaderText(RankedAuctionLotDto lot)
        {
            string origin = lot.kind == (int)RankedLotKind.Seller
                ? _loc.Tr("ranked.auction.seller", string.IsNullOrEmpty(lot.sellerClubName)
                    ? ClubName(lot.sellerClubExternalId) : lot.sellerClubName)
                : _loc.Tr("ranked.auction.free_agent");

            string leader;
            if (lot.youAreLeading) leader = _loc.Tr("auction.leader_you");
            else if (lot.highBid > 0)
                leader = _loc.Tr("auction.leader", ClubName(lot.highBidClubExternalId));
            else leader = _loc.Tr("auction.no_leader");

            return origin + " · " + leader;
        }

        private string ClubName(int? clubExternalId)
        {
            if (!clubExternalId.HasValue) return "?";
            return _clubNames.TryGetValue(clubExternalId.Value, out string name) && !string.IsNullOrEmpty(name)
                ? name
                : "#" + clubExternalId.Value;
        }

        /// <summary>A ranked lot can run for a day, so the clock says hours until it is worth counting
        /// minutes — a bare "1440:00" tells nobody anything.</summary>
        private string ClockText(RankedAuctionLotDto lot)
        {
            if (lot.status == (int)RankedAuctionStatus.Settled) return _loc.Tr("auction.settled");
            if (lot.status == (int)RankedAuctionStatus.Unsold) return _loc.Tr("auction.unsold");
            if (lot.status == (int)RankedAuctionStatus.Cancelled) return _loc.Tr("ranked.auction.cancelled");
            int s = lot.secondsRemaining < 0 ? 0 : lot.secondsRemaining;
            return s >= 3600
                ? _loc.Tr("ranked.auction.clock_hm", s / 3600, (s % 3600) / 60)
                : $"{s / 60}:{(s % 60):00}";
        }

        // --- bidding -------------------------------------------------------------------------------

        private void OnBidClicked(string auctionId) => OpenBidPanel(auctionId, announce: true);

        private void OpenBidPanel(string auctionId, bool announce)
        {
            var lot = FindLot(auctionId);
            if (lot == null || _board == null) return;

            long current = lot.highBid > 0 ? lot.highBid : lot.startPrice;
            _bidLotId = auctionId;
            _bidLotPrice = current;
            _sellPlayerId = -1;

            _view.ShowBidPanel(new BidPanelVm
            {
                Id = auctionId,
                Title = lot.playerName,
                RoleGroup = RoleFormat.Group((PositionRole)lot.role),
                RoleAbbr = RoleName(lot.role),
                Subtitle = _loc.Tr("auction.min_info",
                    MoneyFormat.Short(lot.minNextBid), MoneyFormat.Short(_board.available)),
                Min = lot.minNextBid,
                Max = _board.available,
                Steps = BidSteps.For(current, Math.Max(MinIncrementFloor, current / 20)),
                ConfirmFormat = _loc.Tr("auction.confirm_amount"),
                MinLabel = _loc.Tr("auction.set_min"),
                MaxLabel = _loc.Tr("auction.set_max"),
                CancelLabel = _loc.Tr("auction.cancel"),
                OverBudget = _loc.Tr("auction.over_budget"),
            });

            if (announce) _view.SetStatus(string.Empty);
        }

        /// <summary>The poll can move a lot under an open bid panel (someone raised, or it closed). Re-arm on
        /// the new minimum, or close the panel, instead of letting a bid go out that is bound to be refused.</summary>
        private void KeepBidPanelHonest()
        {
            if (!_view.BidPanelOpen || string.IsNullOrEmpty(_bidLotId)) return;

            var lot = FindLot(_bidLotId);
            if (lot == null || lot.status != (int)RankedAuctionStatus.Open || !_board.windowOpen)
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

        // --- selling (task 12.2) ---------------------------------------------------------------------

        /// <summary>Step one: how long should it run? The choices are the ones the window still has room
        /// for — a lot never outlives the market that allowed it, so the board tells us its own ceiling.</summary>
        private void OnSellClicked(int playerExternalId)
        {
            var p = _squad?.players?.FirstOrDefault(x => x.externalId == playerExternalId);
            if (p == null || _board == null) return;

            var values = new List<int>();
            var labels = new List<string>();
            foreach (int h in DurationHours)
            {
                int seconds = h * 3600;
                if (seconds < _board.minLotSeconds || seconds > _board.maxLotSeconds) continue;
                values.Add(seconds);
                labels.Add(_loc.Tr("ranked.auction.duration_hours", h));
            }
            if (values.Count == 0)
            {
                _view.SetStatus(_loc.Tr("ranked.auction.window_too_short"));
                return;
            }

            _sellPlayerId = playerExternalId;
            _bidLotId = null;
            _view.ShowChoicePanel(
                _loc.Tr("ranked.auction.pick_duration", p.name), values, labels, _loc.Tr("auction.cancel"));
        }

        /// <summary>Step two: what does he open at? Clamped to the 9.5 band, and opening on his market value
        /// rather than on the floor — a seller pricing his own player should start at what the man is worth.</summary>
        private void OnDurationPicked(int seconds)
        {
            var p = _squad?.players?.FirstOrDefault(x => x.externalId == _sellPlayerId);
            if (p == null) return;

            _sellSeconds = seconds;
            long floor = Math.Max(MinIncrementFloor, p.marketValue * MinFeePercentOfValue / 100);
            long ceiling = Math.Max(floor, p.marketValue * MaxFeePercentOfValue / 100);

            _view.ShowBidPanel(new BidPanelVm
            {
                Id = _sellPlayerId.ToString(),
                Title = _loc.Tr("ranked.auction.reserve_for", p.name),
                RoleGroup = RoleFormat.Group((PositionRole)p.role),
                RoleAbbr = RoleName(p.role),
                Subtitle = _loc.Tr("ranked.auction.reserve_band",
                    MoneyFormat.Short(p.marketValue), MoneyFormat.Short(floor), MoneyFormat.Short(ceiling),
                    seconds / 3600),
                Min = floor,
                Max = ceiling,
                Start = Math.Min(ceiling, Math.Max(floor, p.marketValue)),
                Steps = BidSteps.For(p.marketValue, Math.Max(MinIncrementFloor, p.marketValue / 50)),
                ConfirmFormat = _loc.Tr("ranked.auction.confirm_reserve"),
                MinLabel = _loc.Tr("auction.set_min"),
                MaxLabel = _loc.Tr("auction.set_max"),
                CancelLabel = _loc.Tr("auction.cancel"),
                OverBudget = string.Empty,
            });
        }

        private void OnSellCancel()
        {
            _sellPlayerId = -1;
            _view.HideChoicePanel();
            _view.SetStatus(string.Empty);
        }

        private void OnUnlistClicked(string auctionId) => UnlistAsync(auctionId).Forget();

        private async UniTaskVoid UnlistAsync(string auctionId)
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);

            var result = await _ranked.UnlistLotAsync(auctionId);
            if (result.Success)
            {
                _board = result.Value;
                _view.SetStatus(_loc.Tr("ranked.auction.status.unlisted"));
                await LoadSquadAsync();
                Render();
            }
            else _view.SetStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)));

            _busy = false;
            _view.SetBusy(false);
        }

        // --- confirming an amount (a bid, or a reserve) ------------------------------------------------

        private void OnAmountCancel()
        {
            _bidLotId = null;
            _sellPlayerId = -1;
            _view.SetStatus(string.Empty);
        }

        private void OnAmountConfirm(string id, long amount)
        {
            if (_sellPlayerId >= 0) ListAsync(_sellPlayerId, amount, _sellSeconds).Forget();
            else BidAsync(id, amount).Forget();
        }

        private async UniTaskVoid BidAsync(string auctionId, long amount)
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.SetStatus(_loc.Tr("auction.status.bidding"));

            var result = await _ranked.PlaceBidAsync(auctionId, amount);
            if (result.Success)
            {
                // Remember the lot is one of mine, so being outbid later reads as such rather than as a lot
                // I never went for — the server's lot only carries the CURRENT leader.
                AuctionWatchlist.RecordBid(_groupId, auctionId);
                _bidLotId = null;
                _view.HideBidPanel();
                _view.SetStatus(result.Value != null && result.Value.extended
                    ? _loc.Tr("auction.status.bid_extended")
                    : _loc.Tr("auction.status.bid_placed"));
            }
            else _view.SetStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)));

            await RefreshAsync(silent: true);
            _busy = false;
            _view.SetBusy(false);
        }

        private async UniTaskVoid ListAsync(int playerExternalId, long reserve, int seconds)
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.SetStatus(_loc.Tr("ranked.auction.status.listing"));

            var result = await _ranked.ListLotAsync(playerExternalId, reserve, seconds);
            if (result.Success)
            {
                _board = result.Value;
                _sellPlayerId = -1;
                _view.HideBidPanel();
                _view.HideChoicePanel();
                _view.SetStatus(_loc.Tr("ranked.auction.status.listed"));
                await LoadSquadAsync();
                Render();
            }
            else _view.SetStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)));

            _busy = false;
            _view.SetBusy(false);
        }

        // --- misc ----------------------------------------------------------------------------------

        private void OnTabSelected(int tab)
        {
            _tab = tab;
            _view.SetActiveTab(tab);
            _view.HideChoicePanel();
            _sellPlayerId = -1;
            if (tab == TabSell) LoadSquadThenRenderAsync().Forget();
            else Render();
        }

        private async UniTaskVoid LoadSquadThenRenderAsync()
        {
            _view.SetBusy(true);
            await LoadSquadAsync();
            _view.SetBusy(false);
            Render();
        }

        private void OnFavoriteToggled(int playerExternalId)
        {
            bool now = AuctionWatchlist.ToggleFavorite(_groupId, playerExternalId);
            _view.SetStatus(_loc.Tr(now ? "auction.status.followed" : "auction.status.unfollowed"));
            Render();
        }

        private void OnRefresh() => RefreshAsync(silent: false).Forget();

        private void OnBack() => _navigator.Pop();

        private RankedAuctionLotDto FindLot(string auctionId)
        {
            if (_board?.lots == null || string.IsNullOrEmpty(auctionId)) return null;
            foreach (var lot in _board.lots)
                if (lot.id == auctionId) return lot;
            return null;
        }

        private string RoleName(int role) =>
            _loc.Tr("role." + ((PositionRole)role).ToString().ToLowerInvariant());

        /// <summary>Best-effort local time from the server's ISO instant — the ladder's clock is the
        /// server's, but a coach reads it on his own wall.</summary>
        private static string FormatTime(string isoUtc)
        {
            return DateTime.TryParse(
                isoUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("g")
                : isoUtc;
        }
    }
}
