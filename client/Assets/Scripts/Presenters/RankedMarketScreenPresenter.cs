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
    /// The ranked market screen (Phase 9.2b): direct OFFERS (incoming to answer / outgoing to withdraw) and
    /// BROWSE (pick a rival club, then offer for one of its players). Everything is gated server-side to an
    /// open market window; the screen just reports what the server says. Reached from the ranked season
    /// screen.
    ///
    /// The AUCTIONS were a third tab here until task 12.2. They moved to their own screen
    /// (<see cref="RankedAuctionScreenPresenter"/>) when the lots got their own timers and a coach could
    /// start selling: a board you can sell into needs the auction room, not a list. What stayed behind is
    /// the way in — and the banner, which now says WHEN the window shuts and when the next one opens,
    /// because "when do auctions come back?" used to be unanswerable inside the app.
    /// </summary>
    public sealed class RankedMarketScreenPresenter : IScreenPresenter
    {
        /// <summary>The server's own bid floor (5% of the standing bid, never below this).</summary>
        private const long MinIncrementFloor = 25_000;

        /// <summary>The integrity band a direct offer has to sit in, mirroring the server's Phase 9.5
        /// guard: below 40% of market value is a gift, above 250% is a bribe — both refused.</summary>
        private const int MinFeePercentOfValue = 40;
        private const int MaxFeePercentOfValue = 250;

        private const int TabOffers = 0;
        private const int TabBrowse = 1;

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly RankedMarketView _view;

        private int _tab = TabOffers;
        private bool _busy;
        private string _groupId;
        private int _myClubExt = -1;

        private RankedAuctionsDto _auctions;
        private RankedOffersDto _offers;
        private List<RankedStandingDto> _clubs = new List<RankedStandingDto>();
        private RankedSquadDto _browsedSquad;

        // What the inline amount panel is currently for (offers only since task 12.2 — the bidding lives
        // on the auction screen now).
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
            _view.AuctionsClicked += OnAuctions;
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
            _view.AuctionsClicked -= OnAuctions;
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

            // TASK 12.2: say WHEN. A calendar-driven window with no clock on screen was the one question
            // the app could not answer — the instants come from the auction board and are shown in the
            // device's own time.
            string banner;
            if (open)
                banner = string.IsNullOrEmpty(_auctions?.windowClosesUtc)
                    ? _loc.Tr("ranked.market.window_open")
                    : _loc.Tr("ranked.market.window_open_until", FormatTime(_auctions.windowClosesUtc));
            else
                banner = string.IsNullOrEmpty(_auctions?.nextWindowOpensUtc)
                    ? _loc.Tr("ranked.market.window_closed")
                    : _loc.Tr("ranked.market.window_closed_next", FormatTime(_auctions.nextWindowOpensUtc));
            _view.SetBanner(banner, open);
            _view.SetActiveTab(_tab);

            switch (_tab)
            {
                case TabBrowse: RenderBrowse(); break;
                default: RenderOffers(); break;
            }
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

        /// <summary>Task 12.2 — the lots live on their own screen now (they have their own timers, and you
        /// can put your own players on the board).</summary>
        private void OnAuctions() => _navigator.Push<RankedAuctionScreenPresenter>();

        /// <summary>Best-effort local time from the server's ISO instant.</summary>
        private static string FormatTime(string isoUtc)
        {
            return System.DateTime.TryParse(
                isoUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("g")
                : isoUtc;
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
            if (_pendingOfferPlayerId >= 0)
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
