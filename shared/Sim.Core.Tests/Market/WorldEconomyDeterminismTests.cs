using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Acceptance tests for R14 of realistic-club-economy.md:
    /// - Golden master 0x5EF1EDDAFA52BAFA unchanged;
    /// - Two worlds from the same seed produce identical stature, finances and transfer records
    ///   after a full season with two windows;
    /// - No Math.Pow/Exp/Log or DateTime in new Market code.
    /// </summary>
    [TestFixture]
    public class WorldEconomyDeterminismTests
    {
        private const ulong GoldenCombinedHash = 0x5EF1EDDAFA52BAFAUL;
        private const ulong TestSeed = 20260921_1300UL;
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FootballTeamSimulator.sln")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new DirectoryNotFoundException("Repo root containing FootballTeamSimulator.sln not found");
        }

        // ============================================================ 1. Golden master guard
        [Test]
        public void GoldenMaster_CombinedHash_IsUnchanged()
        {
            DeterminismCheck.Result result = DeterminismCheck.Run();

            Assert.That(result.MatchHashes.Count, Is.EqualTo(DeterminismCheck.DefaultMatches));
            Assert.That(result.CombinedHash, Is.EqualTo(GoldenCombinedHash),
                $"Combined hash {result.CombinedHashHex} != golden 0x{GoldenCombinedHash:X16}");
            Assert.That(result.CombinedHashHex, Is.EqualTo("0x5EF1EDDAFA52BAFA"));
        }

        // ============================================================ 2. Code purity (no Math.Pow/Exp/Log, DateTime, System.Random)
        [Test]
        public void MarketCode_ContainsNoForbiddenFloatingPointOrDateTimeFunctions()
        {
            string repoRoot = FindRepoRoot();
            string marketDir = Path.Combine(repoRoot, "shared", "Sim.Core", "Market");
            Assert.That(Directory.Exists(marketDir), Is.True, $"Market directory must exist at {marketDir}");

            string[] sourceFiles = Directory.GetFiles(marketDir, "*.cs", SearchOption.AllDirectories);
            Assert.That(sourceFiles.Length, Is.GreaterThanOrEqualTo(10), "Market directory should contain all economy/market source files");

            var forbiddenPatterns = new[]
            {
                @"Math\.(Pow|Exp|Log|Sin|Cos|Tan|Sqrt|Round|Floor|Ceiling)",
                @"\bDateTime\b",
                @"\bDateTimeOffset\b",
                @"\bSystem\.Random\b"
            };

            var regexes = forbiddenPatterns.Select(p => new Regex(p, RegexOptions.Compiled)).ToList();
            var violations = new List<string>();

            foreach (string file in sourceFiles)
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.Trim();

                    // Skip comment lines
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                        continue;

                    // Strip inline trailing comments
                    int commentIdx = line.IndexOf("//", StringComparison.Ordinal);
                    string codeOnly = commentIdx >= 0 ? line.Substring(0, commentIdx) : line;

                    foreach (var regex in regexes)
                    {
                        if (regex.IsMatch(codeOnly))
                        {
                            violations.Add($"{Path.GetFileName(file)}:{i + 1} - {trimmed}");
                        }
                    }
                }
            }

            Assert.That(violations, Is.Empty,
                $"Found forbidden floating-point or time function calls in Market code:\n{string.Join("\n", violations)}");
        }

        // ============================================================ 3. Determinism across full season + 2 windows
        [Test]
        public void TwoWorlds_FromSameSeed_ProduceIdenticalStatureFinancesAndTransfers_AfterFullSeasonWithTwoWindows()
        {
            SimulationRunResult runA = SimulateSeason(TestSeed);
            SimulationRunResult runB = SimulateSeason(TestSeed);

            // 1. Summer transfers (window 0) must match exactly
            Assert.That(runA.SummerTransfers, Is.Not.Empty);
            Assert.That(runA.SummerTransfers.Count, Is.EqualTo(runB.SummerTransfers.Count),
                "Summer transfer count must be identical between runs");

            for (int i = 0; i < runA.SummerTransfers.Count; i++)
            {
                TransferRecord tA = runA.SummerTransfers[i];
                TransferRecord tB = runB.SummerTransfers[i];
                Assert.That(tA.PlayerId, Is.EqualTo(tB.PlayerId), $"Summer transfer #{i} PlayerId mismatch");
                Assert.That(tA.FromClubId, Is.EqualTo(tB.FromClubId), $"Summer transfer #{i} FromClubId mismatch");
                Assert.That(tA.ToClubId, Is.EqualTo(tB.ToClubId), $"Summer transfer #{i} ToClubId mismatch");
                Assert.That(tA.Fee, Is.EqualTo(tB.Fee), $"Summer transfer #{i} Fee mismatch");
            }

            TestContext.Out.WriteLine($"Summer transfers: {runA.SummerTransfers.Count}, Winter transfers: {runA.WinterTransfers.Count}");
            Assert.That(runA.WinterTransfers, Is.Not.Empty);
            Assert.That(runA.WinterTransfers.Count, Is.EqualTo(runB.WinterTransfers.Count),
                "Winter transfer count must be identical between runs");

            for (int i = 0; i < runA.WinterTransfers.Count; i++)
            {
                TransferRecord tA = runA.WinterTransfers[i];
                TransferRecord tB = runB.WinterTransfers[i];
                Assert.That(tA.PlayerId, Is.EqualTo(tB.PlayerId), $"Winter transfer #{i} PlayerId mismatch");
                Assert.That(tA.FromClubId, Is.EqualTo(tB.FromClubId), $"Winter transfer #{i} FromClubId mismatch");
                Assert.That(tA.ToClubId, Is.EqualTo(tB.ToClubId), $"Winter transfer #{i} ToClubId mismatch");
                Assert.That(tA.Fee, Is.EqualTo(tB.Fee), $"Winter transfer #{i} Fee mismatch");
            }

            // 3. Rollover promotions and relegations must match
            Assert.That(runA.Rollover.PromotedClubIds, Is.EqualTo(runB.Rollover.PromotedClubIds));
            Assert.That(runA.Rollover.RelegatedClubIds, Is.EqualTo(runB.Rollover.RelegatedClubIds));
            Assert.That(runA.Rollover.ChampionByNation, Is.EqualTo(runB.Rollover.ChampionByNation));

            // 4. Stature, Finances, Budgets and Squads must match for every club in the world
            List<Club> clubsA = runA.World.AllLeagues().SelectMany(l => l.Clubs).ToList();
            List<Club> clubsB = runB.World.AllLeagues().SelectMany(l => l.Clubs).ToList();

            Assert.That(clubsA.Count, Is.EqualTo(clubsB.Count));
            Assert.That(clubsA.Count, Is.GreaterThan(50), "World must have non-trivial club count");

            for (int i = 0; i < clubsA.Count; i++)
            {
                Club cA = clubsA[i];
                Club cB = clubsB[i];

                Assert.That(cA.Id, Is.EqualTo(cB.Id));
                Assert.That(cA.Stature, Is.EqualTo(cB.Stature), $"Club {cA.Name} (Id={cA.Id}) Stature differs");
                Assert.That(cA.TransferBudget, Is.EqualTo(cB.TransferBudget), $"Club {cA.Name} (Id={cA.Id}) TransferBudget differs");
                Assert.That(cA.Finances.Balance, Is.EqualTo(cB.Finances.Balance), $"Club {cA.Name} (Id={cA.Id}) Balance differs");
                Assert.That(cA.Finances.SeasonIncome, Is.EqualTo(cB.Finances.SeasonIncome), $"Club {cA.Name} (Id={cA.Id}) SeasonIncome differs");
                Assert.That(cA.Finances.SeasonExpense, Is.EqualTo(cB.Finances.SeasonExpense), $"Club {cA.Name} (Id={cA.Id}) SeasonExpense differs");
                Assert.That(cA.Finances.SeasonWageExpense, Is.EqualTo(cB.Finances.SeasonWageExpense), $"Club {cA.Name} (Id={cA.Id}) SeasonWageExpense differs");
                Assert.That(cA.Finances.SeasonGateIncome, Is.EqualTo(cB.Finances.SeasonGateIncome), $"Club {cA.Name} (Id={cA.Id}) SeasonGateIncome differs");
                Assert.That(cA.Finances.SeasonSponsorIncome, Is.EqualTo(cB.Finances.SeasonSponsorIncome), $"Club {cA.Name} (Id={cA.Id}) SeasonSponsorIncome differs");
                Assert.That(cA.Finances.SeasonPrizeIncome, Is.EqualTo(cB.Finances.SeasonPrizeIncome), $"Club {cA.Name} (Id={cA.Id}) SeasonPrizeIncome differs");
                Assert.That(cA.Finances.SeasonEstimatedIncome, Is.EqualTo(cB.Finances.SeasonEstimatedIncome), $"Club {cA.Name} (Id={cA.Id}) SeasonEstimatedIncome differs");

                // Squad players
                Assert.That(cA.Squad.Players.Count, Is.EqualTo(cB.Squad.Players.Count), $"Club {cA.Name} squad count differs");
                for (int p = 0; p < cA.Squad.Players.Count; p++)
                {
                    Assert.That(cA.Squad.Players[p].Id, Is.EqualTo(cB.Squad.Players[p].Id),
                        $"Club {cA.Name} player #{p} ID differs");
                }
            }
        }

        // ============================================================ 4. Sensitivity test (different seeds diverge)
        [Test]
        public void TwoWorlds_FromDifferentSeeds_ProduceDivergentOutcomes()
        {
            SimulationRunResult runA = SimulateSeason(TestSeed);
            SimulationRunResult runB = SimulateSeason(TestSeed + 100);

            // The transfer lists or finances should differ
            bool transfersDiffer = runA.SummerTransfers.Count != runB.SummerTransfers.Count ||
                runA.SummerTransfers.Zip(runB.SummerTransfers, (a, b) => a.PlayerId != b.PlayerId || a.Fee != b.Fee).Any(diff => diff);

            Assert.That(transfersDiffer, Is.True, "Different seeds must produce different transfer market outcomes");
        }

        // ============================================================ Simulation helper
        private sealed class SimulationRunResult
        {
            public required World World { get; set; }
            public required List<TransferRecord> SummerTransfers { get; set; }
            public required List<TransferRecord> WinterTransfers { get; set; }
            public required WorldRolloverResult Rollover { get; set; }
        }

        private static SimulationRunResult SimulateSeason(ulong seed)
        {
            var scope = new WorldScope { Size = DatabaseSize.Medium };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 2 });
            scope.Playable.Add(new PlayableNation { Code = "ENG", PlayableTiers = 2 });

            World world = new WorldGenerator(new WorldGenerationOptions { Scope = scope }, Cfg).Generate(seed);
            var finance = new FinanceProgressor(Cfg);
            finance.SeedWorld(world);

            var playable = world.PlayableLeagues();
            var background = world.LeaguesAt(LeagueDetailLevel.Background);
            var dataOnly = world.LeaguesAt(LeagueDetailLevel.DataOnly);

            int humanClubId = playable[0].Clubs[0].Id;
            var transferMarket = new TransferMarket(Cfg);

            // 1. Summer transfer window (window 0)
            List<TransferRecord> summerTransfers = transferMarket.RunWindow(world, seed, windowIndex: 0, humanClubId: humanClubId);

            // Generate career season fixtures
            Season careerSeason = new Season { Year = 1 };
            var fixtureGen = new FixtureGenerator(Cfg.Season);
            int nextId = 1;
            foreach (League league in playable)
            {
                var fixtures = fixtureGen.Generate(league, new Pcg32(seed, WorldRollover.CareerFixtureSequenceBase + (ulong)league.Id), nextId);
                nextId += fixtures.Count;
                careerSeason.Fixtures.AddRange(fixtures);
            }

            int totalDays = careerSeason.Fixtures.Count > 0 ? careerSeason.Fixtures.Max(f => f.Day) : 38;
            int midDay = totalDays / 2;

            // 2. Play first half of season
            var backgroundProgressor = new BackgroundLeagueProgressor(Cfg);

            for (int day = 1; day <= midDay; day++)
            {
                var playablePlayedToday = new List<Fixture>();
                foreach (Fixture f in careerSeason.Fixtures)
                {
                    if (f.Day == day)
                    {
                        Club? home = world.FindClub(f.HomeClubId);
                        Club? away = world.FindClub(f.AwayClubId);
                        if (home != null && away != null)
                        {
                            QuickResultResolver.Resolve(f, home.Strength, away.Strength, seed, Cfg);
                            playablePlayedToday.Add(f);
                        }
                    }
                }

                if (playablePlayedToday.Count > 0)
                    finance.AccrueMatchday(playable, playablePlayedToday);

                backgroundProgressor.AdvanceTo(world, day, seed);
                if (background.Count > 0)
                {
                    var bgPlayedToday = new List<Fixture>();
                    foreach (Fixture f in world.BackgroundSeason.Fixtures)
                    {
                        if (f.Played && f.Day == day)
                            bgPlayedToday.Add(f);
                    }
                    if (bgPlayedToday.Count > 0)
                        finance.AccrueMatchday(background, bgPlayedToday);
                }

                // Weekly finances
                if (day % 7 == 0)
                {
                    finance.AccrueWeek(playable, careerSeason);
                    if (background.Count > 0)
                        finance.AccrueWeek(background, world.BackgroundSeason);
                    if (dataOnly.Count > 0)
                        finance.AccrueDataOnlyWeek(dataOnly);
                }
            }

            // 3. Winter transfer window (window 1)
            List<TransferRecord> winterTransfers = transferMarket.RunWindow(world, seed, windowIndex: 1, humanClubId: humanClubId);

            // 4. Play second half of season
            for (int day = midDay + 1; day <= totalDays; day++)
            {
                var playablePlayedToday = new List<Fixture>();
                foreach (Fixture f in careerSeason.Fixtures)
                {
                    if (f.Day == day)
                    {
                        Club? home = world.FindClub(f.HomeClubId);
                        Club? away = world.FindClub(f.AwayClubId);
                        if (home != null && away != null)
                        {
                            QuickResultResolver.Resolve(f, home.Strength, away.Strength, seed, Cfg);
                            playablePlayedToday.Add(f);
                        }
                    }
                }

                if (playablePlayedToday.Count > 0)
                    finance.AccrueMatchday(playable, playablePlayedToday);

                backgroundProgressor.AdvanceTo(world, day, seed);
                if (background.Count > 0)
                {
                    var bgPlayedToday = new List<Fixture>();
                    foreach (Fixture f in world.BackgroundSeason.Fixtures)
                    {
                        if (f.Played && f.Day == day)
                            bgPlayedToday.Add(f);
                    }
                    if (bgPlayedToday.Count > 0)
                        finance.AccrueMatchday(background, bgPlayedToday);
                }

                // Weekly finances
                if (day % 7 == 0)
                {
                    finance.AccrueWeek(playable, careerSeason);
                    if (background.Count > 0)
                        finance.AccrueWeek(background, world.BackgroundSeason);
                    if (dataOnly.Count > 0)
                        finance.AccrueDataOnlyWeek(dataOnly);
                }
            }

            // Ensure all background fixtures up to end of calendar are finished
            backgroundProgressor.AdvanceTo(world, int.MaxValue, seed);

            // 5. Award prize money
            finance.AwardPrizeMoney(playable, careerSeason);
            if (background.Count > 0)
                finance.AwardPrizeMoney(background, world.BackgroundSeason);

            // 6. Stature evolution finishes capture
            var finishes = new Dictionary<int, (int actual, int expected)>();
            var boardModel = new BoardModel(Cfg);

            foreach (Nation nation in world.Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    if (league.DetailLevel == LeagueDetailLevel.DataOnly)
                        continue;

                    Season s = league.DetailLevel == LeagueDetailLevel.Playable ? careerSeason : world.BackgroundSeason;
                    List<LeagueTableRow> table = LeagueTable.Compute(league, s, Cfg.Season);
                    for (int i = 0; i < table.Count; i++)
                    {
                        Club? club = league.FindClub(table[i].ClubId);
                        if (club == null) continue;

                        int actual = i + 1;
                        int expected = club.Coach != null && club.Coach.ObjectiveExpectedPosition >= 1
                            ? club.Coach.ObjectiveExpectedPosition
                            : boardModel.ExpectedPosition(club, league, 0);

                        finishes[club.Id] = (actual, expected);
                    }
                }
            }

            // 7. Rollover
            WorldRolloverResult rolloverResult = new WorldRollover(Cfg).EndSeason(world, careerSeason, seed);
            var promotedSet = new HashSet<int>(rolloverResult.PromotedClubIds);
            var relegatedSet = new HashSet<int>(rolloverResult.RelegatedClubIds);

            var statureProgressor = new StatureProgressor(Cfg);
            foreach (var kvp in finishes)
            {
                int clubId = kvp.Key;
                var (actual, expected) = kvp.Value;
                Club? club = world.FindClub(clubId);
                if (club != null)
                {
                    bool promoted = promotedSet.Contains(clubId);
                    bool relegated = relegatedSet.Contains(clubId);
                    statureProgressor.ApplySeasonEnd(club, actual, expected, promoted, relegated);
                }
            }

            // 8. Reseed budgets and reset counters
            FinanceProgressor.ResetSeasonCounters(world.AllLeagues());
            finance.SeedTransferBudgets(world.AllLeagues());

            return new SimulationRunResult
            {
                World = world,
                SummerTransfers = summerTransfers,
                WinterTransfers = winterTransfers,
                Rollover = rolloverResult
            };
        }
    }
}
