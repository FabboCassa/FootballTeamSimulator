using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Market
{
    /// <summary>
    /// The AI transfer market for one window (task 5.2, ARCHITECTURE.md §4.7). Runs across the
    /// WHOLE world (every league/division): each AI club analyses its squad needs and buys
    /// upgrades it can afford, negotiating offer/counteroffer with the selling club. AI↔AI
    /// transfers move players and money between clubs and update <see cref="Club.TransferBudget"/>.
    ///
    /// Determinism: the only RNG is a per-window seeded <see cref="Pcg32"/> used to order buyers
    /// and break ties, so a window replays byte-identically for a given (worldSeed, windowIndex).
    /// Everything else — valuation, squad need, negotiation — is the pure integer math of the
    /// 5.1/5.2 models. The match engine never touches any of this, so golden masters/replays are
    /// unaffected (opt-in by being called).
    ///
    /// The human's club (<paramref name="humanClubId"/>) is excluded as both buyer and seller: the
    /// user drives his own transfers through the negotiation UI (task 5.3), so the AI never trades
    /// his players without consent. Two guards keep deals sensible — a buyer never bids above its
    /// budget or its willingness ceiling, and a seller (via <see cref="NegotiationModel.MinSalePrice"/>)
    /// never sells a best-XI player for peanuts.
    /// </summary>
    public sealed class TransferMarket
    {
        /// <summary>Odd constant decorrelating per-window market seeds from other per-world streams.</summary>
        private const ulong WindowSeedMix = 0x9E3779B97F4A7C15UL;

        private readonly BalanceConfig _cfg;
        private readonly TransferBalance _t;

        public TransferMarket(BalanceConfig cfg)
        {
            _cfg = cfg;
            _t = cfg.Transfer;
        }

        /// <summary>
        /// Runs one transfer window over every league and returns the completed transfers, in the
        /// order they were agreed. Re-prices the world first so negotiations use fresh values; does
        /// NOT re-seed budgets (a window spends from the budgets the host seeded at season start).
        /// </summary>
        public List<TransferRecord> RunWindow(IReadOnlyList<League> leagues, ulong worldSeed, int windowIndex, int humanClubId = -1)
        {
            // Fresh prices for the window (deterministic, no RNG).
            new ValuationProgressor(_cfg).Reprice(leagues);

            var clubById = new Dictionary<int, Club>();
            var levelByClub = new Dictionary<int, int>();
            var buyerIds = new List<int>();
            var orderedClubIds = new List<int>(); // explicit, sorted seller scan order (platform-independent)
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    clubById[club.Id] = club;
                    levelByClub[club.Id] = league.Division;
                    orderedClubIds.Add(club.Id);
                    if (club.Id != humanClubId) buyerIds.Add(club.Id);
                }
            }
            orderedClubIds.Sort();

            // Deterministic buyer order (Fisher-Yates with the window RNG).
            var rng = new Pcg32(worldSeed ^ (WindowSeedMix * (ulong)(uint)(windowIndex + 1)), 4242UL);
            for (int i = buyerIds.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (buyerIds[i], buyerIds[j]) = (buyerIds[j], buyerIds[i]);
            }

            var analysisCache = new Dictionary<int, SquadAnalysis>();
            SquadAnalysis Analyze(int clubId)
            {
                if (!analysisCache.TryGetValue(clubId, out SquadAnalysis a))
                {
                    a = SquadAnalysis.Analyze(clubById[clubId], _t);
                    analysisCache[clubId] = a;
                }
                return a;
            }

            var records = new List<TransferRecord>();

            foreach (int buyerId in buyerIds)
            {
                if (records.Count >= _t.MaxTransfersPerWindow) break;

                Club buyer = clubById[buyerId];
                PersonalityProfile buyerProfile = ClubPersonalities.Profile(ClubPersonalities.For(buyerId, worldSeed));

                int signings = 0;
                while (signings < _t.MaxSigningsPerClubPerWindow && records.Count < _t.MaxTransfersPerWindow)
                {
                    TransferRecord? deal = TrySignOne(buyer, buyerId, buyerProfile, worldSeed,
                                                      clubById, orderedClubIds, levelByClub, humanClubId, Analyze, analysisCache);
                    if (deal == null) break;
                    records.Add(deal);
                    signings++;
                }
            }

            return records;
        }

        /// <summary>Attempts a single signing for the buyer across its needs; returns the deal or null.</summary>
        private TransferRecord? TrySignOne(
            Club buyer, int buyerId, PersonalityProfile buyerProfile, ulong worldSeed,
            Dictionary<int, Club> clubById, List<int> orderedClubIds, Dictionary<int, int> levelByClub, int humanClubId,
            System.Func<int, SquadAnalysis> analyze, Dictionary<int, SquadAnalysis> analysisCache)
        {
            SquadAnalysis buyerAnalysis = analyze(buyerId);
            bool youthFocused = buyerProfile.YouthBiasPermille > 1000;

            foreach (RoleNeed need in buyerAnalysis.Needs)
            {
                int bar = need.CurrentBest + _t.UpgradeMinPoints;

                // Collect EVERY affordable upgrade for this role across all (non-human) sellers, then
                // try them best-first until one deal actually closes — a club doesn't abandon a need just
                // because its #1 target's club won't sell (e.g. a Hoarder); it moves down the list.
                var candidates = new List<Candidate>();
                foreach (int sellerId in orderedClubIds)
                {
                    if (sellerId == buyerId || sellerId == humanClubId) continue;
                    Club seller = clubById[sellerId];
                    SquadAnalysis sa = analyze(sellerId);

                    foreach (Player player in seller.Squad.Players)
                    {
                        int rating = PlayerRating.OverallFor(player, need.Role);
                        if (rating < bar) continue;
                        if (!sa.CanSell(player, _t)) continue;

                        PlayerImportance imp = sa.ImportanceOf(player);
                        long value = player.MarketValue > 0
                            ? player.MarketValue
                            : ValuationModel.Value(player, levelByClub[sellerId], _cfg);

                        long minSale = NegotiationModel.MinSalePrice(value, imp, _t);
                        long buyerMax = NegotiationModel.BuyerMaxPrice(value, buyerProfile, buyer.TransferBudget, _t);
                        if (buyerMax < minSale) continue; // can't reach the floor — skip

                        candidates.Add(new Candidate(player, seller, value, imp, rating, player.Age));
                    }
                }

                if (candidates.Count == 0) continue;

                candidates.Sort((a, b) => IsBetterTarget(
                    a.Rating, a.Age, a.Value, a.Player.Id,
                    b.Rating, b.Age, b.Value, b.Player.Id, youthFocused) ? -1 : 1);

                foreach (Candidate c in candidates)
                {
                    PersonalityProfile sellerProfile =
                        ClubPersonalities.Profile(ClubPersonalities.For(c.Seller.Id, worldSeed));
                    NegotiationResult result = NegotiationModel.AutoNegotiate(
                        c.Value, c.Importance, sellerProfile, buyerProfile, buyer.TransferBudget, _t);

                    if (!result.Agreed || result.Fee > buyer.TransferBudget) continue;

                    // Execute the transfer.
                    c.Seller.Squad.Players.Remove(c.Player);
                    buyer.Squad.Players.Add(c.Player);
                    buyer.TransferBudget -= result.Fee;
                    c.Seller.TransferBudget += result.Fee;
                    c.Player.Contract.SeasonsRemaining = _t.SignedContractSeasons;

                    analysisCache.Remove(buyerId);
                    analysisCache.Remove(c.Seller.Id);

                    return new TransferRecord
                    {
                        PlayerId = c.Player.Id,
                        PlayerName = c.Player.FullName,
                        Role = c.Player.Role,
                        FromClubId = c.Seller.Id,
                        FromClubName = c.Seller.Name,
                        ToClubId = buyer.Id,
                        ToClubName = buyer.Name,
                        Fee = result.Fee
                    };
                }
            }

            return null;
        }

        /// <summary>A scored buy target for a need, scanned before any negotiation.</summary>
        private readonly struct Candidate
        {
            public readonly Player Player;
            public readonly Club Seller;
            public readonly long Value;
            public readonly PlayerImportance Importance;
            public readonly int Rating;
            public readonly int Age;

            public Candidate(Player player, Club seller, long value, PlayerImportance importance, int rating, int age)
            {
                Player = player;
                Seller = seller;
                Value = value;
                Importance = importance;
                Rating = rating;
                Age = age;
            }
        }

        /// <summary>
        /// Target ranking: higher role rating first; for youth-focused buyers, younger next; then
        /// cheaper; then lower id (stable). Pure integer comparisons → deterministic everywhere.
        /// </summary>
        private static bool IsBetterTarget(
            int rating, int age, long value, int id,
            int bestRating, int bestAge, long bestValue, int bestId, bool youthFocused)
        {
            if (rating != bestRating) return rating > bestRating;
            if (youthFocused && age != bestAge) return age < bestAge;
            if (value != bestValue) return value < bestValue;
            return id < bestId;
        }
    }
}
