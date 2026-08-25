using System.Collections.Generic;
using System.Linq;
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
    /// The ranked market screen (Phase 9.2b): three tabs — free-agent AUCTIONS (place ascending bids),
    /// direct OFFERS (incoming to answer / outgoing to withdraw), and BROWSE (pick a rival club, then offer
    /// for one of its players). Everything is gated server-side to an open market window; the screen just
    /// reports what the server says. Reached from the ranked season screen.
    /// </summary>
    public sealed class RankedMarketScreenPresenter : IScreenPresenter
    {
        /// <summary>The server's own bid floor (5% of the standing bid, never below this).</summary>
        private const long MinIncrementFloor = 25_000;

        /// <summary>The integrity band a direct offer has to sit in, mirroring the server's Phase 9.5
        /// guard: below 40% of market value is a gift, above 250% is a bribe — both refused.</summary>
        private const int MinFeePercentOfValue = 40;
        private const int MaxFeePercentOfValue = 250;

        private const int TabAuctions = 0;
        private const int TabOffers = 1;
        private const int TabBrowse = 2;

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly RankedMarketView _view;

        private int _tab = TabAuctions;
        private bool _busy;
        private string _groupId;
        private int _myClubExt = -1;

        private RankedAuctionsDto _auctions;
        private RankedOffersDto _offers;
        private List<RankedStandingDto> _clubs = new List<RankedStandingDto>();
        private RankedSquadDto _browsedSquad;

        // What the inline amount panel is currently for.
        private string _pendingBidLotId;
        private int _pendingOfferPlayerId = -1;

        // Which club the report panel is open for (Phase 9.5), -1 = closed.
        private int _pendingReportClubExt = -1;

        public VisualElement View => _view.Root;

        public RankedMarketScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
            _view = new RankedMarketView(loc.Tr, MoneyFormat.Short);
        }

        public void Enter()
        {
            _view.TabSelected += OnTab;
            _view.RefreshClicked += OnRefresh;
            _view.BotMarketClicked += OnBotMarket;
            _view.AmountConfirmClicked += OnAmountConfirm;
            _view.AmountCancelClicked += OnAmountCancel;
            _view.ReportReasonClicked += OnReportReason;
            _view.ReportCancelClicked += OnReportCancel;
            _view.BackClicked += OnBack;
            _view.SetDevToolsVisible(DevFlags.OnlineTestTools);
            _view.SetActiveTab(_tab);
            LoadAsync().Forget();
        }

        public void Exit()
        {
            _view.TabSelected -= OnTab;
            _view.RefreshClicked -= OnRefresh;
            _view.BotMarketClicked -= OnBotMarket;
            _view.AmountConfirmClicked -= OnAmountConfirm;
            _view.AmountCancelClicked -= OnAmountCancel;
            _view.ReportReasonClicked -= OnReportReason;
            _view.ReportCancelClicked -= OnReportCancel;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => LoadAsync().Forget();

        // --- loading ---------------------------------------------------------------------------------

        private async UniTask LoadAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);

            var mine = await _ranked.GetMineAsync();
            if (mine.Success && mine.Value != null)
            {
                _groupId = mine.Value.groupId;
                _myClubExt = mine.Value.clubExternalId ?? -1;
            }

            var auctions = await _ranked.GetAuctionsAsync();
            if (auctions.Success) _auctions = auctions.Value;

            var offers = await _ranked.GetOffersAsync();
            if (offers.Success) _offers = offers.Value;

            // The group's clubs (for the browse tab) come from the season standings.
            var season = await _ranked.GetSeasonAsync();
            if (season.Success && season.Value?.standings != null) _clubs = season.Value.standings;

            _busy = false;
            _view.SetBusy(false);
            Render();
        }

        private void Render()
        {
            long budget = _offers?.yourBudget ?? _auctions?.budget ?? 0;
            bool open = (_offers?.marketOpen ?? false) || (_auctions?.windowOpen ?? false);

            _view.SetBudget(_loc.Tr("ranked.market.budget", MoneyFormat.Short(budget)));
            _view.SetBanner(_loc.Tr(open ? "ranked.market.window_open" : "ranked.market.window_closed"), open);
            _view.SetActiveTab(_tab);

            switch (_tab)
            {
                case TabOffers: RenderOffers(); break;
                case TabBrowse: RenderBrowse(); break;
                default: RenderAuctions(); break;
            }
        }

        private void RenderAuctions()
        {
            var rows = new List<RankedMarketView.RowVm>();
            if (_auctions?.lots != null)
            {
                foreach (var lot in _auctions.lots.OrderByDescending(l => l.overall))
                {
                    string id = lot.id;
                    rows.Add(new RankedMarketView.RowVm
                    {
                        Title = _loc.Tr("ranked.market.lot_title", lot.playerName, lot.overall, lot.age),
                        RoleGroup = RoleFormat.Group((PositionRole)lot.role),
                        RoleAbbr = RoleName(lot.role),
                        Badge = lot.youAreLeading ? _loc.Tr("auction.badge_leading") : null,
                        BadgeKind = lot.youAreLeading ? 1 : 0,
                        Detail = lot.highBid > 0
                            ? _loc.Tr("ranked.market.lot_bid", MoneyFormat.Short(lot.highBid),
                                lot.youAreLeading ? _loc.Tr("ranked.market.you") : ClubName(lot.highBidClubExternalId),
                                MoneyFormat.Short(lot.minNextBid))
                            : _loc.Tr("ranked.market.lot_base", MoneyFormat.Short(lot.startPrice)),
                        PrimaryLabel = lot.youAreLeading ? null : _loc.Tr("ranked.market.bid"),
                        PrimaryAction = () => OnBid(id),
                        Dimmed = lot.youAreLeading,
                    });
                }
            }
            _view.SetRows(rows);
        }

        /// <summary>Names the club that holds a lot — the standings we already fetched are the group's
        /// club directory, so a bid can say WHO made it instead of showing a bare id.</summary>
        private string ClubName(int? clubExternalId)
        {
            if (!clubExternalId.HasValue) return "?";
            foreach (var c in _clubs)
                if (c.clubExternalId == clubExternalId.Value) return c.clubName;
            return "#" + clubExternalId.Value;
        }

        private string RoleName(int role) =>
            _loc.Tr("role." + ((PositionRole)role).ToString().ToLowerInvariant());

        private void RenderOffers()
        {
            var rows = new List<RankedMarketView.RowVm>();
            if (_offers != null)
            {
                foreach (var o in _offers.incoming)
                {
                    string id = o.id;
                    bool pending = o.status == (int)RankedOfferStatus.Pending;
                    rows.Add(new RankedMarketView.RowVm
                    {
                        Title = _loc.Tr("ranked.market.offer_in", o.playerName, MoneyFormat.Short(o.fee), o.buyerClubName),
                        Detail = _loc.Tr(StatusKey(o.status)),
                        PrimaryLabel = pending ? _loc.Tr("ranked.market.accept") : null,
                        PrimaryAction = () => OnOfferAccept(id),
                        SecondaryLabel = pending ? _loc.Tr("ranked.market.reject") : null,
                        SecondaryAction = () => OnOfferReject(id),
                        Dimmed = !pending,
                    });
                }
                foreach (var o in _offers.outgoing)
                {
                    string id = o.id;
                    bool pending = o.status == (int)RankedOfferStatus.Pending;
                    rows.Add(new RankedMarketView.RowVm
                    {
                        Title = _loc.Tr("ranked.market.offer_out", o.playerName, MoneyFormat.Short(o.fee), o.sellerClubName),
                        Detail = _loc.Tr(StatusKey(o.status)),
                        PrimaryLabel = pending ? _loc.Tr("ranked.market.withdraw") : null,
                        PrimaryAction = () => OnOfferWithdraw(id),
                        Dimmed = !pending,
                    });
                }
            }
            _view.SetRows(rows);
        }

        private void RenderBrowse()
        {
            var rows = new List<RankedMarketView.RowVm>();

            // No club chosen yet → list the group's clubs (yours excluded).
            if (_browsedSquad == null)
            {
                foreach (var c in _clubs)
                {
                    if (c.clubExternalId == _myClubExt) continue;
                    int ext = c.clubExternalId;
                    rows.Add(new RankedMarketView.RowVm
                    {
                        Title = c.clubName,
                        PrimaryLabel = _loc.Tr("ranked.market.view_squad"),
                        PrimaryAction = () => OnClubSelected(ext),
                    });
                }
                _view.SetRows(rows);
                return;
            }

            // A club is open → its players, each offerable. The club's own row also carries the report
            // action (Phase 9.5): this is the first point where the client knows whether the club is run by
            // a coach at all — an AI seat has nobody to report.
            int browsedClubExt = _browsedSquad.clubExternalId;
            rows.Add(new RankedMarketView.RowVm
            {
                Title = _browsedSquad.clubName,
                Detail = _loc.Tr(_browsedSquad.isHuman ? "ranked.market.human_club" : "ranked.market.ai_club"),
                PrimaryLabel = _loc.Tr("ranked.market.back_to_clubs"),
                PrimaryAction = () => { _browsedSquad = null; Render(); },
                SecondaryLabel = _browsedSquad.isHuman ? _loc.Tr("ranked.report.open") : null,
                SecondaryAction = () => OnReport(browsedClubExt),
            });
            foreach (var p in _browsedSquad.players)
            {
                int pid = p.externalId;
                rows.Add(new RankedMarketView.RowVm
                {
                    Title = _loc.Tr("ranked.market.lot_title", p.name, p.overall, p.age),
                    RoleGroup = RoleFormat.Group((PositionRole)p.role),
                    RoleAbbr = RoleName(p.role),
                    Detail = _loc.Tr("ranked.market.value", MoneyFormat.Short(p.marketValue)),
                    PrimaryLabel = _browsedSquad.isHuman ? _loc.Tr("ranked.market.offer") : null,
                    PrimaryAction = () => OnOfferForPlayer(pid),
                });
            }
            _view.SetRows(rows);
        }

        private static string StatusKey(int status) => status switch
        {
            (int)RankedOfferStatus.Accepted => "ranked.market.status_accepted",
            (int)RankedOfferStatus.Rejected => "ranked.market.status_rejected",
            (int)RankedOfferStatus.Withdrawn => "ranked.market.status_withdrawn",
            _ => "ranked.market.status_pending",
        };

        // --- actions ---------------------------------------------------------------------------------

        private void OnTab(int tab)
        {
            _tab = tab;
            if (tab != TabBrowse) _browsedSquad = null;
            _view.ClearStatus();
            _view.HideAmountPanel();
            _view.HideReportPanel();
            _pendingReportClubExt = -1;
            Render();
        }

        private void OnRefresh() => LoadAsync().Forget();

        private void OnBid(string lotId)
        {
            var lot = _auctions?.lots?.FirstOrDefault(l => l.id == lotId);
            if (lot == null) return;
            _pendingBidLotId = lotId;
            _pendingOfferPlayerId = -1;

            long current = lot.highBid > 0 ? lot.highBid : lot.startPrice;
            long available = _auctions?.available ?? 0;
            _view.ShowAmountPanel(new BidPanelVm
            {
                Id = lotId,
                Title = _loc.Tr("ranked.market.bid_for", lot.playerName),
                RoleGroup = RoleFormat.Group((PositionRole)lot.role),
                RoleAbbr = RoleName(lot.role),
                Subtitle = _loc.Tr("auction.min_info",
                    MoneyFormat.Short(lot.minNextBid), MoneyFormat.Short(available)),
                Min = lot.minNextBid,
                Max = available,
                Steps = BidSteps.For(current, System.Math.Max(MinIncrementFloor, current / 20)),
                ConfirmFormat = _loc.Tr("auction.confirm_amount"),
                MinLabel = _loc.Tr("auction.set_min"),
                MaxLabel = _loc.Tr("auction.set_max"),
                CancelLabel = _loc.Tr("ranked.market.cancel"),
                OverBudget = _loc.Tr("auction.over_budget"),
            });
        }

        private void OnClubSelected(int clubExternalId) => BrowseAsync(clubExternalId).Forget();

        private async UniTaskVoid BrowseAsync(int clubExternalId)
        {
            _view.SetBusy(true);
            var squad = await _ranked.GetClubSquadAsync(clubExternalId);
            _view.SetBusy(false);
            if (!squad.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(squad.Error)), isError: true);
                return;
            }
            _browsedSquad = squad.Value;
            _view.ClearStatus();
            Render();
        }

        private void OnOfferForPlayer(int playerExternalId)
        {
            var p = _browsedSquad?.players?.FirstOrDefault(x => x.externalId == playerExternalId);
            if (p == null) return;
            _pendingOfferPlayerId = playerExternalId;
            _pendingBidLotId = null;

            // The fee has to sit inside the integrity band (a gift and a bribe are both refused server-side,
            // Phase 9.5), so the control opens on the market value and cannot leave the legal range.
            long budget = _offers?.yourBudget ?? 0;
            long floor = p.marketValue * MinFeePercentOfValue / 100;
            long ceiling = System.Math.Min(budget, p.marketValue * MaxFeePercentOfValue / 100);
            _view.ShowAmountPanel(new BidPanelVm
            {
                Id = playerExternalId.ToString(),
                Title = _loc.Tr("ranked.market.offer_for", p.name),
                RoleGroup = RoleFormat.Group((PositionRole)p.role),
                RoleAbbr = RoleName(p.role),
                Subtitle = _loc.Tr("ranked.market.fee_band",
                    MoneyFormat.Short(p.marketValue), MoneyFormat.Short(floor), MoneyFormat.Short(ceiling)),
                Min = floor,
                Max = ceiling,
                Steps = BidSteps.For(p.marketValue, System.Math.Max(MinIncrementFloor, p.marketValue / 50)),
                ConfirmFormat = _loc.Tr("ranked.market.offer_amount"),
                MinLabel = _loc.Tr("auction.set_min"),
                MaxLabel = _loc.Tr("auction.set_max"),
                CancelLabel = _loc.Tr("ranked.market.cancel"),
                OverBudget = _loc.Tr("ranked.market.cannot_afford"),
            });
        }

        private void OnAmountCancel()
        {
            _pendingBidLotId = null;
            _pendingOfferPlayerId = -1;
            _view.HideAmountPanel();
        }

        private void OnAmountConfirm(long amount) => ConfirmAmountAsync(amount).Forget();

        private async UniTaskVoid ConfirmAmountAsync(long amount)
        {
            if (_busy || amount <= 0) return;
            _busy = true;
            _view.SetBusy(true);

            RankedApiError error = RankedApiError.None;
            bool ok;
            if (_pendingBidLotId != null)
            {
                var res = await _ranked.PlaceBidAsync(_pendingBidLotId, amount);
                ok = res.Success; error = res.Error;
            }
            else if (_pendingOfferPlayerId >= 0)
            {
                var res = await _ranked.MakeOfferAsync(_pendingOfferPlayerId, amount);
                ok = res.Success; error = res.Error;
            }
            else ok = false;

            _busy = false;
            _view.SetBusy(false);
            if (!ok)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(error)), isError: true);
                return;
            }

            _pendingBidLotId = null;
            _pendingOfferPlayerId = -1;
            _view.HideAmountPanel();
            _view.ShowStatus(_loc.Tr("ranked.market.done"), isError: false);
            await LoadAsync();
        }

        private void OnOfferAccept(string offerId) => RespondAsync(offerId, true).Forget();
        private void OnOfferReject(string offerId) => RespondAsync(offerId, false).Forget();

        private async UniTaskVoid RespondAsync(string offerId, bool accept)
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            var res = await _ranked.RespondOfferAsync(offerId, accept);
            _busy = false;
            _view.SetBusy(false);
            if (!res.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(res.Error)), isError: true);
                return;
            }
            _view.ShowStatus(_loc.Tr("ranked.market.done"), isError: false);
            await LoadAsync();
        }

        private void OnOfferWithdraw(string offerId) => WithdrawAsync(offerId).Forget();

        private async UniTaskVoid WithdrawAsync(string offerId)
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            var res = await _ranked.WithdrawOfferAsync(offerId);
            _busy = false;
            _view.SetBusy(false);
            if (!res.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(res.Error)), isError: true);
                return;
            }
            await LoadAsync();
        }

        // --- reporting a coach (Phase 9.5) -----------------------------------------------------------

        private void OnReport(int clubExternalId)
        {
            _pendingReportClubExt = clubExternalId;
            _view.HideAmountPanel();
            _view.ClearStatus();
            string clubName = _browsedSquad?.clubName
                              ?? _clubs.FirstOrDefault(c => c.clubExternalId == clubExternalId)?.clubName
                              ?? string.Empty;
            _view.ShowReportPanel(_loc.Tr("ranked.report.title", clubName));
        }

        private void OnReportCancel()
        {
            _pendingReportClubExt = -1;
            _view.HideReportPanel();
        }

        private void OnReportReason(int reason) => SendReportAsync(reason).Forget();

        private async UniTaskVoid SendReportAsync(int reason)
        {
            if (_busy || _pendingReportClubExt < 0) return;
            _busy = true;
            _view.SetBusy(true);

            var res = await _ranked.ReportAsync(_pendingReportClubExt, (RankedReportReason)reason);

            _busy = false;
            _view.SetBusy(false);
            if (!res.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(res.Error)), isError: true);
                return;
            }

            // The server tells a reporter nothing beyond "filed" — so neither do we.
            _pendingReportClubExt = -1;
            _view.HideReportPanel();
            _view.ShowStatus(_loc.Tr("ranked.report.sent"), isError: false);
        }

        // Dev-only: the group's bots outbid on the lots and answer the offers you sent them.
        private void OnBotMarket() => BotMarketAsync().Forget();

        private async UniTaskVoid BotMarketAsync()
        {
            if (_busy || string.IsNullOrEmpty(_groupId)) return;
            _busy = true;
            _view.SetBusy(true);
            await _ranked.BotMarketDevAsync(_groupId);
            _busy = false;
            _view.SetBusy(false);
            await LoadAsync();
        }

        private void OnBack() => _navigator.Pop();
    }
}
