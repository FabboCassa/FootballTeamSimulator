using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Task: worldwide AI transfer market within 2s (R10 of the realistic-club-economy spec):
    ///   - <see cref="TransferMarket.RunWindow(World, ulong, int, int)"/> trades across the WHOLE
    ///     world — playable, background AND data-only clubs alike — not just the leagues the host
    ///     happens to simulate in full detail;
    ///   - a window produces at least one cross-nation deal and at least one data-only club acting
    ///     as buyer or seller;
    ///   - money is only ever moved between clubs, never created or destroyed;
    ///   - one window on the Medium database preset resolves in well under 2s on desktop;
    ///   - the human's club is still excluded as both buyer and seller.
    ///
    /// Pure/deterministic (integer math, no new RNG streams) and off the match-engine path, so the
    /// golden master is untouched - proven elsewhere by SimulationDeterminismTests staying green.
    /// </summary>
    [TestFixture]
    public class WorldMarketTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private const ulong WorldSeed = 20260919_001UL;

        /// <summary>A Medium-preset, multi-nation world with every detail level represented: the shape a real career actually loads.</summary>
        private static World MediumWorld()
        {
            var scope = new WorldScope { Size = DatabaseSize.Medium };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 2 });
            scope.Playable.Add(new PlayableNation { Code = "ENG", PlayableTiers = 2 });

            World world = new WorldGenerator(new WorldGenerationOptions { Scope = scope }, Cfg).Generate(WorldSeed);
            new FinanceProgressor(Cfg).SeedTransferBudgets(world.AllLeagues());
            return world;
        }

        private static long TotalBudget(World world) =>
            world.AllLeagues().SelectMany(l => l.Clubs).Sum(c => c.TransferBudget);

        // ----------------------------------------------------------------- whole-world coverage

        [Test]
        public void RunWindow_OperatesOverPlayableBackgroundAndDataOnlyClubs()
        {
            World world = MediumWorld();
            Assert.That(world.LeaguesAt(LeagueDetailLevel.Playable), Is.Not.Empty);
            Assert.That(world.LeaguesAt(LeagueDetailLevel.Background), Is.Not.Empty);
            Assert.That(world.LeaguesAt(LeagueDetailLevel.DataOnly), Is.Not.Empty,
                "the Medium preset must actually load some data-only nations for this test to mean anything");

            int humanClubId = world.LeaguesAt(LeagueDetailLevel.Playable)[0].Clubs[0].Id;

            List<TransferRecord> records = new TransferMarket(Cfg).RunWindow(world, WorldSeed, 0, humanClubId);

            TestContext.Out.WriteLine(
                $"[world-market] {world.ClubCount()} clubs, {world.PlayerCount()} players, {records.Count} transfers");
            Assert.That(records, Is.Not.Empty, "a world of 1000+ clubs must produce AI transfers");
        }

        // ----------------------------------------------------------------- cross-nation + data-only participation

        [Test]
        public void RunWindow_ProducesACrossNationDeal_AndADataOnlyBuyerOrSeller()
        {
            World world = MediumWorld();
            int humanClubId = world.LeaguesAt(LeagueDetailLevel.Playable)[0].Clubs[0].Id;
            var dataOnlyClubIds = new HashSet<int>(
                world.LeaguesAt(LeagueDetailLevel.DataOnly).SelectMany(l => l.Clubs).Select(c => c.Id));

            List<TransferRecord> records = new TransferMarket(Cfg).RunWindow(world, WorldSeed, 0, humanClubId);
            Assert.That(records, Is.Not.Empty);

            bool anyCrossNation = records.Any(r =>
            {
                string fromNation = world.LeagueOf(r.FromClubId)?.NationCode ?? string.Empty;
                string toNation = world.LeagueOf(r.ToClubId)?.NationCode ?? string.Empty;
                return fromNation.Length > 0 && toNation.Length > 0 && fromNation != toNation;
            });
            bool anyDataOnlyParty = records.Any(r =>
                dataOnlyClubIds.Contains(r.FromClubId) || dataOnlyClubIds.Contains(r.ToClubId));

            TestContext.Out.WriteLine(
                $"[world-market] {records.Count} transfers, cross-nation={anyCrossNation}, data-only party={anyDataOnlyParty}");

            Assert.That(anyCrossNation, Is.True, "at least one deal must cross a nation boundary");
            Assert.That(anyDataOnlyParty, Is.True, "at least one data-only club must be a buyer or seller");
        }

        // ----------------------------------------------------------------- money conservation

        [Test]
        public void RunWindow_ConservesMoney_AcrossTheWholeWorld()
        {
            World world = MediumWorld();
            int humanClubId = world.LeaguesAt(LeagueDetailLevel.Playable)[0].Clubs[0].Id;
            long before = TotalBudget(world);

            List<TransferRecord> records = new TransferMarket(Cfg).RunWindow(world, WorldSeed, 0, humanClubId);

            long after = TotalBudget(world);
            Assert.That(records, Is.Not.Empty);
            Assert.That(after, Is.EqualTo(before), "a world-wide window only moves money between clubs, never creates or destroys it");
        }

        // ----------------------------------------------------------------- human club excluded

        [Test]
        public void RunWindow_NeverTradesTheHumanClub_AtWorldScale()
        {
            World world = MediumWorld();
            Club human = world.LeaguesAt(LeagueDetailLevel.Playable)[0].Clubs[0];
            int humanClubId = human.Id;
            var before = human.Squad.Players.Select(p => p.Id).OrderBy(x => x).ToArray();

            List<TransferRecord> records = new TransferMarket(Cfg).RunWindow(world, WorldSeed, 0, humanClubId);

            var after = human.Squad.Players.Select(p => p.Id).OrderBy(x => x).ToArray();
            Assert.That(after, Is.EqualTo(before), "the human squad must be untouched by the world-wide AI market");
            Assert.That(records.Any(r => r.FromClubId == humanClubId || r.ToClubId == humanClubId), Is.False,
                "no transfer may involve the human club");
        }

        // ----------------------------------------------------------------- performance: Medium preset <= 2s

        [Test]
        public void RunWindow_OnTheMediumPreset_ResolvesWellUnderTwoSeconds()
        {
            World world = MediumWorld();
            int humanClubId = world.LeaguesAt(LeagueDetailLevel.Playable)[0].Clubs[0].Id;
            var market = new TransferMarket(Cfg);

            // Warm-up run: pays for JIT/tiered compilation once so the measured run reflects steady-state
            // cost, exactly like the harness measures it. Window 0 (warm-up) and window 1 (measured) use
            // different indices, so the measured run is a genuine, differently-seeded window, not a
            // repeat of the warm-up's exact RNG draws.
            market.RunWindow(world, WorldSeed, 0, humanClubId);

            var stopwatch = Stopwatch.StartNew();
            List<TransferRecord> records = market.RunWindow(world, WorldSeed, 1, humanClubId);
            stopwatch.Stop();

            TestContext.Out.WriteLine(
                $"[world-market-perf] {world.ClubCount()} clubs, {world.PlayerCount()} players, " +
                $"{records.Count} transfers in {stopwatch.Elapsed.TotalMilliseconds:F0} ms " +
                "(measured here in a debug NUnit run; the shipped Release/IL2CPP build is faster still)");

            Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.LessThan(2000),
                "one world-wide transfer window on the Medium preset must resolve in well under 2s");
        }
    }
}
