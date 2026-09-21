using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Acceptance tests for task: prestige-based transfer refusal (R11 of realistic-club-economy spec):
    /// - Prestige = f(league wealth, club stature), pure function;
    /// - Top player of a tier-1 top club refuses a tier-2 buyer that has the budget;
    /// - Same buyer can sign a bench player from that club;
    /// - Players aged &lt;= 21 or &gt;= 32 and non-best-XI players have lower thresholds;
    /// - NegotiationModel returns a Refused outcome with a reason code; AI transfers never violate it.
    /// </summary>
    [TestFixture]
    public class PrestigeTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static TransferBalance T => Cfg.Transfer;
        private const ulong WorldSeed = 20260921_091UL;

        // ----------------------------------------------------------------- Acceptance 1: Pure function
        [Test]
        public void Prestige_IsPureFunctionOfLeagueWealthAndStature()
        {
            // Monotone in league wealth (stature held constant)
            int p1 = PrestigeModel.ClubPrestige(leagueWealthMultiplierPermille: 1000, clubStature: 50, T);
            int p2 = PrestigeModel.ClubPrestige(leagueWealthMultiplierPermille: 500, clubStature: 50, T);
            int p3 = PrestigeModel.ClubPrestige(leagueWealthMultiplierPermille: 175, clubStature: 50, T);

            Assert.Multiple(() =>
            {
                Assert.That(p1, Is.GreaterThan(p2), "wealth 1000 must exceed wealth 500");
                Assert.That(p2, Is.GreaterThan(p3), "wealth 500 must exceed wealth 175");
            });

            // Monotone in stature (league wealth held constant)
            int s1 = PrestigeModel.ClubPrestige(leagueWealthMultiplierPermille: 500, clubStature: 95, T);
            int s2 = PrestigeModel.ClubPrestige(leagueWealthMultiplierPermille: 500, clubStature: 50, T);
            int s3 = PrestigeModel.ClubPrestige(leagueWealthMultiplierPermille: 500, clubStature: 10, T);

            Assert.Multiple(() =>
            {
                Assert.That(s1, Is.GreaterThan(s2), "stature 95 must exceed stature 50");
                Assert.That(s2, Is.GreaterThan(s3), "stature 50 must exceed stature 10");
            });

            // Deterministic: repeated calls yield identical values
            Assert.That(PrestigeModel.ClubPrestige(500, 50, T), Is.EqualTo(p2));

            // Clamped into [0, 1000]
            Assert.That(p1, Is.InRange(0, 1000));
            Assert.That(p3, Is.InRange(0, 1000));
        }

        // ----------------------------------------------------------------- Acceptance 2: Top player refuses tier-2 buyer
        [Test]
        public void TopPlayer_OfTierOneTopClub_RefusesTierTwoBuyerWithBudget()
        {
            var topClub = new Club { Id = 1, Name = "Top Tier 1 FC", Stature = 95 };
            var tier2Buyer = new Club { Id = 2, Name = "Rich Tier 2 FC", Stature = 40, TransferBudget = 100_000_000L };

            // Top player: prime age 26, regular starter, high overall
            var topPlayer = new Player
            {
                Id = 10,
                Age = 26,
                Development = new PlayerDevelopment { Potential = 85 },
                Contract = new Contract { SeasonsRemaining = 3 }
            };
            topPlayer.Attributes.Pace = 82;
            topPlayer.Attributes.Shooting = 84;
            topPlayer.Attributes.Technique = 82;

            int sellerWealth = 500; // Italy tier 1
            int buyerWealth = 175;  // Italy tier 2
            int sellerPrestige = PrestigeModel.ClubPrestige(sellerWealth, topClub.Stature, T);
            int buyerPrestige = PrestigeModel.ClubPrestige(buyerWealth, tier2Buyer.Stature, T);

            Assert.That(sellerPrestige, Is.GreaterThan(buyerPrestige), "top club prestige must exceed tier 2 buyer");

            RefusalReason refusal = PrestigeModel.EvaluateRefusal(
                topPlayer, PlayerImportance.Starter,
                sellerPrestige, buyerPrestige, T);

            Assert.That(refusal, Is.EqualTo(RefusalReason.PrestigeTooLow),
                "a top player at a top tier-1 club must refuse a tier-2 buyer");

            var sellerProfile = ClubPersonalities.Profile(ClubPersonalities.For(topClub.Id, WorldSeed));
            var buyerProfile = ClubPersonalities.Profile(ClubPersonalities.For(tier2Buyer.Id, WorldSeed));

            NegotiationResult result = NegotiationModel.AutoNegotiate(
                topPlayer, PlayerImportance.Starter,
                topClub, sellerLeagueLevel: 1, sellerEconomicReputation: 82,
                tier2Buyer, buyerLeagueLevel: 2, buyerEconomicReputation: 82,
                value: 30_000_000L, sellerProfile: sellerProfile, buyerProfile: buyerProfile, buyerBudget: tier2Buyer.TransferBudget, cfg: Cfg);

            Assert.Multiple(() =>
            {
                Assert.That(result.Agreed, Is.False, "negotiation cannot agree when player refuses");
                Assert.That(result.Outcome, Is.EqualTo(NegotiationOutcome.Refused));
                Assert.That(result.RefusalReason, Is.EqualTo(RefusalReason.PrestigeTooLow));
            });
        }

        // ----------------------------------------------------------------- Acceptance 3: Same buyer can sign bench player
        [Test]
        public void SameBuyer_CanSignBenchPlayer_FromSameClub()
        {
            var topClub = new Club { Id = 1, Name = "Top Tier 1 FC", Stature = 95 };
            var tier2Buyer = new Club { Id = 2, Name = "Rich Tier 2 FC", Stature = 40, TransferBudget = 100_000_000L };

            // Bench player: age 26, same rating range, but squad importance (not best XI)
            var benchPlayer = new Player
            {
                Id = 11,
                Age = 26,
                Development = new PlayerDevelopment { Potential = 75 },
                Contract = new Contract { SeasonsRemaining = 3 }
            };
            benchPlayer.Attributes.Pace = 72;
            benchPlayer.Attributes.Shooting = 70;

            int sellerWealth = 500;
            int buyerWealth = 175;
            int sellerPrestige = PrestigeModel.ClubPrestige(sellerWealth, topClub.Stature, T);
            int buyerPrestige = PrestigeModel.ClubPrestige(buyerWealth, tier2Buyer.Stature, T);

            RefusalReason benchRefusal = PrestigeModel.EvaluateRefusal(
                benchPlayer, PlayerImportance.Squad,
                sellerPrestige, buyerPrestige, T);

            Assert.That(benchRefusal, Is.EqualTo(RefusalReason.None),
                "the bench player does not refuse the tier-2 buyer");

            var sellerProfile = ClubPersonalities.Profile(ClubPersonalities.For(topClub.Id, WorldSeed));
            var buyerProfile = ClubPersonalities.Profile(ClubPersonalities.For(tier2Buyer.Id, WorldSeed));

            NegotiationResult result = NegotiationModel.AutoNegotiate(
                benchPlayer, PlayerImportance.Squad,
                topClub, sellerLeagueLevel: 1, sellerEconomicReputation: 82,
                tier2Buyer, buyerLeagueLevel: 2, buyerEconomicReputation: 82,
                value: 5_000_000L, sellerProfile: sellerProfile, buyerProfile: buyerProfile, buyerBudget: tier2Buyer.TransferBudget, cfg: Cfg);

            Assert.Multiple(() =>
            {
                Assert.That(result.Agreed, Is.True, "buyer with enough budget can sign the bench player");
                Assert.That(result.Outcome, Is.EqualTo(NegotiationOutcome.Agreed));
                Assert.That(result.Fee, Is.GreaterThan(0));
            });
        }

        // ----------------------------------------------------------------- Acceptance 4: Age & role thresholds
        [Test]
        public void YoungAndVeteranPlayers_AndNonBestXi_HaveLowerThresholds()
        {
            const int sellerPrestige = 700;

            var primeStarter = new Player { Age = 26 };
            primeStarter.Attributes.Pace = 75;

            var youngStarter = new Player { Age = 20 };
            youngStarter.Attributes.Pace = 75;

            var veteranStarter = new Player { Age = 33 };
            veteranStarter.Attributes.Pace = 75;

            var primeBench = new Player { Age = 26 };
            primeBench.Attributes.Pace = 75;

            int reqPrime = PrestigeModel.RequiredPrestige(primeStarter, PlayerImportance.Starter, sellerPrestige, T);
            int reqYoung = PrestigeModel.RequiredPrestige(youngStarter, PlayerImportance.Starter, sellerPrestige, T);
            int reqVeteran = PrestigeModel.RequiredPrestige(veteranStarter, PlayerImportance.Starter, sellerPrestige, T);
            int reqBench = PrestigeModel.RequiredPrestige(primeBench, PlayerImportance.Squad, sellerPrestige, T);

            Assert.Multiple(() =>
            {
                Assert.That(reqYoung, Is.LessThan(reqPrime), "player aged <=21 must have a lower threshold than prime starter");
                Assert.That(reqVeteran, Is.LessThan(reqPrime), "player aged >=32 must have a lower threshold than prime starter");
                Assert.That(reqBench, Is.LessThan(reqPrime), "non-best-XI player must have a lower threshold than starter");
            });
        }

        // ----------------------------------------------------------------- Acceptance 5: NegotiationModel refusal & AI enforcement
        [Test]
        public void NegotiationModel_EvaluateOffer_ReturnsRefuseWhenPrestigeTooLow()
        {
            var topClub = new Club { Id = 1, Stature = 95 };
            var lowClub = new Club { Id = 2, Stature = 10 };
            var player = new Player { Age = 26 };
            player.Attributes.Pace = 80;

            long ask = 10_000_000L;
            long offer = 10_000_000L;
            long minSale = 8_000_000L;

            SellerResponse response = NegotiationModel.EvaluateOffer(
                player, PlayerImportance.Starter,
                topClub, sellerLeagueLevel: 1, sellerEconomicReputation: 100,
                lowClub, buyerLeagueLevel: 3, buyerEconomicReputation: 50,
                ask, offer, minSale, Cfg);

            Assert.Multiple(() =>
            {
                Assert.That(response.Decision, Is.EqualTo(SellerDecision.Refuse));
                Assert.That(response.RefusalReason, Is.EqualTo(RefusalReason.PrestigeTooLow));
            });
        }

        [Test]
        public void RunWindow_AiTransfers_NeverViolatePrestigeRefusal()
        {
            // Multi-nation, multi-division world
            var scope = new WorldScope { Size = DatabaseSize.Medium };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 2 });
            scope.Playable.Add(new PlayableNation { Code = "ENG", PlayableTiers = 2 });

            World world = new WorldGenerator(new WorldGenerationOptions { Scope = scope }, Cfg).Generate(WorldSeed);
            new FinanceProgressor(Cfg).SeedTransferBudgets(world.AllLeagues());

            var clubById = world.AllLeagues().SelectMany(l => l.Clubs).ToDictionary(c => c.Id);
            var leagueByClub = new Dictionary<int, League>();
            foreach (League l in world.AllLeagues())
                foreach (Club c in l.Clubs)
                    leagueByClub[c.Id] = l;

            var playerById = world.AllLeagues().SelectMany(l => l.Clubs).SelectMany(c => c.Squad.Players).ToDictionary(p => p.Id);

            var market = new TransferMarket(Cfg);
            List<TransferRecord> transfers = market.RunWindow(world, WorldSeed, 0);

            Assert.That(transfers, Is.Not.Empty, "AI transfers must take place in the world");

            foreach (TransferRecord r in transfers)
            {
                Club seller = clubById[r.FromClubId];
                Club buyer = clubById[r.ToClubId];
                League sellerLeague = leagueByClub[r.FromClubId];
                League buyerLeague = leagueByClub[r.ToClubId];

                int sellerPrestige = PrestigeModel.ClubPrestige(seller, sellerLeague.Division, sellerLeague.EconomicReputation, Cfg);
                int buyerPrestige = PrestigeModel.ClubPrestige(buyer, buyerLeague.Division, buyerLeague.EconomicReputation, Cfg);

                Player player = playerById[r.PlayerId];

                // For whatever importance the player had, the move must be accepted (at least as a bench player or starter)
                bool benchRefused = PrestigeModel.IsRefused(
                    player, PlayerImportance.Squad,
                    seller, sellerLeague.Division, sellerLeague.EconomicReputation,
                    buyer, buyerLeague.Division, buyerLeague.EconomicReputation,
                    Cfg);

                Assert.That(benchRefused, Is.False,
                    $"Transfer {r.PlayerId} from {seller.Name} ({sellerPrestige}) to {buyer.Name} ({buyerPrestige}) violated prestige refusal");
            }
        }
    }
}
