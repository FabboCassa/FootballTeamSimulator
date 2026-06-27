using System.Collections.Generic;
using Fts.Services.Persistence;
using Sim.Core.Config;
using Sim.Core.Difficulty;
using Sim.Core.Domain;
using Sim.Core.Market;
using UnityEngine;

namespace Fts.Services
{
    /// <summary>
    /// Single-player transfer market (task 5.2b): drives the Sim.Core whole-world AI market
    /// (<see cref="TransferMarket.RunWindow"/>) at the two season windows — start of season and
    /// mid-season — seeding every club's transfer budget at the start window. The user's own
    /// club is excluded (his buying/selling is the 5.3 negotiation UI); the rest of the world
    /// trades AI↔AI. Deterministic per (world seed, window index): a window replays identically.
    ///
    /// Opt-in by being called (the match engine never references any of this), so golden
    /// masters/replays are unaffected; AI squads simply evolve across windows, like condition
    /// went live in 4.2. Lives in the Game scope. The single guard is
    /// <see cref="CareerState.TransferWindowsRun"/>, so a window never double-fires within a
    /// season and a reload mid-season resumes correctly.
    /// </summary>
    public sealed class LocalMarketService
    {
        /// <summary>Most pending AI offers we let pile up against a single listed player.</summary>
        private const int MaxOffersPerListing = 3;

        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly InboxService _inbox;
        private readonly BalanceConfig _config = new BalanceConfig();
        private readonly TransferBalance _t;
        private readonly TransferMarket _market;
        private readonly FinanceProgressor _finance;

        public LocalMarketService(CareerState career, ISaveRepository saveRepository, InboxService inbox)
        {
            _career = career;
            _saveRepository = saveRepository;
            _inbox = inbox;
            _t = _config.Transfer;
            _market = new TransferMarket(_config);
            _finance = new FinanceProgressor(_config);
        }

        /// <summary>The current transfer-window status (task 5.3) — drives whether the user may trade.</summary>
        public MarketWindow Window => MarketWindow.For(_career.Season, _config.Season);

        /// <summary>
        /// Runs any transfer window now due and saves if anything changed. Called at career open
        /// and at the top of each day advance; idempotent within a season via
        /// <see cref="CareerState.TransferWindowsRun"/>. The start window (index 0) seeds budgets
        /// first; the mid-season window (index 1) fires once the calendar reaches halfway.
        /// </summary>
        public bool RunDueWindows()
        {
            bool ran = false;

            if (_career.TransferWindowsRun <= 0)
            {
                // Start-of-season kitties now come from each club's FINANCES (task 5.5): a share of
                // cash reserves + a board grant, replacing the 5.2 strength-based seed. A club that
                // banked a profitable season gets a bigger budget; one that spent down gets less.
                _finance.SeedTransferBudgets(_career.Leagues); // overwrites; only here

                // Difficulty (task 5.7b): scale the freshly-seeded kitties — the user's club up on Easy,
                // the AI clubs up on Hard (more aggressive in the market). Applied BEFORE the window so
                // the AI trades on its difficulty budget and the user sees his scaled figure immediately.
                ScaleBudgetsByDifficulty();

                RunWindow(0);
                _career.TransferWindowsRun = 1;
                ran = true;
            }

            if (_career.TransferWindowsRun == 1 && _career.Season.CurrentDay >= MidSeasonDay())
            {
                RunWindow(1);
                _career.TransferWindowsRun = 2;
                ran = true;
            }

            if (ran)
                _saveRepository.Save(_career);

            return ran;
        }

        /// <summary>
        /// Scales every club's just-seeded transfer budget by difficulty (task 5.7b): the user's club
        /// by the user multiplier (generous on Easy), every AI club by the AI multiplier (aggressive on
        /// Hard), floored at the configured minimum. Normal = ×1000/1000 = no change. Composes with the
        /// finance-based seed (it scales whatever SeedTransferBudgets produced).
        /// </summary>
        private void ScaleBudgetsByDifficulty()
        {
            DifficultySettings s = DifficultyModel.Resolve(_career.Difficulty, _config);
            long minBudget = _t.MinBudget;

            foreach (League league in _career.Leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    int permille = club.Id == _career.UserClubId ? s.UserBudgetPermille : s.AiBudgetPermille;
                    long scaled = club.TransferBudget * permille / 1000;
                    if (scaled < minBudget) scaled = minBudget;
                    club.TransferBudget = scaled;
                }
            }
        }

        private void RunWindow(int windowIndex)
        {
            List<TransferRecord> records =
                _market.RunWindow(_career.Leagues, _career.Seed, windowIndex, _career.UserClubId);

            _career.TransferNews.AddRange(records);
            Debug.Log($"[Market] Season {_career.Season.Year} window {windowIndex}: {records.Count} AI transfers.");

            // Notify the user a window has resolved (task 6.2). The detail is in the Market News tab;
            // this is the headline that surfaces it in the Inbox + Hub badge.
            _inbox.Post(InboxCategory.Market, "inbox.market_window", records.Count.ToString());
        }

        /// <summary>The matchday at/after which the mid-season window opens (half the season length).</summary>
        private int MidSeasonDay()
        {
            int maxDay = 0;
            foreach (Fixture f in _career.Season.Fixtures)
                if (f.Day > maxDay)
                    maxDay = f.Day;

            return maxDay > 0 ? maxDay / 2 : 1;
        }

        // ── User-driven trading (task 5.3) ──────────────────────────────────────────────

        /// <summary>The cached market value of a player, falling back to a fresh valuation if unpriced.</summary>
        public long ValueOf(Player player, int clubId) =>
            player.MarketValue > 0 ? player.MarketValue : ValuationModel.Value(player, LevelOf(clubId), _config);

        /// <summary>The division level of the club holding a player (1 = top); used by the valuation fallback.</summary>
        public int LevelOf(int clubId)
        {
            foreach (League league in _career.Leagues)
                if (league.FindClub(clubId) != null)
                    return league.Division;
            return 1;
        }

        /// <summary>
        /// Executes a user PURCHASE agreed in the negotiation UI: moves the player into the
        /// user's squad, debits his budget and credits the seller, resets the contract, and logs
        /// the deal to the transfer news. Re-validates (window open, budget, the seller can still
        /// legally part with the player) so a stale UI can't force an illegal move; returns false
        /// if the deal can no longer go through.
        /// </summary>
        public bool ExecuteUserPurchase(Player player, Club seller, long fee)
        {
            if (!Window.IsOpen) return false;
            Club userClub = _career.GetUserClub();
            if (userClub == null || seller == null || player == null) return false;
            if (!seller.Squad.Players.Contains(player)) return false;
            if (fee < 0 || fee > userClub.TransferBudget) return false;

            SquadAnalysis sellerAnalysis = SquadAnalysis.Analyze(seller, _t);
            if (!sellerAnalysis.CanSell(player, _t)) return false;

            seller.Squad.Players.Remove(player);
            userClub.Squad.Players.Add(player);
            userClub.TransferBudget -= fee;
            seller.TransferBudget += fee;
            player.Contract.SeasonsRemaining = _t.SignedContractSeasons;

            RecordDeal(player, seller, userClub, fee);
            _inbox.Post(InboxCategory.Transfer, "inbox.transfer_in", player.FullName, seller.Name, Money.Short(fee));
            _saveRepository.Save(_career);
            return true;
        }

        /// <summary>
        /// Executes a user SALE agreed in the negotiation UI or by accepting an incoming offer:
        /// moves the player to the buyer, credits the user's budget and debits the buyer's,
        /// resets the contract, removes any listing/offers for the player, and logs the deal.
        /// Re-validates (window open, the user keeps a legal squad, the buyer can afford it).
        /// </summary>
        public bool ExecuteUserSale(Player player, Club buyer, long fee)
        {
            if (!Window.IsOpen) return false;
            Club userClub = _career.GetUserClub();
            if (userClub == null || buyer == null || player == null) return false;
            if (!userClub.Squad.Players.Contains(player)) return false;
            if (fee < 0 || fee > buyer.TransferBudget) return false;

            SquadAnalysis userAnalysis = SquadAnalysis.Analyze(userClub, _t);
            if (!userAnalysis.CanSell(player, _t)) return false;

            userClub.Squad.Players.Remove(player);
            buyer.Squad.Players.Add(player);
            userClub.TransferBudget += fee;
            buyer.TransferBudget -= fee;
            player.Contract.SeasonsRemaining = _t.SignedContractSeasons;

            RemoveListing(player.Id);
            RecordDeal(player, userClub, buyer, fee);
            _inbox.Post(InboxCategory.Transfer, "inbox.transfer_out", player.FullName, buyer.Name, Money.Short(fee));
            _saveRepository.Save(_career);
            return true;
        }

        /// <summary>The AI clubs that would want to buy <paramref name="player"/> and can afford his floor.</summary>
        public List<Club> InterestedBuyers(Player player)
        {
            var result = new List<Club>();
            if (player == null) return result;

            long value = ValueOf(player, _career.UserClubId);
            PlayerImportance importance = UserImportance(player);
            long minSale = NegotiationModel.MinSalePrice(value, importance, _t);
            int rating = PlayerRating.OverallFor(player, player.Role);

            foreach (League league in _career.Leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    if (club.Id == _career.UserClubId) continue;

                    SquadAnalysis sa = SquadAnalysis.Analyze(club, _t);
                    if (!RoleWanted(sa, player.Role, rating)) continue;

                    PersonalityProfile profile = ClubPersonalities.Profile(ClubPersonalities.For(club.Id, _career.Seed));
                    long buyerMax = NegotiationModel.BuyerMaxPrice(value, profile, club.TransferBudget, _t);
                    if (buyerMax < minSale) continue;

                    result.Add(club);
                }
            }

            return result;
        }

        /// <summary>
        /// Tops up incoming AI offers for every listed player (task 5.3). Runs only while a window
        /// is open; for each listing, interested clubs without a pending offer bid the asking price
        /// when they can afford it, otherwise their (lower) ceiling — up to a small cap so the
        /// inbox doesn't flood. Deterministic (no RNG); saves and returns true if anything changed.
        /// </summary>
        public bool GenerateListingOffers()
        {
            if (!Window.IsOpen) return false;

            bool changed = false;
            var stale = new List<int>();

            foreach (TransferListing listing in _career.TransferList)
            {
                Player player = _career.FindPlayer(listing.PlayerId);
                Club userClub = _career.GetUserClub();
                if (player == null || userClub == null || !userClub.Squad.Players.Contains(player))
                {
                    stale.Add(listing.PlayerId);
                    continue;
                }

                int existing = CountOffers(listing.PlayerId);
                if (existing >= MaxOffersPerListing) continue;

                long value = ValueOf(player, _career.UserClubId);
                PlayerImportance importance = UserImportance(player);
                long minSale = NegotiationModel.MinSalePrice(value, importance, _t);

                foreach (Club buyer in InterestedBuyers(player))
                {
                    if (existing >= MaxOffersPerListing) break;
                    if (HasOffer(listing.PlayerId, buyer.Id)) continue;

                    PersonalityProfile profile = ClubPersonalities.Profile(ClubPersonalities.For(buyer.Id, _career.Seed));
                    long buyerMax = NegotiationModel.BuyerMaxPrice(value, profile, buyer.TransferBudget, _t);
                    if (buyerMax < minSale) continue;

                    long fee = buyerMax >= listing.AskingPrice ? listing.AskingPrice : buyerMax;

                    _career.IncomingOffers.Add(new IncomingOffer
                    {
                        PlayerId = listing.PlayerId,
                        FromClubId = buyer.Id,
                        FromClubName = buyer.Name,
                        Fee = fee
                    });
                    // Surface the bid in the Inbox (task 6.2): the user acts on it in the Market Sell tab.
                    _inbox.Post(InboxCategory.Market, "inbox.offer", buyer.Name, Money.Short(fee), player.FullName);
                    existing++;
                    changed = true;
                }
            }

            foreach (int playerId in stale)
                RemoveListing(playerId);

            if (changed || stale.Count > 0)
                _saveRepository.Save(_career);

            return changed;
        }

        /// <summary>Importance of a user-club player to his own squad (drives the no-peanuts floor and suggested ask).</summary>
        public PlayerImportance UserImportance(Player player) =>
            SquadAnalysis.Analyze(_career.GetUserClub(), _t).ImportanceOf(player);

        /// <summary>True if the club wants this role and the player would be an upgrade on its current best there.</summary>
        private bool RoleWanted(SquadAnalysis analysis, PositionRole role, int rating)
        {
            foreach (RoleNeed need in analysis.Needs)
                if (need.Role == role && rating >= need.CurrentBest + _t.UpgradeMinPoints)
                    return true;
            return false;
        }

        private void RecordDeal(Player player, Club from, Club to, long fee)
        {
            _career.TransferNews.Add(new TransferRecord
            {
                PlayerId = player.Id,
                PlayerName = player.FullName,
                Role = player.Role,
                FromClubId = from.Id,
                FromClubName = from.Name,
                ToClubId = to.Id,
                ToClubName = to.Name,
                Fee = fee
            });
        }

        /// <summary>Drops a player's listing and every pending offer for him (after a sale or unlist).</summary>
        public void RemoveListing(int playerId)
        {
            _career.TransferList.RemoveAll(l => l.PlayerId == playerId);
            _career.IncomingOffers.RemoveAll(o => o.PlayerId == playerId);
        }

        private int CountOffers(int playerId)
        {
            int count = 0;
            foreach (IncomingOffer o in _career.IncomingOffers)
                if (o.PlayerId == playerId)
                    count++;
            return count;
        }

        private bool HasOffer(int playerId, int clubId)
        {
            foreach (IncomingOffer o in _career.IncomingOffers)
                if (o.PlayerId == playerId && o.FromClubId == clubId)
                    return true;
            return false;
        }
    }
}
