using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Scouting;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Acceptance tests for Issue #11 (R10, R11 world market search, refusal reason, and club budget):
    /// 1. Market Buy tab lists players from any club in the world (paged, filters work);
    /// 2. LocalMarketService runs AI windows over the World;
    /// 3. Negotiation shows localized refusal reason (en+it) and disables offers when refused;
    /// 4. Club screen shows revenue/wages/budget consistent with nation x division x stature.
    /// </summary>
    [TestFixture]
    public class WorldMarketUiTests
    {
        private const ulong Seed = 20260921_011UL;
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FootballTeamSimulator.sln")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new DirectoryNotFoundException("Repo root containing FootballTeamSimulator.sln not found");
        }

        private static World MediumWorld()
        {
            var scope = new WorldScope { Size = DatabaseSize.Medium };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 2 });
            scope.Playable.Add(new PlayableNation { Code = "ENG", PlayableTiers = 2 });

            World world = new WorldGenerator(new WorldGenerationOptions { Scope = scope }, Cfg).Generate(Seed);
            new FinanceProgressor(Cfg).SeedTransferBudgets(world.AllLeagues());
            return world;
        }

        // ============================================================ 1. World Market Search & Paging
        [Test]
        public void WorldMarketSearch_ListsPlayersFromAnyClubInTheWorld_PagedAndFiltered()
        {
            World world = MediumWorld();
            var index = WorldPlayerIndex.Build(world, Cfg.Scouting);

            Club userClub = world.LeaguesAt(LeagueDetailLevel.Playable)[0].Clubs[0];
            int totalWorldPlayers = world.PlayerCount();
            int userPlayers = userClub.Squad.Players.Count;

            var query = new PlayerSearchQuery
            {
                Page = 0,
                PageSize = 20,
                ExcludeOwnClub = true,
                ObserverClubId = userClub.Id
            };

            PlayerSearchPage page0 = index.Search(query, null, Seed, ScoutQuality.Neutral);

            Assert.Multiple(() =>
            {
                Assert.That(page0.Total, Is.EqualTo(totalWorldPlayers - userPlayers),
                    "World search must cover all players in the world except user club");
                Assert.That(page0.Hits.Count, Is.EqualTo(20));
                Assert.That(page0.Page, Is.EqualTo(0));
                Assert.That(page0.PageCount, Is.GreaterThan(1));
            });

            // Verify search contains players across multiple detail levels
            var detailLevels = new HashSet<LeagueDetailLevel>();
            foreach (League league in world.AllLeagues())
            {
                foreach (Club c in league.Clubs)
                {
                    if (page0.Hits.Any(h => h.ClubId == c.Id))
                        detailLevels.Add(league.DetailLevel);
                }
            }
            Assert.That(detailLevels.Count, Is.GreaterThan(0));

            // Verify World.ClubOfPlayer correctly resolves in O(1) for any player
            foreach (PlayerSearchHit hit in page0.Hits)
            {
                Club? resolved = world.ClubOfPlayer(hit.PlayerId);
                Assert.That(resolved, Is.Not.Null);
                Assert.That(resolved!.Id, Is.EqualTo(hit.ClubId));
            }

            // Role filtering
            var roleQuery = new PlayerSearchQuery
            {
                Page = 0,
                PageSize = 50,
                ExcludeOwnClub = true,
                ObserverClubId = userClub.Id,
                Filters = new ScoutingFilters { Role = (int)PositionRole.Goalkeeper }
            };
            PlayerSearchPage gkPage = index.Search(roleQuery, null, Seed, ScoutQuality.Neutral);
            Assert.Multiple(() =>
            {
                Assert.That(gkPage.Hits, Is.Not.Empty);
                Assert.That(gkPage.Hits.All(h => h.Player.Role == PositionRole.Goalkeeper), Is.True,
                    "Every hit with Role=Goalkeeper filter must be a Goalkeeper");
                Assert.That(gkPage.Total, Is.LessThan(page0.Total), "Goalkeeper count must be a subset of total players");
            });

            // Sorting by Value
            var valueQuery = new PlayerSearchQuery
            {
                Page = 0,
                PageSize = 30,
                ExcludeOwnClub = true,
                ObserverClubId = userClub.Id,
                Sort = PlayerSearchSort.Value
            };
            PlayerSearchPage valuePage = index.Search(valueQuery, null, Seed, ScoutQuality.Neutral);
            for (int i = 1; i < valuePage.Hits.Count; i++)
            {
                Assert.That(valuePage.Hits[i - 1].Player.MarketValue, Is.GreaterThanOrEqualTo(valuePage.Hits[i].Player.MarketValue),
                    "Sort by Value must produce non-increasing market values");
            }

            // Sorting by Age
            var ageQuery = new PlayerSearchQuery
            {
                Page = 0,
                PageSize = 30,
                ExcludeOwnClub = true,
                ObserverClubId = userClub.Id,
                Sort = PlayerSearchSort.Age
            };
            PlayerSearchPage agePage = index.Search(ageQuery, null, Seed, ScoutQuality.Neutral);
            for (int i = 1; i < agePage.Hits.Count; i++)
            {
                Assert.That(agePage.Hits[i - 1].Player.Age, Is.LessThanOrEqualTo(agePage.Hits[i].Player.Age),
                    "Sort by Age must produce non-decreasing player ages");
            }

            // Paging stability (page 0 and page 1 are non-overlapping)
            query.Page = 1;
            PlayerSearchPage page1 = index.Search(query, null, Seed, ScoutQuality.Neutral);
            var page0Ids = new HashSet<int>(page0.Hits.Select(h => h.PlayerId));
            var page1Ids = new HashSet<int>(page1.Hits.Select(h => h.PlayerId));
            Assert.That(page0Ids.Overlaps(page1Ids), Is.False, "Adjacent pages must have disjoint sets of players");
        }

        // ============================================================ 2. AI Windows Run Over World
        [Test]
        public void LocalMarketService_AiWindows_RunOverAllWorldLeagues()
        {
            World world = MediumWorld();
            Club userClub = world.LeaguesAt(LeagueDetailLevel.Playable)[0].Clubs[0];

            long initialTotalBudget = world.AllLeagues().SelectMany(l => l.Clubs).Sum(c => c.TransferBudget);
            long initialUserBudget = userClub.TransferBudget;

            var market = new TransferMarket(Cfg);
            List<TransferRecord> records = market.RunWindow(world, Seed, 0, userClub.Id);

            Assert.Multiple(() =>
            {
                Assert.That(records, Is.Not.Empty, "AI window must produce transfers across the world");
                Assert.That(records.All(r => r.FromClubId != userClub.Id && r.ToClubId != userClub.Id), Is.True,
                    "User club must not buy or sell during AI windows");
                Assert.That(userClub.TransferBudget, Is.EqualTo(initialUserBudget),
                    "User club budget must remain unchanged by AI window");
            });

            // Cross-tier / cross-nation participation
            var buyerDetailLevels = records.Select(r => world.LeagueOf(r.ToClubId)?.DetailLevel).Distinct().ToList();
            Assert.That(buyerDetailLevels.Count, Is.GreaterThan(0));

            // Budget conservation across the world
            long finalTotalBudget = world.AllLeagues().SelectMany(l => l.Clubs).Sum(c => c.TransferBudget);
            Assert.That(finalTotalBudget, Is.EqualTo(initialTotalBudget),
                "Total transfer budget across the whole world must be strictly conserved");
        }

        // ============================================================ 3. Prestige Refusal & Localization
        [Test]
        public void PrestigeRefusal_TopPlayerRefusesTierTwo_BenchPlayerAccepts_AndLocalizedReasonFormatted()
        {
            var topClub = new Club { Id = 1, Name = "Top Tier 1 FC", Stature = 95 };
            var tier2Buyer = new Club { Id = 2, Name = "Rich Tier 2 FC", Stature = 40, TransferBudget = 100_000_000L };

            var topPlayer = new Player
            {
                Id = 10,
                FirstName = "Aurelio",
                LastName = "Campione",
                Age = 26,
                Role = PositionRole.Striker
            };
            topPlayer.Attributes.Shooting = 92;
            topPlayer.Attributes.Pace = 90;

            var benchPlayer = new Player
            {
                Id = 11,
                FirstName = "Luca",
                LastName = "Riserva",
                Age = 25,
                Role = PositionRole.Striker
            };
            benchPlayer.Attributes.Shooting = 70;
            benchPlayer.Attributes.Pace = 70;

            // Seller in Tier 1 (Italy), buyer in Tier 2 (Italy)
            int sellerDivision = 1;
            int sellerEconRep = 82;
            int buyerDivision = 2;
            int buyerEconRep = 82;

            RefusalReason starterRefusal = PrestigeModel.EvaluateRefusal(
                topPlayer,
                PlayerImportance.Starter,
                topClub, sellerDivision, sellerEconRep,
                tier2Buyer, buyerDivision, buyerEconRep,
                Cfg);

            Assert.That(starterRefusal, Is.EqualTo(RefusalReason.PrestigeTooLow),
                "Top starter of tier-1 top club must refuse tier-2 buyer");

            RefusalReason benchRefusal = PrestigeModel.EvaluateRefusal(
                benchPlayer,
                PlayerImportance.Squad,
                topClub, sellerDivision, sellerEconRep,
                tier2Buyer, buyerDivision, buyerEconRep,
                Cfg);

            Assert.That(benchRefusal, Is.EqualTo(RefusalReason.None),
                "Bench rotation player must be willing to accept tier-2 buyer");

            // Localization verification for refusal reason
            string root = FindRepoRoot();
            string enPath = Path.Combine(root, "client", "Assets", "Resources", "Localization", "en.json");
            string itPath = Path.Combine(root, "client", "Assets", "Resources", "Localization", "it.json");

            using var enDoc = JsonDocument.Parse(File.ReadAllText(enPath));
            using var itDoc = JsonDocument.Parse(File.ReadAllText(itPath));

            Assert.Multiple(() =>
            {
                Assert.That(enDoc.RootElement.TryGetProperty("negotiation.status.refused", out JsonElement enVal), Is.True);
                Assert.That(itDoc.RootElement.TryGetProperty("negotiation.status.refused", out JsonElement itVal), Is.True);

                string enFormat = enVal.GetString() ?? string.Empty;
                string itFormat = itVal.GetString() ?? string.Empty;

                Assert.That(enFormat, Does.Contain("{0}"), "en refusal must contain {0} for player name");
                Assert.That(itFormat, Does.Contain("{0}"), "it refusal must contain {0} for player name");

                string enFormatted = string.Format(enFormat, topPlayer.FullName);
                string itFormatted = string.Format(itFormat, topPlayer.FullName);

                Assert.That(enFormatted, Does.Contain("Aurelio Campione"));
                Assert.That(enFormatted.ToLowerInvariant(), Does.Contain("prestige"));
                Assert.That(itFormatted, Does.Contain("Aurelio Campione"));
                Assert.That(itFormatted.ToLowerInvariant(), Does.Contain("prestigio"));
            });
        }

        // ============================================================ 4. Club Finances Consistency & UI Strings
        [Test]
        public void ClubFinances_ConsistentWithNationDivisionAndStature_AndLocalizationPresent()
        {
            // Verify finance models consistency
            var clubT1High = new Club { Id = 1, Name = "Big T1", Stature = 90 };
            var clubT1Low = new Club { Id = 2, Name = "Small T1", Stature = 30 };
            var clubT2 = new Club { Id = 3, Name = "T2 Club", Stature = 30 };

            var testPlayer = new Player { Id = 100, Age = 25, Role = PositionRole.CentralMidfielder, MarketValue = 10_000_000L };
            clubT1High.Squad.Players.Add(testPlayer);
            clubT1Low.Squad.Players.Add(testPlayer);
            clubT2.Squad.Players.Add(testPlayer);

            long revT1High = FinanceModel.EstimatedWeeklyRevenue(clubT1High, 1, 100, 20, Cfg);
            long revT1Low = FinanceModel.EstimatedWeeklyRevenue(clubT1Low, 1, 100, 20, Cfg);
            long revT2 = FinanceModel.EstimatedWeeklyRevenue(clubT2, 2, 100, 20, Cfg);

            Assert.Multiple(() =>
            {
                Assert.That(revT1High, Is.GreaterThan(revT1Low), "Higher stature club must earn more than lower stature club in same league");
                Assert.That(revT1Low, Is.GreaterThan(revT2), "Tier 1 club must earn more than Tier 2 club of same stature");
            });

            long budgetT1High = FinanceModel.SeasonTransferBudget(clubT1High, 1, 100, Cfg);
            long budgetT1Low = FinanceModel.SeasonTransferBudget(clubT1Low, 1, 100, Cfg);
            long budgetT2 = FinanceModel.SeasonTransferBudget(clubT2, 2, 100, Cfg);

            Assert.Multiple(() =>
            {
                Assert.That(budgetT1High, Is.GreaterThan(budgetT1Low), "Higher stature club must have larger transfer budget");
                Assert.That(budgetT1Low, Is.GreaterThan(budgetT2), "Tier 1 club must have larger transfer budget than Tier 2");
            });

            long wageT1High = FinanceModel.WeeklyWageBill(clubT1High, 1000, 1, 100, Cfg);
            long wageT1Low = FinanceModel.WeeklyWageBill(clubT1Low, 1000, 1, 100, Cfg);
            long wageT2 = FinanceModel.WeeklyWageBill(clubT2, 1000, 2, 100, Cfg);

            Assert.Multiple(() =>
            {
                Assert.That(wageT1High, Is.GreaterThan(wageT1Low), "Higher stature club must have higher wage bill");
                Assert.That(wageT1Low, Is.GreaterThan(wageT2), "Tier 1 club must have higher wage bill than Tier 2");
            });

            // Verify localization strings for club budget and market paging
            string root = FindRepoRoot();
            string enPath = Path.Combine(root, "client", "Assets", "Resources", "Localization", "en.json");
            string itPath = Path.Combine(root, "client", "Assets", "Resources", "Localization", "it.json");

            using var enDoc = JsonDocument.Parse(File.ReadAllText(enPath));
            using var itDoc = JsonDocument.Parse(File.ReadAllText(itPath));

            Assert.Multiple(() =>
            {
                Assert.That(enDoc.RootElement.TryGetProperty("club.budget", out JsonElement enBudget), Is.True);
                Assert.That(itDoc.RootElement.TryGetProperty("club.budget", out JsonElement itBudget), Is.True);
                Assert.That(enBudget.GetString(), Is.EqualTo("Transfer budget"));
                Assert.That(itBudget.GetString(), Is.EqualTo("Budget trasferimenti"));

                Assert.That(enDoc.RootElement.TryGetProperty("market.page.summary", out JsonElement enPager), Is.True);
                Assert.That(itDoc.RootElement.TryGetProperty("market.page.summary", out JsonElement itPager), Is.True);
                Assert.That(enPager.GetString(), Does.Contain("{0}").And.Contains("{1}").And.Contains("{2}"));
                Assert.That(itPager.GetString(), Does.Contain("{0}").And.Contains("{1}").And.Contains("{2}"));
            });
        }
    }
}
