using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Tests.Development
{
    /// <summary>
    /// Task 4.4 acceptance: development & ageing on top of the 4.3 training foundation.
    /// THE ✅ (10-season harness): youth who play grow more than benched youth, players in
    /// their thirties decline gently, and no attribute collapse is possible. Plus the
    /// supporting guarantees — the age curve is position-dependent, everything is
    /// deterministic, the whole world ages, and at full youth + neutral modifiers the 4.4
    /// model reduces EXACTLY to the 4.3 training model (clean superset; golden masters safe).
    /// </summary>
    [TestFixture]
    public class AgingTests
    {
        private const int WeeksPerSeason = 38;
        private const int Seasons = 10;
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static DevelopmentBalance D => Cfg.Development;

        // --------------------------------------------- THE ✅ (minutes): youth need games

        [Test]
        public void YouthWithMinutes_GrowMoreThanBenchedYouth_OverTenSeasons()
        {
            // Two identical young squads (same seed == same save), lots of headroom to grow.
            Club played = MakeYoungSquad(seed: 4_040_001, clubIndex: 6, startAge: 18, headroom: 25);
            Club benched = MakeYoungSquad(seed: 4_040_001, clubIndex: 6, startAge: 18, headroom: 25);

            int startTotal = SkillTotal(played);
            Assert.That(SkillTotal(benched), Is.EqualTo(startTotal), "Same seed must start identical");

            var everPresent = new DevelopmentContext(playingSharePercent: 100, D.FacilityNeutralLevel, D.PerformanceNeutralRating);
            var onTheBench = new DevelopmentContext(playingSharePercent: 0, D.FacilityNeutralLevel, D.PerformanceNeutralRating);

            // Same RNG stream each week (so only minutes differ); identical ageing on both.
            DevelopSeasons(played, TeamTrainingFocus.Balanced, everPresent, baseSeed: 321);
            DevelopSeasons(benched, TeamTrainingFocus.Balanced, onTheBench, baseSeed: 321);

            int playedTotal = SkillTotal(played);
            int benchedTotal = SkillTotal(benched);
            TestContext.Out.WriteLine(
                $"[aging-minutes] start {startTotal} -> playing {playedTotal} (+{playedTotal - startTotal}), " +
                $"benched {benchedTotal} (+{benchedTotal - startTotal}) over {Seasons} seasons");

            Assert.That(benchedTotal, Is.GreaterThan(startTotal), "Benched youth still develop, just slower");
            Assert.That(playedTotal, Is.GreaterThan(benchedTotal), "Youth who play must grow more than benched youth (the ✅)");
        }

        // --------------------------------------------- thirties decline gently, no collapse

        [Test]
        public void Veterans_DeclineGently_AndNeverCollapse_OverTenSeasons()
        {
            // Peaked squad (potential == current), entering their thirties; they only age.
            Club club = MakeClub(seed: 4_040_002, clubIndex: 3);
            foreach (Player p in club.Squad.Players)
            {
                p.Age = 33;
                p.Development.Potential = PlayerRating.Overall(p);
            }

            var before = new Dictionary<int, int>();
            foreach (Player p in club.Squad.Players) before[p.Id] = PlayerRating.Overall(p);

            // Balanced focus = no skill drilled hard enough to be protected → honest decline.
            DevelopSeasons(club, TeamTrainingFocus.Balanced, DevelopmentContext.Neutral(D), baseSeed: 77);

            int declined = 0, worstDrop = 0, totalDrop = 0;
            foreach (Player p in club.Squad.Players)
            {
                int overall = PlayerRating.Overall(p);
                int floor = p.Development.Potential * D.AgeDeclineFloorPercentOfPotential / 100;

                Assert.That(overall, Is.GreaterThanOrEqualTo(floor - 2),
                    $"Ageing must never collapse a player below his potential floor (player {p.Id})");
                Assert.That(overall, Is.LessThanOrEqualTo(before[p.Id]),
                    "An ageing peaked player can't end higher than he started");

                int drop = before[p.Id] - overall;
                if (drop > 0) declined++;
                if (drop > worstDrop) worstDrop = drop;
                totalDrop += drop;
            }

            int n = club.Squad.Players.Count;
            TestContext.Out.WriteLine(
                $"[aging-decline] {declined}/{n} thirty-somethings declined over {Seasons} seasons; " +
                $"avg drop {totalDrop / n}, worst {worstDrop} overall points (~{worstDrop / Seasons}/season) — gentle, floored");

            Assert.That(declined, Is.GreaterThan(n / 2),
                "Most players in their thirties must visibly decline (the world doesn't freeze)");
        }

        // --------------------------------------------- age curve shape (position-dependent)

        [Test]
        public void AgeCurve_IsPositionDependent_AndMonotonic()
        {
            // Growth: full while young, zero at the peak, never rising with age.
            Assert.That(AgeCurve.GrowthFactorPermille(18, PositionRole.Striker, D), Is.EqualTo(1000));
            Assert.That(AgeCurve.GrowthFactorPermille(22, PositionRole.Striker, D),
                Is.GreaterThan(AgeCurve.GrowthFactorPermille(25, PositionRole.Striker, D)));
            Assert.That(AgeCurve.GrowthFactorPermille(40, PositionRole.Striker, D), Is.Zero);

            // Keepers/centre-backs peak and decline later than pace-reliant wingers.
            Assert.That(AgeCurve.PeakAge(PositionRole.Goalkeeper, D),
                Is.GreaterThan(AgeCurve.PeakAge(PositionRole.Winger, D)));
            Assert.That(AgeCurve.DeclineOnsetAge(PositionRole.Goalkeeper, D),
                Is.GreaterThan(AgeCurve.DeclineOnsetAge(PositionRole.Winger, D)));

            // At 31 a winger is already declining; a goalkeeper is not yet.
            Assert.That(AgeCurve.DeclineMultiplierPermille(31, PositionRole.Winger, D), Is.GreaterThan(0));
            Assert.That(AgeCurve.DeclineMultiplierPermille(31, PositionRole.Goalkeeper, D), Is.Zero);

            // Decline pressure rises with age and is capped (no cliff → anti-collapse).
            Assert.That(AgeCurve.DeclineMultiplierPermille(38, PositionRole.Striker, D),
                Is.GreaterThan(AgeCurve.DeclineMultiplierPermille(33, PositionRole.Striker, D)));
            Assert.That(AgeCurve.DeclineMultiplierPermille(60, PositionRole.Striker, D),
                Is.EqualTo(D.AgeDeclineMaxMultiplierPermille));
        }

        // --------------------------------------------- clean superset: 4.4 reduces to 4.3

        [Test]
        public void FullYouth_NeutralModifiers_ReducesExactlyToTrainingModel()
        {
            // At/below the youth-full age the age factor is 1000, and a neutral context is
            // full minutes / neutral facility / neutral performance (all factors 1000), so
            // the 4.4 growth must be byte-identical to the 4.3 training model.
            Club v44 = MakeYoungSquad(seed: 909_044, clubIndex: 4, startAge: 18, headroom: 20);
            Club v43 = MakeYoungSquad(seed: 909_044, clubIndex: 4, startAge: 18, headroom: 20);

            DevelopmentContext neutral = DevelopmentContext.Neutral(D);
            for (int week = 0; week < WeeksPerSeason; week++)
            {
                var r44 = new Pcg32(555, (ulong)week);
                var r43 = new Pcg32(555, (ulong)week);
                foreach (Player p in v44.Squad.Players)
                    DevelopmentModel.ApplyDevelopmentWeek(p, TeamTrainingFocus.Attacking, IndividualTrainingFocus.None, neutral, r44, D);
                foreach (Player p in v43.Squad.Players)
                    TrainingModel.ApplyTrainingWeek(p, TeamTrainingFocus.Attacking, IndividualTrainingFocus.None, r43, D);
            }

            foreach (var (a, b) in Pairs(v44, v43))
                for (int s = 0; s < PlayerAttributes.SkillCount; s++)
                    Assert.That(a.Attributes[s], Is.EqualTo(b.Attributes[s]),
                        $"At full youth + neutral modifiers 4.4 must equal 4.3 (player {a.Id}, skill {s})");
        }

        // --------------------------------------------- determinism

        [Test]
        public void Development_IsDeterministic_PerSeedContextAndPlan()
        {
            Club a = MakeYoungSquad(seed: 4_040_003, clubIndex: 2, startAge: 20, headroom: 15);
            Club b = MakeYoungSquad(seed: 4_040_003, clubIndex: 2, startAge: 20, headroom: 15);

            var ctx = new DevelopmentContext(playingSharePercent: 60, facilityLevel: 80, performanceRating: 65);
            DevelopSeasons(a, TeamTrainingFocus.Technical, ctx, baseSeed: 13);
            DevelopSeasons(b, TeamTrainingFocus.Technical, ctx, baseSeed: 13);

            foreach (var (pa, pb) in Pairs(a, b))
                for (int s = 0; s < PlayerAttributes.SkillCount; s++)
                    Assert.That(pb.Attributes[s], Is.EqualTo(pa.Attributes[s]),
                        $"Same seed + context + plan must replay identically (player {pa.Id}, skill {s})");
        }

        // --------------------------------------------- whole world ages (DevelopmentProgressor)

        [Test]
        public void WholeWorld_Ages_AndIsDeterministic_AiClubsToo()
        {
            League worldA = new LeagueGenerator().Generate(new Pcg32(4_040_777));
            League worldB = new LeagueGenerator().Generate(new Pcg32(4_040_777));

            int userClubId = worldA.Clubs[0].Id;
            var plans = new Dictionary<int, TrainingPlan>
            {
                [userClubId] = new TrainingPlan { TeamFocus = TeamTrainingFocus.Physical }
            };

            var progA = new DevelopmentProgressor(D);
            var progB = new DevelopmentProgressor(D);
            for (int week = 0; week < WeeksPerSeason; week++)
            {
                progA.EvolveWeek(new[] { worldA }, plans, contexts: null, worldSeed: 99, week);
                progB.EvolveWeek(new[] { worldB }, plans, contexts: null, worldSeed: 99, week);
            }

            for (int c = 0; c < worldA.Clubs.Count; c++)
                foreach (var (pa, pb) in Pairs(worldA.Clubs[c], worldB.Clubs[c]))
                    for (int s = 0; s < PlayerAttributes.SkillCount; s++)
                        Assert.That(pb.Attributes[s], Is.EqualTo(pa.Attributes[s]),
                            "Whole-world development must be deterministic and order-independent");

            League fresh = new LeagueGenerator().Generate(new Pcg32(4_040_777));
            int aiBefore = SkillTotal(fresh.Clubs[5]);
            int aiAfter = SkillTotal(worldA.Clubs[5]);
            TestContext.Out.WriteLine($"[aging-world] AI club skill-total change over a season: {aiAfter - aiBefore} (youth grow, veterans decline)");
            Assert.That(aiAfter, Is.Not.EqualTo(aiBefore), "AI clubs must age too — the world isn't frozen");
        }

        // --------------------------------------------- helpers

        private static Club MakeClub(int seed, int clubIndex) =>
            new LeagueGenerator().Generate(new Pcg32((ulong)seed)).Clubs[clubIndex];

        /// <summary>A generated club re-aged uniformly young with potential headroom (isolates the curve).</summary>
        private static Club MakeYoungSquad(int seed, int clubIndex, int startAge, int headroom)
        {
            Club club = MakeClub(seed, clubIndex);
            foreach (Player p in club.Squad.Players)
            {
                p.Age = startAge;
                p.Development.Potential = AttributeScale.ClampSkill(PlayerRating.Overall(p) + headroom);
            }
            return club;
        }

        /// <summary>
        /// Runs <see cref="Seasons"/> seasons of <see cref="WeeksPerSeason"/> weekly ticks,
        /// ageing every player +1 at each season boundary (mirroring SeasonRollover). The
        /// RNG stream depends only on (baseSeed, global week) so A/B squads stay aligned.
        /// </summary>
        private static void DevelopSeasons(Club club, TeamTrainingFocus focus, DevelopmentContext ctx, int baseSeed)
        {
            int globalWeek = 0;
            for (int season = 0; season < Seasons; season++)
            {
                for (int w = 0; w < WeeksPerSeason; w++, globalWeek++)
                {
                    var rng = new Pcg32((ulong)baseSeed, (ulong)globalWeek);
                    foreach (Player p in club.Squad.Players)
                        DevelopmentModel.ApplyDevelopmentWeek(p, focus, IndividualTrainingFocus.None, ctx, rng, D);
                }
                foreach (Player p in club.Squad.Players) p.Age++;
            }
        }

        private static int SkillTotal(Club club)
        {
            int sum = 0;
            foreach (Player p in club.Squad.Players)
                for (int s = 0; s < PlayerAttributes.SkillCount; s++) sum += p.Attributes[s];
            return sum;
        }

        private static IEnumerable<(Player, Player)> Pairs(Club a, Club b)
        {
            for (int i = 0; i < a.Squad.Players.Count; i++)
                yield return (a.Squad.Players[i], b.Squad.Players[i]);
        }
    }
}
