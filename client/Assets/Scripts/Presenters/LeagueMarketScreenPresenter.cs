using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using Sim.Core.Domain;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// The private-league transfer market (Phase 12.1b). Four tabs — the live NEGOTIATIONS (the ones
    /// waiting on you first, then what you sent and received, then the league's recent deals), BUY (pick a
    /// club, then offer for one of its players), SELL (your squad, on and off the transfer list) and FREE
    /// AGENTS (agree terms with an unattached player).
    ///
    /// The screen is a thin skin over one endpoint: every action returns the WHOLE market, so there is a
    /// single source of truth and no local patching that could drift from the server. Everything is gated
    /// server-side to a round-based window; the screen only reports what the server says.
    ///
    /// Two things here are worth knowing when reading the code. First, a bot answers INSIDE the request:
    /// offering to a bot club returns a market where that negotiation is already accepted, countered or
    /// refused, so the screen never has to poll for an AI. Second, an offer nobody answers EXPIRES when
    /// the round resolves — that is the user's rule for a league played together — which is why the
    /// "waiting on you" section is the default tab and carries a count.
    /// </summary>
    public sealed class LeagueMarketScreenPresenter : IScreenPresenter
    {
        /// <summary>The 9.5 integrity band, mirrored so the figure control cannot leave the legal range:
        /// under 40% of market value is a gift, over 250% a bribe, and the server refuses both.</summary>
        private const int MinFeePercentOfValue = 40;
        private const int MaxFeePercentOfValue = 250;

        /// <summary>The server prepays this many weeks of wage out of the transfer budget on a free
        /// signing (LeagueMarketEngine.WagePrepaidWeeks) — the screen needs it to show the real cost.</summary>
        private const int WagePrepaidWeeks = 52;

        private const int TabOffers = 0;
        private const int TabBuy = 1;
        private const int TabSell = 2;
        private const int TabFree = 3;

        /// <summary>What the inline figure control is currently collecting.</summary>
        private enum Pending { None, Offer, Counter, Listing, Wage }

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly LeagueSelection _selection;
        private readonly LeagueMarketView _view;

        private LeagueMarketDto _market;
        private int _tab = TabOffers;
        private bool _busy;

        private int _browsedClubExt = -1;

        private Pending _pending = Pending.None;
        private int _pendingPlayerExt = -1;
        private string _pendingOfferId;
        private int _pendingSeasons;

        public VisualElement View => _view.Root;

        public LeagueMarketScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues, LeagueSelection selection)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _view = new LeagueMarketView(loc.Tr, MoneyFormat.Short);
        }

        public void Enter()
        {
            _view.TabSelected += OnTab;
            _view.RefreshClicked += OnRefresh;
            _view.AmountConfirmClicked += OnAmountConfirm;
            _view.AmountCancelClicked += OnAmountCancel;
            _view.ChoiceClicked += OnChoice;
            _view.ChoiceCancelClicked += OnChoiceCancel;
            _view.BackClicked += OnBack;
            _view.SetActiveTab(_tab);
            LoadAsync().Forget();
        }

        public void Exit()
        {
            _view.TabSelected -= OnTab;
            _view.RefreshClicked -= OnRefresh;
            _view.AmountConfirmClicked -= OnAmountConfirm;
            _view.AmountCancelClicked -= OnAmountCancel;
            _view.ChoiceClicked -= OnChoice;
            _view.ChoiceCancelClicked -= OnChoiceCancel;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => LoadAsync().Forget();

        // --- loading ---------------------------------------------------------------------------------

        private async UniTask LoadAsync()
        {
            if (_busy || string.IsNullOrEmpty(_selection.LeagueId)) return;
            _busy = true;
            _view.SetBusy(true);

            var result = await _leagues.GetMarketAsync(_selection.LeagueId);

            _busy = false;
            _view.SetBusy(false);

            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
                return;
            }

            _market = result.Value;
            Render();
        }

        /// <summary>Every action returns the whole market, so the screen simply adopts it.</summary>
        private void Adopt(LeagueMarketDto market)
        {
            if (market != null) _market = market;
            Render();
        }

        private void Render()
        {
            _view.SetHeader(_loc.Tr("league.market.title"));
            _view.SetBudget(_loc.Tr("league.market.budget",
                MoneyFormat.Short(_market?.yourBudget ?? 0), _market?.yourSquadSize ?? 0));
            _view.SetBanner(WindowText(), _market?.window?.open ?? false);
            _view.SetPendingCount(_market?.awaitingYou?.Count ?? 0);
            _view.SetActiveTab(_tab);

            switch (_tab)
            {
                case TabBuy: RenderBuy(); break;
                case TabSell: RenderSell(); break;
                case TabFree: RenderFreeAgents(); break;
                default: RenderNegotiations(); break;
            }
        }

        /// <summary>A private league runs on ROUNDS, not on a clock, so the banner talks in rounds: which
        /// window is open, the round that will shut it, or the round that reopens it.</summary>
        private string WindowText()
        {
            LeagueMarketWindowDto w = _market?.window;
            if (w == null) return string.Empty;
            if (w.open)
                return _loc.Tr(w.windowIndex == 0 ? "league.market.window_pre" : "league.market.window_mid",
                    w.closesAfterRound);
            if (w.nextOpensAfterRound > 0)
                return _loc.Tr("league.market.window_next", w.nextOpensAfterRound);
            return _loc.Tr("league.market.window_closed");
        }

        // --- negotiations ----------------------------------------------------------------------------

        private void RenderNegotiations()
        {
            var rows = new List<LeagueMarketView.RowVm>();
            if (_market != null)
            {
                var awaiting = _market.awaitingYou ?? new List<LeagueOfferDto>();
                var awaitingIds = new HashSet<string>(awaiting.Select(o => o.id));

                if (awaiting.Count > 0)
                {
                    rows.Add(Header(_loc.Tr("league.market.section_awaiting")));
                    foreach (var o in awaiting) rows.Add(NegotiationRow(o, awaitingYou: true));
                }

                var sent = (_market.outgoing ?? new List<LeagueOfferDto>())
                    .Where(o => !awaitingIds.Contains(o.id)).ToList();
                if (sent.Count > 0)
                {
                    rows.Add(Header(_loc.Tr("league.market.section_sent")));
                    foreach (var o in sent) rows.Add(NegotiationRow(o, awaitingYou: false));
                }

                var received = (_market.incoming ?? new List<LeagueOfferDto>())
                    .Where(o => !awaitingIds.Contains(o.id)).ToList();
                if (received.Count > 0)
                {
                    rows.Add(Header(_loc.Tr("league.market.section_received")));
                    foreach (var o in received) rows.Add(NegotiationRow(o, awaitingYou: false));
                }

                var news = _market.news ?? new List<LeagueTransferNewsDto>();
                if (news.Count > 0)
                {
                    rows.Add(Header(_loc.Tr("league.market.section_news")));
                    foreach (var n in news)
                    {
                        rows.Add(new LeagueMarketView.RowVm
                        {
                            Title = n.playerName,
                            Detail = _loc.Tr("league.market.news_row",
                                n.fromClubName, n.toClubName,
                                n.fee > 0 ? MoneyFormat.Short(n.fee) : _loc.Tr("league.market.free")),
                            Highlight = n.involvesYou,
                        });
                    }
                }
            }
            _view.SetRows(rows);
        }

        private static LeagueMarketView.RowVm Header(string title) =>
            new LeagueMarketView.RowVm { Title = title, IsHeader = true };

        private LeagueMarketView.RowVm NegotiationRow(LeagueOfferDto o, bool awaitingYou)
        {
            string id = o.id;
            bool pending = o.status == (int)LeagueOfferStatus.Pending;
            string other = o.youAreBuyer ? o.sellerClubName : o.buyerClubName;

            var vm = new LeagueMarketView.RowVm
            {
                Title = _loc.Tr("league.market.offer_title", o.playerName, MoneyFormat.Short(o.amount)),
                RoleGroup = RoleFormat.Group((PositionRole)o.playerRole),
                RoleAbbr = RoleName(o.playerRole),
                Detail = _loc.Tr(o.youAreBuyer ? "league.market.offer_to" : "league.market.offer_from", other)
                         + " · " + _loc.Tr(StatusKey(o.status, awaitingYou, o.proposedBy, o.youAreBuyer)),
                Badge = awaitingYou ? _loc.Tr("league.market.badge_your_move") : null,
                BadgeKind = awaitingYou ? 3 : 0,
                Highlight = awaitingYou,
                Dimmed = !pending,
            };

            if (awaitingYou)
            {
                // Three answers, because a negotiation genuinely has three.
                vm.PrimaryLabel = _loc.Tr("league.market.accept");
                vm.PrimaryAction = () => RespondAsync(id, LeagueOfferAction.Accept, 0).Forget();
                vm.SecondaryLabel = _loc.Tr("league.market.counter");
                vm.SecondaryAction = () => OnCounter(id);
                vm.TertiaryLabel = o.youAreBuyer ? _loc.Tr("league.market.withdraw") : _loc.Tr("league.market.reject");
                vm.TertiaryAction = () => RespondAsync(
                    id, o.youAreBuyer ? LeagueOfferAction.Withdraw : LeagueOfferAction.Reject, 0).Forget();
            }
            else if (pending && o.youAreBuyer)
            {
                // Waiting on the other coach: the only move left is to pull it.
                vm.PrimaryLabel = _loc.Tr("league.market.withdraw");
                vm.PrimaryAction = () => RespondAsync(id, LeagueOfferAction.Withdraw, 0).Forget();
            }

            return vm;
        }

        /// <summary>What the negotiation is doing right now — "your move", "waiting for him", or how it
        /// ended. A private league expires an unanswered offer at the round, so Expired has its own line.</summary>
        private static string StatusKey(int status, bool awaitingYou, int proposedBy, bool youAreBuyer)
        {
            if (status == (int)LeagueOfferStatus.Accepted) return "league.market.status_accepted";
            if (status == (int)LeagueOfferStatus.Rejected) return "league.market.status_rejected";
            if (status == (int)LeagueOfferStatus.Withdrawn) return "league.market.status_withdrawn";
            if (status == (int)LeagueOfferStatus.Expired) return "league.market.status_expired";
            if (awaitingYou)
                return proposedBy == (int)LeagueOfferParty.Seller
                    ? "league.market.status_countered_you"
                    : "league.market.status_your_move";
            return youAreBuyer ? "league.market.status_waiting_him" : "league.market.status_waiting_you_sent";
        }

        // --- buy -------------------------------------------------------------------------------------

        private void RenderBuy()
        {
            var rows = new List<LeagueMarketView.RowVm>();
            var clubs = _market?.clubs ?? new List<LeagueMarketSquadDto>();

            if (_browsedClubExt < 0)
            {
                foreach (var c in clubs.OrderByDescending(c => c.isHuman).ThenBy(c => c.clubName))
                {
                    int ext = c.clubExternalId;
                    rows.Add(new LeagueMarketView.RowVm
                    {
                        Title = c.clubName,
                        Badge = _loc.Tr(c.isHuman ? "league.market.human_club" : "league.market.bot_club"),
                        BadgeKind = c.isHuman ? 3 : 0,
                        Detail = _loc.Tr("league.market.club_detail",
                            c.players?.Count ?? 0, MoneyFormat.Short(c.transferBudget)),
                        PrimaryLabel = _loc.Tr("league.market.view_squad"),
                        PrimaryAction = () => { _browsedClubExt = ext; _view.ClearStatus(); Render(); },
                    });
                }
                _view.SetRows(rows);
                return;
            }

            var club = clubs.FirstOrDefault(c => c.clubExternalId == _browsedClubExt);
            if (club == null)
            {
                _browsedClubExt = -1;
                RenderBuy();
                return;
            }

            rows.Add(new LeagueMarketView.RowVm
            {
                Title = club.clubName,
                Detail = _loc.Tr(club.isHuman ? "league.market.human_club_hint" : "league.market.bot_club_hint"),
                PrimaryLabel = _loc.Tr("league.market.back_to_clubs"),
                PrimaryAction = () => { _browsedClubExt = -1; Render(); },
            });

            foreach (var p in club.players ?? new List<LeagueMarketPlayerDto>())
            {
                int pid = p.externalId;
                rows.Add(new LeagueMarketView.RowVm
                {
                    Title = _loc.Tr("league.market.player_title", p.name, p.overall, p.age),
                    RoleGroup = RoleFormat.Group((PositionRole)p.role),
                    RoleAbbr = RoleName(p.role),
                    Badge = p.listed ? _loc.Tr("league.market.badge_listed") : null,
                    BadgeKind = p.listed ? 1 : 0,
                    Detail = p.listed
                        ? _loc.Tr("league.market.player_listed",
                            MoneyFormat.Short(p.marketValue), MoneyFormat.Short(p.askingPrice))
                        : _loc.Tr("league.market.player_value", MoneyFormat.Short(p.marketValue)),
                    PrimaryLabel = _loc.Tr("league.market.offer"),
                    PrimaryAction = () => OnOffer(pid),
                });
            }

            _view.SetRows(rows);
        }

        // --- sell ------------------------------------------------------------------------------------

        private void RenderSell()
        {
            var rows = new List<LeagueMarketView.RowVm>();
            foreach (var p in _market?.yourSquad ?? new List<LeagueMarketPlayerDto>())
            {
                int pid = p.externalId;
                bool listed = p.listed;
                rows.Add(new LeagueMarketView.RowVm
                {
                    Title = _loc.Tr("league.market.player_title", p.name, p.overall, p.age),
                    RoleGroup = RoleFormat.Group((PositionRole)p.role),
                    RoleAbbr = RoleName(p.role),
                    Badge = listed ? _loc.Tr("league.market.badge_listed") : null,
                    BadgeKind = listed ? 1 : 0,
                    Detail = listed
                        ? _loc.Tr("league.market.player_listed",
                            MoneyFormat.Short(p.marketValue), MoneyFormat.Short(p.askingPrice))
                        : _loc.Tr("league.market.player_own",
                            MoneyFormat.Short(p.marketValue), MoneyFormat.Short(p.weeklyWage),
                            p.contractSeasonsRemaining),
                    PrimaryLabel = _loc.Tr(listed ? "league.market.unlist" : "league.market.list"),
                    PrimaryAction = listed
                        ? (Action)(() => SetListingAsync(pid, false, 0).Forget())
                        : () => OnList(pid),
                });
            }
            _view.SetRows(rows);
        }

        // --- free agents -----------------------------------------------------------------------------

        private void RenderFreeAgents()
        {
            var rows = new List<LeagueMarketView.RowVm>();
            foreach (var f in _market?.freeAgents ?? new List<LeagueFreeAgentDto>())
            {
                int pid = f.externalId;
                bool affordable = f.signingCostAtDemand <= (_market?.yourBudget ?? 0);
                rows.Add(new LeagueMarketView.RowVm
                {
                    Title = _loc.Tr("league.market.player_title", f.name, f.overall, f.age),
                    RoleGroup = RoleFormat.Group((PositionRole)f.role),
                    RoleAbbr = RoleName(f.role),
                    Detail = _loc.Tr("league.market.free_agent_detail",
                        MoneyFormat.Short(f.demandedWeeklyWage), MoneyFormat.Short(f.signingCostAtDemand)),
                    PrimaryLabel = _loc.Tr("league.market.sign"),
                    PrimaryAction = () => OnSign(pid),
                    Dimmed = !affordable,
                });
            }
            _view.SetRows(rows);
        }

        // --- opening the figure control ---------------------------------------------------------------

        private void OnOffer(int playerExternalId)
        {
            var p = FindPlayer(playerExternalId);
            if (p == null) return;

            long budget = _market?.yourBudget ?? 0;
            long floor = Math.Max(1, p.marketValue * MinFeePercentOfValue / 100);
            long ceiling = Math.Min(budget, p.marketValue * MaxFeePercentOfValue / 100);

            _pending = Pending.Offer;
            _pendingPlayerExt = playerExternalId;
            _pendingOfferId = null;

            _view.ShowAmountPanel(new BidPanelVm
            {
                Id = playerExternalId.ToString(),
                Title = _loc.Tr("league.market.offer_for", p.name),
                RoleGroup = RoleFormat.Group((PositionRole)p.role),
                RoleAbbr = RoleName(p.role),
                Subtitle = _loc.Tr("league.market.fee_band",
                    MoneyFormat.Short(p.marketValue), MoneyFormat.Short(floor), MoneyFormat.Short(ceiling)),
                Min = floor,
                Max = ceiling,
                Steps = BidSteps.For(p.marketValue, Math.Max(25_000, p.marketValue / 50)),
                ConfirmFormat = _loc.Tr("league.market.confirm_offer"),
                MinLabel = _loc.Tr("auction.set_min"),
                MaxLabel = _loc.Tr("auction.set_max"),
                CancelLabel = _loc.Tr("league.market.cancel"),
                OverBudget = _loc.Tr("league.market.cannot_afford"),
            });
        }

        private void OnCounter(string offerId)
        {
            var o = AllOffers().FirstOrDefault(x => x.id == offerId);
            if (o == null) return;

            long value = Math.Max(1, o.playerMarketValue);
            long budget = _market?.yourBudget ?? 0;
            long bandFloor = Math.Max(1, value * MinFeePercentOfValue / 100);
            long bandCeiling = value * MaxFeePercentOfValue / 100;

            // As the BUYER you are bounded by your budget as well as by the band; as the SELLER you are
            // asking, not paying, so only the band binds — but never below what is already on the table.
            long min = o.youAreBuyer ? bandFloor : Math.Max(bandFloor, o.amount);
            long max = o.youAreBuyer ? Math.Min(budget, bandCeiling) : bandCeiling;

            _pending = Pending.Counter;
            _pendingOfferId = offerId;
            _pendingPlayerExt = o.playerExternalId;

            _view.ShowAmountPanel(new BidPanelVm
            {
                Id = offerId,
                Title = _loc.Tr("league.market.counter_for", o.playerName),
                RoleGroup = RoleFormat.Group((PositionRole)o.playerRole),
                RoleAbbr = RoleName(o.playerRole),
                Subtitle = _loc.Tr("league.market.counter_band",
                    MoneyFormat.Short(o.amount), MoneyFormat.Short(min), MoneyFormat.Short(max)),
                Min = min,
                Max = max,
                Steps = BidSteps.For(Math.Max(o.amount, value), Math.Max(25_000, value / 50)),
                ConfirmFormat = _loc.Tr("league.market.confirm_counter"),
                MinLabel = _loc.Tr("auction.set_min"),
                MaxLabel = _loc.Tr("auction.set_max"),
                CancelLabel = _loc.Tr("league.market.cancel"),
                OverBudget = _loc.Tr("league.market.cannot_afford"),
            });
        }

        private void OnList(int playerExternalId)
        {
            var p = (_market?.yourSquad ?? new List<LeagueMarketPlayerDto>())
                .FirstOrDefault(x => x.externalId == playerExternalId);
            if (p == null) return;

            long value = Math.Max(25_000, p.marketValue);

            _pending = Pending.Listing;
            _pendingPlayerExt = playerExternalId;
            _pendingOfferId = null;

            // An asking price is a shop window, not a floor: it may sit well above or below the valuation,
            // and offers under it are still allowed through.
            _view.ShowAmountPanel(new BidPanelVm
            {
                Id = playerExternalId.ToString(),
                Title = _loc.Tr("league.market.list_for", p.name),
                RoleGroup = RoleFormat.Group((PositionRole)p.role),
                RoleAbbr = RoleName(p.role),
                Subtitle = _loc.Tr("league.market.list_hint", MoneyFormat.Short(value)),
                Min = value / 2,
                Max = value * 4,
                Steps = BidSteps.For(value, Math.Max(25_000, value / 50)),
                ConfirmFormat = _loc.Tr("league.market.confirm_list"),
                MinLabel = _loc.Tr("auction.set_min"),
                MaxLabel = _loc.Tr("auction.set_max"),
                CancelLabel = _loc.Tr("league.market.cancel"),
                OverBudget = _loc.Tr("league.market.cannot_afford"),
            });
        }

        /// <summary>Signing a free agent is two taps, and the contract length comes FIRST because it is
        /// what the wage is for — a five-season deal at the same wage is a different commitment.</summary>
        private void OnSign(int playerExternalId)
        {
            var f = (_market?.freeAgents ?? new List<LeagueFreeAgentDto>())
                .FirstOrDefault(x => x.externalId == playerExternalId);
            if (f == null) return;

            _pending = Pending.Wage;
            _pendingPlayerExt = playerExternalId;
            _pendingOfferId = null;

            var values = new List<int>();
            var labels = new List<string>();
            for (int seasons = f.minSeasons; seasons <= f.maxSeasons; seasons++)
            {
                values.Add(seasons);
                labels.Add(_loc.Tr("league.market.seasons", seasons));
            }
            _view.ShowChoicePanel(
                _loc.Tr("league.market.contract_for", f.name), values, labels, _loc.Tr("league.market.cancel"));
        }

        private void OnChoice(int seasons)
        {
            var f = (_market?.freeAgents ?? new List<LeagueFreeAgentDto>())
                .FirstOrDefault(x => x.externalId == _pendingPlayerExt);
            if (f == null) { OnChoiceCancel(); return; }

            _pendingSeasons = seasons;
            _view.HideChoicePanel();

            long budget = _market?.yourBudget ?? 0;
            long maxByBudget = budget / WagePrepaidWeeks;
            long max = Math.Max(f.demandedWeeklyWage, Math.Min(maxByBudget, f.demandedWeeklyWage * 3));

            _view.ShowAmountPanel(new BidPanelVm
            {
                Id = f.externalId.ToString(),
                Title = _loc.Tr("league.market.wage_for", f.name, seasons),
                RoleGroup = RoleFormat.Group((PositionRole)f.role),
                RoleAbbr = RoleName(f.role),
                Subtitle = _loc.Tr("league.market.wage_hint",
                    MoneyFormat.Short(f.demandedWeeklyWage), MoneyFormat.Short(f.signingCostAtDemand)),
                Min = f.demandedWeeklyWage,
                Max = max,
                Steps = BidSteps.For(f.demandedWeeklyWage, Math.Max(1_000, f.demandedWeeklyWage / 20)),
                ConfirmFormat = _loc.Tr("league.market.confirm_wage"),
                MinLabel = _loc.Tr("auction.set_min"),
                MaxLabel = _loc.Tr("auction.set_max"),
                CancelLabel = _loc.Tr("league.market.cancel"),
                OverBudget = _loc.Tr("league.market.cannot_afford"),
            });
        }

        private void OnChoiceCancel()
        {
            _pending = Pending.None;
            _pendingPlayerExt = -1;
            _view.HideChoicePanel();
        }

        private void OnAmountCancel()
        {
            _pending = Pending.None;
            _pendingPlayerExt = -1;
            _pendingOfferId = null;
            _view.HideAmountPanel();
        }

        private void OnAmountConfirm(long amount) => ConfirmAsync(amount).Forget();

        private async UniTaskVoid ConfirmAsync(long amount)
        {
            if (_busy || amount <= 0 || _pending == Pending.None) return;
            Pending what = _pending;
            int playerExt = _pendingPlayerExt;
            string offerId = _pendingOfferId;
            int seasons = _pendingSeasons;

            _busy = true;
            _view.SetBusy(true);

            LeagueApiError error = LeagueApiError.None;
            LeagueMarketDto market = null;
            string message = null;
            bool ok;

            switch (what)
            {
                case Pending.Offer:
                {
                    var res = await _leagues.MakeOfferAsync(_selection.LeagueId, playerExt, amount);
                    ok = res.Success; error = res.Error; market = res.Value;
                    break;
                }
                case Pending.Counter:
                {
                    var res = await _leagues.RespondOfferAsync(
                        _selection.LeagueId, offerId, LeagueOfferAction.Counter, amount);
                    ok = res.Success; error = res.Error; market = res.Value;
                    break;
                }
                case Pending.Listing:
                {
                    var res = await _leagues.SetListingAsync(_selection.LeagueId, playerExt, true, amount);
                    ok = res.Success; error = res.Error; market = res.Value;
                    break;
                }
                default:
                {
                    var res = await _leagues.SignFreeAgentAsync(_selection.LeagueId, playerExt, amount, seasons);
                    ok = res.Success; error = res.Error;
                    if (res.Success && res.Value != null)
                    {
                        market = res.Value.market;
                        // A refusal is not an error: he simply says what he wants, so the screen repeats
                        // his terms and leaves the coach one tap from trying again.
                        message = res.Value.signed
                            ? _loc.Tr("league.market.signed", res.Value.playerName)
                            : _loc.Tr("league.market.refused", res.Value.playerName, res.Value.message);
                    }
                    break;
                }
            }

            _busy = false;
            _view.SetBusy(false);

            if (!ok)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(error)), isError: true);
                return;
            }

            _pending = Pending.None;
            _pendingPlayerExt = -1;
            _pendingOfferId = null;
            _view.HideAmountPanel();
            _view.ShowStatus(message ?? _loc.Tr("league.market.done"), isError: false);
            Adopt(market);
        }

        // --- answering -------------------------------------------------------------------------------

        private async UniTaskVoid RespondAsync(string offerId, LeagueOfferAction action, long amount)
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);

            var res = await _leagues.RespondOfferAsync(_selection.LeagueId, offerId, action, amount);

            _busy = false;
            _view.SetBusy(false);
            if (!res.Success)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(res.Error)), isError: true);
                return;
            }

            _view.ClearStatus();
            Adopt(res.Value);
        }

        private async UniTaskVoid SetListingAsync(int playerExternalId, bool listed, long asking)
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);

            var res = await _leagues.SetListingAsync(_selection.LeagueId, playerExternalId, listed, asking);

            _busy = false;
            _view.SetBusy(false);
            if (!res.Success)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(res.Error)), isError: true);
                return;
            }

            _view.ClearStatus();
            Adopt(res.Value);
        }

        // --- helpers ---------------------------------------------------------------------------------

        private IEnumerable<LeagueOfferDto> AllOffers()
        {
            if (_market == null) yield break;
            foreach (var o in _market.awaitingYou ?? new List<LeagueOfferDto>()) yield return o;
            foreach (var o in _market.incoming ?? new List<LeagueOfferDto>()) yield return o;
            foreach (var o in _market.outgoing ?? new List<LeagueOfferDto>()) yield return o;
        }

        private LeagueMarketPlayerDto FindPlayer(int externalId)
        {
            foreach (var c in _market?.clubs ?? new List<LeagueMarketSquadDto>())
            {
                var hit = (c.players ?? new List<LeagueMarketPlayerDto>())
                    .FirstOrDefault(p => p.externalId == externalId);
                if (hit != null) return hit;
            }
            return null;
        }

        private string RoleName(int role) =>
            _loc.Tr("role." + ((PositionRole)role).ToString().ToLowerInvariant());

        private void OnTab(int tab)
        {
            _tab = tab;
            if (tab != TabBuy) _browsedClubExt = -1;
            _view.ClearStatus();
            _view.HideAmountPanel();
            _view.HideChoicePanel();
            _pending = Pending.None;
            Render();
        }

        private void OnRefresh() => LoadAsync().Forget();

        private void OnBack() => _navigator.Pop();
    }
}
