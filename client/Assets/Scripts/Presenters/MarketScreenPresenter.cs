using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
using Fts.Views;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;
using Sim.Core.Scouting;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Market screen (task 5.3): Buy / Sell / News over the single-player transfer market.
    /// Buy = browse every other club's players (filter by role, sort, shortlist) and open a
    /// negotiation. Sell = list a player (AI clubs bid into the inbox) or pick a club and
    /// negotiate directly. News = the season's completed transfers. All trading is gated to the
    /// two transfer windows (the user is restricted like the AI); the presenter computes legality
    /// (window open, affordability, squad depth) and the view just renders and emits clicks.
    /// </summary>
    public sealed class MarketScreenPresenter : IScreenPresenter
    {
        private const int MaxBuyRows = 60;
        private const int MaxNewsRows = 50;
        private const int RoleMax = 7; // PositionRole: goalkeeper(0)..striker(7)

        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly LocalMarketService _market;
        private readonly MarketTarget _target;
        private readonly ISaveRepository _saveRepository;
        private readonly ILocalizationService _loc;
        private readonly ScoutingService _scouting;
        private readonly OverlayHost _overlay;
        private readonly MarketView _view;
        private readonly TransferBalance _cfg = new BalanceConfig().Transfer;

        private int _tab;             // 0 buy, 1 sell, 2 news
        private int _roleFilter = -1; // -1 = all roles
        private int _sort;            // 0 overall, 1 value, 2 age
        private bool _shortlistOnly;
        private int _sellPicking = -1; // playerId whose buyer we're choosing, or -1

        public VisualElement View => _view.Root;

        public MarketScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            LocalMarketService market,
            MarketTarget target,
            ISaveRepository saveRepository,
            ILocalizationService loc,
            ScoutingService scouting,
            OverlayHost overlay)
        {
            _navigator = navigator;
            _career = career;
            _market = market;
            _target = target;
            _saveRepository = saveRepository;
            _loc = loc;
            _scouting = scouting;
            _overlay = overlay;
            _view = new MarketView(loc.Tr);
        }

        public void Enter()
        {
            _view.TabSelected += OnTab;
            _view.RoleFilterClicked += OnRoleFilter;
            _view.SortClicked += OnSort;
            _view.ShortlistOnlyClicked += OnShortlistOnly;
            _view.RowActionA += OnRowActionA;
            _view.RowActionB += OnRowActionB;
            _view.PickClicked += OnPick;
            _view.OfferAccept += OnOfferAccept;
            _view.OfferReject += OnOfferReject;
            _view.SubBackClicked += OnSubBack;
            _view.BackClicked += OnBack;

            _market.GenerateListingOffers();
            Refresh();
        }

        public void Exit()
        {
            _view.TabSelected -= OnTab;
            _view.RoleFilterClicked -= OnRoleFilter;
            _view.SortClicked -= OnSort;
            _view.ShortlistOnlyClicked -= OnShortlistOnly;
            _view.RowActionA -= OnRowActionA;
            _view.RowActionB -= OnRowActionB;
            _view.PickClicked -= OnPick;
            _view.OfferAccept -= OnOfferAccept;
            _view.OfferReject -= OnOfferReject;
            _view.SubBackClicked -= OnSubBack;
            _view.BackClicked -= OnBack;
        }

        public void Reveal()
        {
            _market.GenerateListingOffers();
            Refresh();
        }

        private void OnTab(int tab)
        {
            _tab = tab;
            _sellPicking = -1;
            Refresh();
        }

        private void OnRoleFilter()
        {
            _roleFilter = _roleFilter >= RoleMax ? -1 : _roleFilter + 1;
            Refresh();
        }

        private void OnSort()
        {
            _sort = (_sort + 1) % 3;
            Refresh();
        }

        private void OnShortlistOnly()
        {
            _shortlistOnly = !_shortlistOnly;
            Refresh();
        }

        private void OnRowActionA(int playerId)
        {
            if (_tab == 0) ToggleShortlist(playerId);
            else if (_tab == 1) ToggleListing(playerId);
            Refresh();
        }

        private void OnRowActionB(int playerId)
        {
            if (_tab == 0)
            {
                Club seller = ClubOfPlayer(playerId);
                if (seller == null || !_market.Window.IsOpen) return;
                _target.Mode = NegotiationMode.Buy;
                _target.PlayerId = playerId;
                _target.CounterpartyClubId = seller.Id;
                _navigator.Push<NegotiationScreenPresenter>();
            }
            else if (_tab == 1)
            {
                if (!_market.Window.IsOpen) return;
                _sellPicking = playerId;
                Refresh();
            }
        }

        private void OnPick(int clubId)
        {
            if (_sellPicking < 0 || !_market.Window.IsOpen) return;
            _target.Mode = NegotiationMode.Sell;
            _target.PlayerId = _sellPicking;
            _target.CounterpartyClubId = clubId;
            _sellPicking = -1;
            _navigator.Push<NegotiationScreenPresenter>();
        }

        private void OnOfferAccept(int index)
        {
            if (index < 0 || index >= _career.IncomingOffers.Count) return;
            IncomingOffer offer = _career.IncomingOffers[index];
            Player player = _career.FindPlayer(offer.PlayerId);
            Club buyer = _career.FindClub(offer.FromClubId);
            if (player == null || buyer == null) return;

            // Confirm before the one-tap sale (task 6.2) — it moves a player and changes the budget.
            Dialogs.Confirm(_overlay, _loc,
                "dialog.accept_offer.title", "dialog.accept_offer.message", "dialog.accept_offer.confirm",
                () =>
                {
                    if (_market.ExecuteUserSale(player, buyer, offer.Fee))
                        Dialogs.Toast(_overlay, _loc, "market.toast.sold", player.FullName);
                    Refresh();
                },
                player.FullName, buyer.Name, MoneyFormat.Short(offer.Fee));
        }

        private void OnOfferReject(int index)
        {
            if (index < 0 || index >= _career.IncomingOffers.Count) return;
            _career.IncomingOffers.RemoveAt(index);
            _saveRepository.Save(_career);
            Refresh();
        }

        private void OnSubBack()
        {
            _sellPicking = -1;
            Refresh();
        }

        private void OnBack() => _navigator.Pop();

        // ── Rendering ───────────────────────────────────────────────────────────────────

        private void Refresh()
        {
            MarketWindow window = _market.Window;

            _view.SetBudget(_loc.Tr("market.budget", MoneyFormat.Short(_career.GetUserClub().TransferBudget)));
            string windowLine = window.IsOpen
                ? _loc.Tr("market.window.open", window.DaysUntilClose)
                : window.DaysUntilOpen > 0
                    ? _loc.Tr("market.window.closed_soon", window.DaysUntilOpen)
                    : _loc.Tr("market.window.closed");
            _view.SetWindow(windowLine, window.IsOpen);

            _view.SetActiveTab(_tab);
            _view.SetFilterBar(_tab == 0, RoleFilterText(), SortText(), ShortlistOnlyText());

            _view.BeginContent();
            switch (_tab)
            {
                case 0: RenderBuy(window); break;
                case 1: RenderSell(window); break;
                default: RenderNews(); break;
            }
        }

        private void RenderBuy(MarketWindow window)
        {
            var analyses = new Dictionary<int, SquadAnalysis>();
            var rows = new List<Player>();
            foreach (League league in _career.Leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    if (club.Id == _career.UserClubId) continue;
                    foreach (Player p in club.Squad.Players)
                    {
                        if (_roleFilter >= 0 && (int)p.Role != _roleFilter) continue;
                        if (_shortlistOnly && !_career.Shortlist.Contains(p.Id)) continue;
                        rows.Add(p);
                    }
                }
            }

            rows.Sort(CompareBuy);

            if (rows.Count == 0)
            {
                _view.AddInfoLine(_loc.Tr("market.buy_empty"));
                return;
            }

            int shown = 0;
            foreach (Player p in rows)
            {
                if (shown >= MaxBuyRows) break;
                Club club = ClubOfPlayer(p.Id);
                if (club == null) continue;

                if (!analyses.TryGetValue(club.Id, out SquadAnalysis sa))
                {
                    sa = SquadAnalysis.Analyze(club, _cfg);
                    analyses[club.Id] = sa;
                }

                bool shortlisted = _career.Shortlist.Contains(p.Id);
                _view.AddPlayerRow(new MarketRowVm
                {
                    PlayerId = p.Id,
                    Primary = _loc.Tr("market.buy_row", p.FullName, club.Name),
                    Sub = ScoutedSub(p),
                    Value = MoneyFormat.Short(_market.ValueOf(p, club.Id)),
                    Tag = shortlisted ? _loc.Tr("market.tag.shortlisted") : string.Empty,
                    ActionAText = shortlisted ? "★" : "☆",
                    ActionAHighlighted = shortlisted,
                    ActionBText = _loc.Tr("market.buy"),
                    ActionBEnabled = window.IsOpen && sa.CanSell(p, _cfg)
                });
                shown++;
            }

            if (rows.Count > shown)
                _view.AddInfoLine(_loc.Tr("market.more_results", rows.Count - shown));
        }

        private void RenderSell(MarketWindow window)
        {
            if (_sellPicking >= 0)
            {
                RenderSellPicker(window);
                return;
            }

            Club userClub = _career.GetUserClub();
            SquadAnalysis analysis = SquadAnalysis.Analyze(userClub, _cfg);

            _view.AddSectionLabel(_loc.Tr("market.your_squad"));
            var squad = new List<Player>(userClub.Squad.Players);
            squad.Sort((a, b) => PlayerRating.Overall(b).CompareTo(PlayerRating.Overall(a)));

            foreach (Player p in squad)
            {
                bool listed = IsListed(p.Id, out long ask);
                bool canSell = analysis.CanSell(p, _cfg);
                _view.AddPlayerRow(new MarketRowVm
                {
                    PlayerId = p.Id,
                    Primary = p.FullName,
                    Sub = _loc.Tr("market.player_sub", RoleName(p.Role), p.Age, PlayerRating.Overall(p)),
                    Value = MoneyFormat.Short(_market.ValueOf(p, userClub.Id)),
                    Tag = listed ? _loc.Tr("market.tag.listed", MoneyFormat.Short(ask)) : string.Empty,
                    ActionAText = listed ? _loc.Tr("market.unlist") : _loc.Tr("market.list"),
                    ActionAHighlighted = listed,
                    ActionBText = _loc.Tr("market.sell_to"),
                    ActionBEnabled = window.IsOpen && canSell
                });
            }

            _view.AddSectionLabel(_loc.Tr("market.incoming_offers"));
            if (_career.IncomingOffers.Count == 0)
            {
                _view.AddInfoLine(_loc.Tr("market.no_offers"));
                return;
            }

            for (int i = 0; i < _career.IncomingOffers.Count; i++)
            {
                IncomingOffer offer = _career.IncomingOffers[i];
                Player player = _career.FindPlayer(offer.PlayerId);
                string name = player?.FullName ?? _loc.Tr("market.unknown_player");
                _view.AddOfferRow(new MarketOfferVm
                {
                    Index = i,
                    Label = _loc.Tr("market.offer_row", name, offer.FromClubName),
                    Fee = MoneyFormat.Short(offer.Fee),
                    ActionsEnabled = window.IsOpen
                });
            }
        }

        private void RenderSellPicker(MarketWindow window)
        {
            Player player = _career.FindPlayer(_sellPicking);
            if (player == null)
            {
                _sellPicking = -1;
                RenderSell(window);
                return;
            }

            _view.AddSectionLabel(_loc.Tr("market.sell_to_header", player.FullName));

            List<Club> buyers = _market.InterestedBuyers(player);
            if (buyers.Count == 0)
            {
                _view.AddInfoLine(_loc.Tr("market.no_buyers"));
            }
            else
            {
                buyers.Sort((a, b) => b.TransferBudget.CompareTo(a.TransferBudget));
                foreach (Club club in buyers)
                {
                    _view.AddPickRow(new MarketPickVm
                    {
                        Id = club.Id,
                        Label = club.Name,
                        Detail = _loc.Tr("market.club_budget", MoneyFormat.Short(club.TransferBudget))
                    });
                }
            }

            _view.AddBackRow(_loc.Tr("market.back_to_squad"));
        }

        private void RenderNews()
        {
            if (_career.TransferNews.Count == 0)
            {
                _view.AddInfoLine(_loc.Tr("market.no_news"));
                return;
            }

            int count = 0;
            for (int i = _career.TransferNews.Count - 1; i >= 0 && count < MaxNewsRows; i--, count++)
            {
                TransferRecord r = _career.TransferNews[i];
                _view.AddInfoLine(_loc.Tr("market.news_line", r.PlayerName, r.FromClubName, r.ToClubName, MoneyFormat.Short(r.Fee)));
            }
        }

        // ── Actions ─────────────────────────────────────────────────────────────────────

        private void ToggleShortlist(int playerId)
        {
            if (_career.Shortlist.Contains(playerId))
                _career.Shortlist.Remove(playerId);
            else
                _career.Shortlist.Add(playerId);
            _saveRepository.Save(_career);
        }

        private void ToggleListing(int playerId)
        {
            if (IsListed(playerId, out _))
            {
                _market.RemoveListing(playerId);
            }
            else
            {
                Player p = FindUserPlayer(playerId);
                if (p == null) return;
                Club userClub = _career.GetUserClub();
                long value = _market.ValueOf(p, userClub.Id);
                PlayerImportance imp = _market.UserImportance(p);
                PersonalityProfile profile = ClubPersonalities.Profile(ClubPersonalities.For(userClub.Id, _career.Seed));
                long ask = NegotiationModel.AskingPrice(value, imp, profile, _cfg);
                _career.TransferList.Add(new TransferListing { PlayerId = playerId, AskingPrice = ask });
            }

            _saveRepository.Save(_career);
            _market.GenerateListingOffers();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────

        private int CompareBuy(Player a, Player b)
        {
            switch (_sort)
            {
                case 1:
                    // The world is re-priced, so MarketValue is populated; sort by it directly
                    // (avoids an O(N) club lookup per comparison).
                    int byValue = b.MarketValue.CompareTo(a.MarketValue);
                    return byValue != 0 ? byValue : a.Id.CompareTo(b.Id);
                case 2:
                    int byAge = a.Age.CompareTo(b.Age);
                    return byAge != 0 ? byAge : a.Id.CompareTo(b.Id);
                default:
                    int byOverall = PlayerRating.Overall(b).CompareTo(PlayerRating.Overall(a));
                    return byOverall != 0 ? byOverall : a.Id.CompareTo(b.Id);
            }
        }

        private bool IsListed(int playerId, out long ask)
        {
            foreach (TransferListing l in _career.TransferList)
            {
                if (l.PlayerId == playerId)
                {
                    ask = l.AskingPrice;
                    return true;
                }
            }
            ask = 0;
            return false;
        }

        private Club ClubOfPlayer(int playerId)
        {
            foreach (League league in _career.Leagues)
                foreach (Club club in league.Clubs)
                    foreach (Player p in club.Squad.Players)
                        if (p.Id == playerId)
                            return club;
            return null;
        }

        private Player FindUserPlayer(int playerId)
        {
            foreach (Player p in _career.GetUserClub().Squad.Players)
                if (p.Id == playerId)
                    return p;
            return null;
        }

        /// <summary>
        /// The Buy-row sub line: role · age · scouted overall. The overall is a range that narrows
        /// with scouting knowledge (task 5.4b), shown as the exact number only once fully scouted —
        /// so the user buys on what his scouts actually know.
        /// </summary>
        private string ScoutedSub(Player p)
        {
            int knowledge = _scouting.KnowledgeOf(p.Id);
            if (knowledge >= _scouting.MaxKnowledge)
                return _loc.Tr("market.player_sub", RoleName(p.Role), p.Age, PlayerRating.Overall(p));

            ScoutedRange ovr = _scouting.Report(p).Overall;
            return _loc.Tr("market.player_sub_range", RoleName(p.Role), p.Age, ovr.Min, ovr.Max);
        }

        private string RoleFilterText() =>
            _roleFilter < 0 ? _loc.Tr("market.filter.all_roles") : RoleName((PositionRole)_roleFilter);

        private string SortText() =>
            _loc.Tr(_sort == 1 ? "market.sort.value" : _sort == 2 ? "market.sort.age" : "market.sort.overall");

        private string ShortlistOnlyText() =>
            _loc.Tr(_shortlistOnly ? "market.shortlist.on" : "market.shortlist.off");

        private string RoleName(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());
    }
}
