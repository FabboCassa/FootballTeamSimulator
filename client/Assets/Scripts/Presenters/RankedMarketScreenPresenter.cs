using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
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

        public VisualElement View => _view.Root;

        public RankedMarketScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
            _view = new RankedMarketView(loc.Tr);
        }

        public void Enter()
        {
            _view.TabSelected += OnTab;
            _view.RefreshClicked += OnRefresh;
            _view.BotMarketClicked += OnBotMarket;
            _view.AmountConfirmClicked += OnAmountConfirm;
            _view.AmountCancelClicked += OnAmountCancel;
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
                        Detail = lot.highBid > 0
                            ? _loc.Tr("ranked.market.lot_bid", MoneyFormat.Short(lot.highBid),
                                lot.youAreLeading ? _loc.Tr("ranked.market.you") : (lot.highBidClubExternalId?.ToString() ?? "?"),
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

            // A club is open → its players, each offerable.
            rows.Add(new RankedMarketView.RowVm
            {
                Title = _browsedSquad.clubName,
                Detail = _loc.Tr(_browsedSquad.isHuman ? "ranked.market.human_club" : "ranked.market.ai_club"),
                PrimaryLabel = _loc.Tr("ranked.market.back_to_clubs"),
                PrimaryAction = () => { _browsedSquad = null; Render(); },
            });
            foreach (var p in _browsedSquad.players)
            {
                int pid = p.externalId;
                rows.Add(new RankedMarketView.RowVm
                {
                    Title = _loc.Tr("ranked.market.lot_title", p.name, p.overall, p.age),
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
            Render();
        }

        private void OnRefresh() => LoadAsync().Forget();

        private void OnBid(string lotId)
        {
            var lot = _auctions?.lots?.FirstOrDefault(l => l.id == lotId);
            if (lot == null) return;
            _pendingBidLotId = lotId;
            _pendingOfferPlayerId = -1;
            _view.ShowAmountPanel(_loc.Tr("ranked.market.bid_for", lot.playerName), lot.minNextBid);
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
            // Suggest the player's market value as the opening fee.
            _view.ShowAmountPanel(_loc.Tr("ranked.market.offer_for", p.name), p.marketValue);
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
