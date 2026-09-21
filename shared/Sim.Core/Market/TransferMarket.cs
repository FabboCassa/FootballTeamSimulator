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
    /// his players without consent. Three guards keep deals sensible — a buyer never bids above its
    /// budget or its willingness ceiling, a seller (via <see cref="NegotiationModel.MinSalePrice"/>)
    /// never sells a best-XI player for peanuts, and a lower-division buyer never signs above its own
    /// nation's tier-1 90th-percentile player value (task: realistic transfer budgets, R9) — a HARD
    /// cap in this buying path, not merely a consequence of tier-2 budgets normally staying small:
    /// a cash-rich lower-division club's budget CAN exceed that p90 value (accumulated cash + a
    /// generous board grant), and without this guard it would simply buy whatever it could afford.
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
        /// Runs one transfer window over the WHOLE world — playable, background AND data-only leagues
        /// alike (task: worldwide AI market, R10), not just the leagues the host simulates in full
        /// detail. A data-only club has no fixtures or table, but it still holds real players and a
        /// transfer budget, so it belongs in the same market as everyone else; only the human's own
        /// club (still resolved from the WHOLE world, not just his playable pyramid) is excluded.
        /// </summary>
        public List<TransferRecord> RunWindow(World world, ulong worldSeed, int windowIndex, int humanClubId = -1)
            => RunWindow(world.AllLeagues(), worldSeed, windowIndex, humanClubId);

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
            var nationByClub = new Dictionary<int, string>();
            var econRepByClub = new Dictionary<int, int>();
            var buyerIds = new List<int>();
            var orderedClubIds = new List<int>(); // explicit, sorted seller scan order (platform-independent)
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    clubById[club.Id] = club;
                    levelByClub[club.Id] = league.Division;
                    nationByClub[club.Id] = league.NationCode;
                    econRepByClub[club.Id] = league.EconomicReputation;
                    orderedClubIds.Add(club.Id);
                    if (club.Id != humanClubId) buyerIds.Add(club.Id);
                }
            }
            orderedClubIds.Sort();

            // R9 signing cap: each nation's tier-1 90th-percentile player value, computed fresh
            // (post-reprice) every window — a fixed constant would drift from what "tier-1" means as
            // the world ages. Nations with no division-1 league in this world get no cap (nothing to
            // measure against).
            Dictionary<string, long> tier1P90ByNation = Tier1P90ByNation(leagues);

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
                                                      clubById, orderedClubIds, levelByClub, nationByClub, econRepByClub, tier1P90ByNation,
                                                      humanClubId, Analyze, analysisCache);
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
            Dictionary<int, Club> clubById, List<int> orderedClubIds, Dictionary<int, int> levelByClub,
            Dictionary<int, string> nationByClub, Dictionary<int, int> econRepByClub, Dictionary<string, long> tier1P90ByNation, int humanClubId,
            System.Func<int, SquadAnalysis> analyze, Dictionary<int, SquadAnalysis> analysisCache)
        {
            SquadAnalysis buyerAnalysis = analyze(buyerId);
            bool youthFocused = buyerProfile.YouthBiasPermille > 1000;

            // R9: a lower-division buyer may never even consider a player above its nation's tier-1
            // p90 value — nation with no measured tier-1 (no cap entry) leaves buyers unrestricted.
            long? buyerCap = levelByClub[buyerId] != 1 && tier1P90ByNation.TryGetValue(nationByClub[buyerId], out long cap)
                ? cap
                : (long?)null;

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

                    // CanSell's squad-size floor (MinSquadSize) is a per-CLUB fact, true or false for
                    // every one of its players alike — a data-only/background club below the floor
                    // (the common case at world scale, by design: a 7-player data-only squad must
                    // never be drained) can NEVER be a seller. Reject the whole club before touching a
                    // single player, instead of paying a full rating computation per player only to
                    // have CanSell veto every one of them the same way. Exactly the same candidates
                    // survive; this only skips work that was always going to be thrown away.
                    if (seller.Squad.Players.Count <= _t.MinSquadSize) continue;
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

                        if (buyerCap.HasValue && value > buyerCap.Value) continue; // R9 hard cap

                        // R11: prestige refusal — player refuses a buying club whose prestige is below his threshold
                        if (PrestigeModel.IsRefused(player, imp, seller, levelByClub[sellerId], econRepByClub[sellerId],
                                                    buyer, levelByClub[buyerId], econRepByClub[buyerId], _cfg))
                            continue;

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
                        c.Player, c.Importance, c.Seller, levelByClub[c.Seller.Id], econRepByClub[c.Seller.Id],
                        buyer, levelByClub[buyerId], econRepByClub[buyerId],
                        c.Value, sellerProfile, buyerProfile, buyer.TransferBudget, _cfg);

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

        /// <summary>
        /// The 90th-percentile player market value of every division-1 league, grouped by
        /// <see cref="League.NationCode"/> (task: realistic transfer budgets, R9) — the reference
        /// ceiling <see cref="TrySignOne"/> enforces against lower-division buyers. Assumes
        /// <see cref="RunWindow"/> already repriced the world, so <see cref="Player.MarketValue"/>
        /// reflects this window.
        /// </summary>
        private static Dictionary<string, long> Tier1P90ByNation(IReadOnlyList<League> leagues)
        {
            var valuesByNation = new Dictionary<string, List<long>>();
            foreach (League league in leagues)
            {
                if (league.Division != 1) continue;
                if (!valuesByNation.TryGetValue(league.NationCode, out List<long> values))
                {
                    values = new List<long>();
                    valuesByNation[league.NationCode] = values;
                }
                foreach (Club club in league.Clubs)
                    foreach (Player player in club.Squad.Players)
                        values.Add(player.MarketValue);
            }

            var result = new Dictionary<string, long>();
            foreach (KeyValuePair<string, List<long>> entry in valuesByNation)
            {
                List<long> sorted = entry.Value;
                if (sorted.Count == 0) continue;
                sorted.Sort();
                result[entry.Key] = sorted[(sorted.Count - 1) * 90 / 100];
            }
            return result;
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
