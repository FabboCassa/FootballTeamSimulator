using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Scouting;

namespace Sim.Core.Tests.Scouting
{
    /// <summary>
    /// Task 11.2 acceptance: the scouting NETWORK — scouts sent to areas, not to players.
    ///
    /// THE ✅ — send one scout to a single club and another to a whole continent: after a few weeks
    /// the club scout's players are read accurately while the continental scout returns a broad list
    /// of rough candidates matching the filters we set, and a named direct assignment still narrows
    /// one player fast.
    ///
    /// Around it, the guarantees that make the feature honest rather than merely present:
    /// precision falls off with breadth and is CAPPED there (no amount of patience turns a
    /// continental brief into a club one), area knowledge lifts that cap so patience is still
    /// rewarded, the scout's own attributes matter and are neutral by default (so every task 5.4
    /// number is unchanged), the discovery filters read the SCOUT'S estimate and never the truth,
    /// the whole thing is deterministic, the shortlist is bounded, and a wide brief never erases
    /// what a narrow one already learned.
    ///
    /// Nothing here is called by the engine, so the golden master is unaffected.
    /// </summary>
    [TestFixture]
    public class AreaScoutingTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static ScoutingBalance S => Cfg.Scouting;
        private const ulong WorldSeed = 20260820UL;
        private const int ScoutLevel = 3;

        // ------------------------------------------------------- THE ✅

        [Test]
        public void ClubBrief_ReadsAccurately_WhileContinentalBrief_StaysBroadAndRough()
        {
            World world = SmallWorld();
            Club me = UserClub(world);
            Club target = TargetClub(world, me);

            var book = new ScoutingAssignmentBook();
            var knowledge = new KnowledgeStore();
            var areas = new AreaKnowledgeStore();
            var reports = new ScoutingReportBook();
            var progressor = new ScoutingProgressor(S);

            book.AddAssignment(me.Id, Brief(1, ScoutingArea.ForClub(target.Id)));
            book.AddAssignment(me.Id, Brief(2, ScoutingArea.ForContinent(Continent.SouthAmerica)));

            for (int week = 1; week <= 8; week++)
                progressor.EvolveAreaWeek(world, me, book, knowledge, areas, reports, WorldSeed, week);

            string clubKey = ScoutingArea.ForClub(target.Id).Key;
            string continentKey = ScoutingArea.ForContinent(Continent.SouthAmerica).Key;

            List<int> fromClub = reports.PlayersOfArea(me.Id, clubKey);
            List<int> fromContinent = reports.PlayersOfArea(me.Id, continentKey);

            Assert.That(fromClub.Count, Is.GreaterThan(0), "The club scout must bring back names");
            Assert.That(fromContinent.Count, Is.GreaterThan(0), "The continental scout must bring back names");
            Assert.That(fromContinent.Count, Is.GreaterThanOrEqualTo(fromClub.Count),
                "A wider brief surfaces MORE names, not fewer — that is the trade for the lower precision");

            double clubKnown = AverageKnowledge(me.Id, fromClub, knowledge);
            double continentKnown = AverageKnowledge(me.Id, fromContinent, knowledge);

            // The band is measured on a NEUTRAL YARDSTICK player — a true 55, mid-scale — read at
            // each brief's average knowledge, NOT on the real players' own bands.
            //
            // That distinction is the whole reason this assertion is trustworthy. A continental
            // brief ranks by estimated overall, so it surfaces the very BEST players on the
            // continent, whose bands are squeezed by the [1, 100] clamp at the top of the scale
            // (a 95-overall player at ±18 reads [77, 100], not [77, 113]). A club brief takes an
            // ordinary squad and gets the full width. Comparing those two averages measures the
            // clamp, not the scouting: the first version of this test did exactly that and made
            // the CONTINENTAL scout look twice as precise as the club one. The yardstick removes
            // the clamp and leaves only the mechanism, which is knowledge.
            int clubBand = YardstickWidth(clubKnown);
            int continentBand = YardstickWidth(continentKnown);

            TestContext.Out.WriteLine(
                $"[11.2-area] after 8 weeks — club brief: {fromClub.Count} names, avg knowledge {clubKnown:F1} " +
                $"⇒ band {clubBand} on a mid-scale player (their own bands avg {AverageOverallWidth(world, me.Id, fromClub, knowledge):F1}); " +
                $"continent brief: {fromContinent.Count} names, avg knowledge {continentKnown:F1} ⇒ band {continentBand} " +
                $"(their own bands avg {AverageOverallWidth(world, me.Id, fromContinent, knowledge):F1}, squeezed by the top-of-scale clamp)");

            Assert.That(clubKnown, Is.GreaterThan(continentKnown),
                "The club scout must KNOW his players better than the continental scout knows his");
            Assert.That(clubBand, Is.LessThan(continentBand),
                "…which means a tighter band on a like-for-like player");

            // And the continental read is CAPPED, not merely slower: no player on that brief can be
            // known better than the breadth ceiling allows at the area knowledge reached so far.
            int cap = ScoutingModel.KnowledgeCap(
                ScoutingAreaKind.Continent, areas.Get(me.Id, continentKey), S);
            foreach (int playerId in fromContinent)
                Assert.That(knowledge.Get(me.Id, playerId), Is.LessThanOrEqualTo(cap),
                    "A continental brief cannot push a player past its ceiling");
        }

        [Test]
        public void NamedTarget_StillNarrowsOnePlayerFast()
        {
            World world = SmallWorld();
            Club me = UserClub(world);
            Club target = TargetClub(world, me);
            Player him = target.Squad.Players[0];

            var book = new ScoutingAssignmentBook();
            var knowledge = new KnowledgeStore();
            var progressor = new ScoutingProgressor(S);

            book.Assign(me.Id, him.Id);
            Assert.That(book.IsWatching(me.Id, him.Id), Is.True, "The pre-11.2 direct watch API still works");

            // A named target rides the whole-world weekly tick exactly as it did in task 5.4b.
            var playable = new List<League>(world.PlayableLeagues());
            for (int week = 0; week < 8; week++)
                progressor.EvolveWeek(playable, knowledge, book);

            int known = knowledge.Get(me.Id, him.Id);
            int wide = ScoutingModel.Report(him, 0, WorldSeed, me.Id, S).Overall.Width;
            int tight = ScoutingModel.Report(him, known, WorldSeed, me.Id, S).Overall.Width;

            TestContext.Out.WriteLine($"[11.2-direct] 8 weeks on one name: knowledge {known}, band {wide} → {tight}");

            Assert.That(known, Is.GreaterThanOrEqualTo(8 * ScoutLevel * S.KnowledgePerScoutLevelPerWeek / 2),
                "A named target is the precise option: it must move fast");
            Assert.That(tight, Is.LessThan(wide));
        }

        // ------------------------------------------------------- precision vs breadth

        [Test]
        public void KnowledgeCeiling_FallsOffWithBreadth()
        {
            int player = ScoutingModel.KnowledgeCap(ScoutingAreaKind.Player, 0, S);
            int club = ScoutingModel.KnowledgeCap(ScoutingAreaKind.Club, 0, S);
            int nation = ScoutingModel.KnowledgeCap(ScoutingAreaKind.Nation, 0, S);
            int continent = ScoutingModel.KnowledgeCap(ScoutingAreaKind.Continent, 0, S);

            Assert.That(player, Is.EqualTo(S.MaxKnowledge), "A named target has no ceiling");
            Assert.That(club, Is.LessThan(player));
            Assert.That(nation, Is.LessThan(club));
            Assert.That(continent, Is.LessThan(nation));
        }

        [Test]
        public void AreaKnowledge_LiftsTheCeiling_ButNeverPastANarrowerBrief()
        {
            int cold = ScoutingModel.KnowledgeCap(ScoutingAreaKind.Continent, 0, S);
            int warm = ScoutingModel.KnowledgeCap(ScoutingAreaKind.Continent, S.MaxAreaKnowledge / 2, S);
            int home = ScoutingModel.KnowledgeCap(ScoutingAreaKind.Continent, S.MaxAreaKnowledge, S);

            Assert.That(warm, Is.GreaterThan(cold), "Seasons spent in a region must pay off");
            Assert.That(home, Is.GreaterThan(warm));
            Assert.That(home, Is.LessThanOrEqualTo(ScoutingModel.KnowledgeCap(ScoutingAreaKind.Club, 0, S)),
                "However well you know a continent, a scout parked on one club still reads it better");

            // Monotonic all the way up, for every breadth.
            foreach (ScoutingAreaKind kind in new[]
                     { ScoutingAreaKind.Club, ScoutingAreaKind.Nation, ScoutingAreaKind.Continent })
            {
                int previous = -1;
                for (int area = 0; area <= S.MaxAreaKnowledge; area += 5)
                {
                    int cap = ScoutingModel.KnowledgeCap(kind, area, S);
                    Assert.That(cap, Is.GreaterThanOrEqualTo(previous), $"{kind} ceiling must never fall (area={area})");
                    Assert.That(cap, Is.LessThanOrEqualTo(S.MaxKnowledge));
                    previous = cap;
                }
            }
        }

        [Test]
        public void WideBrief_NeverErases_WhatANarrowOneLearned()
        {
            int cap = ScoutingModel.KnowledgeCap(ScoutingAreaKind.Continent, 0, S);
            int alreadyKnown = S.MaxKnowledge; // a club scout finished the job on this player

            int after = ScoutingModel.AccrueInArea(
                alreadyKnown, ScoutLevel, ScoutingAreaKind.Continent, cap, ScoutQuality.Neutral, S);

            Assert.That(after, Is.EqualTo(alreadyKnown),
                "A continental glance at a player we already know well must leave that knowledge alone");
        }

        [Test]
        public void AreaAccrual_IsSlowerTheWiderTheBrief_AndNeverStalls()
        {
            int club = ScoutingModel.AccrueInArea(0, ScoutLevel, ScoutingAreaKind.Club, S.MaxKnowledge, ScoutQuality.Neutral, S);
            int nation = ScoutingModel.AccrueInArea(0, ScoutLevel, ScoutingAreaKind.Nation, S.MaxKnowledge, ScoutQuality.Neutral, S);
            int continent = ScoutingModel.AccrueInArea(0, ScoutLevel, ScoutingAreaKind.Continent, S.MaxKnowledge, ScoutQuality.Neutral, S);

            Assert.That(club, Is.GreaterThan(nation));
            Assert.That(nation, Is.GreaterThan(continent));

            // Even the worst case — the weakest scout, the widest brief, the least adaptable man —
            // makes SOME progress each week: integer division must never freeze him at zero.
            var homebody = new ScoutQuality(100, 100, 1);
            int crawl = ScoutingModel.AccrueInArea(0, 1, ScoutingAreaKind.Continent, S.MaxKnowledge, homebody, S);
            Assert.That(crawl, Is.GreaterThan(0), "A working scout always learns something");
        }

        // ------------------------------------------------------- the individual scout

        [Test]
        public void NeutralScout_ReadsExactlyLikeThePre112Department()
        {
            Player p = Make(1, skill: 55, potential: 78);
            var neutralScout = new Scout { Id = 1, Level = ScoutLevel }; // all attributes at their default

            for (int k = 0; k <= S.MaxKnowledge; k += 5)
            {
                PlayerScoutReport old = ScoutingModel.Report(p, k, WorldSeed, 7, S);
                PlayerScoutReport through = ScoutingModel.Report(p, k, WorldSeed, 7, S, ScoutQuality.Of(neutralScout, S));

                Assert.That(Serialize(through), Is.EqualTo(Serialize(old)),
                    $"A default scout must reproduce the task 5.4 numbers exactly (k={k})");
            }

            ScoutQuality q = ScoutQuality.Of(neutralScout, S);
            Assert.That(q.AbilityPercent, Is.EqualTo(100));
            Assert.That(q.PotentialPercent, Is.EqualTo(100));
            Assert.That(q.SpeedPercent, Is.EqualTo(100));
        }

        [Test]
        public void BetterJudge_ReadsTighter_AndTheTwoJudgementsAreIndependent()
        {
            Player p = Make(1, skill: 55, potential: 78);
            const int k = 40;

            var poor = new Scout { Id = 1, Level = ScoutLevel, JudgingAbility = 5, JudgingPotential = 5 };
            var great = new Scout { Id = 2, Level = ScoutLevel, JudgingAbility = 95, JudgingPotential = 95 };

            int poorWidth = ScoutingModel.Report(p, k, WorldSeed, 7, S, ScoutQuality.Of(poor, S)).Overall.Width;
            int greatWidth = ScoutingModel.Report(p, k, WorldSeed, 7, S, ScoutQuality.Of(great, S)).Overall.Width;

            TestContext.Out.WriteLine($"[11.2-judging] at knowledge {k}: poor judge band {poorWidth}, great judge band {greatWidth}");
            Assert.That(greatWidth, Is.LessThan(poorWidth), "A better judge of ability reads a tighter band");

            // Independent dials: a fine judge of ability who cannot read a youngster.
            var lopsided = new Scout { Id = 3, Level = ScoutLevel, JudgingAbility = 95, JudgingPotential = 5 };
            PlayerScoutReport report = ScoutingModel.Report(p, k, WorldSeed, 7, S, ScoutQuality.Of(lopsided, S));
            PlayerScoutReport allRound = ScoutingModel.Report(p, k, WorldSeed, 7, S, ScoutQuality.Of(great, S));

            Assert.That(report.Overall.Width, Is.EqualTo(allRound.Overall.Width),
                "Judging potential must not touch the ability band");
            Assert.That(report.Potential.Width, Is.GreaterThan(allRound.Potential.Width),
                "…but it must widen the potential band");
        }

        [Test]
        public void AdaptableScout_WorksAWideBriefFaster()
        {
            var homebody = new Scout { Id = 1, Level = ScoutLevel, Adaptability = 5 };
            var traveller = new Scout { Id = 2, Level = ScoutLevel, Adaptability = 95 };

            int cap = S.MaxKnowledge;
            int slow = ScoutingModel.AccrueInArea(0, ScoutLevel, ScoutingAreaKind.Continent, cap, ScoutQuality.Of(homebody, S), S);
            int fast = ScoutingModel.AccrueInArea(0, ScoutLevel, ScoutingAreaKind.Continent, cap, ScoutQuality.Of(traveller, S), S);

            Assert.That(fast, Is.GreaterThan(slow), "Adaptability is what makes a continent survivable");
        }

        [Test]
        public void TrueValue_StaysInsideTheBand_ThroughAnyScoutsEyes()
        {
            // The anti-frustration guarantee of task 5.4 must survive the two new dials: the scout
            // only ever moves WHICH knowledge level the band is built at, and the band always
            // contains the truth at every level.
            var qualities = new[]
            {
                ScoutQuality.Neutral,
                ScoutQuality.Of(new Scout { JudgingAbility = 1, JudgingPotential = 1, Adaptability = 1 }, S),
                ScoutQuality.Of(new Scout { JudgingAbility = 100, JudgingPotential = 100, Adaptability = 100 }, S)
            };

            for (int trueVal = 1; trueVal <= 100; trueVal += 3)
            {
                Player p = Make(2, trueVal, potential: trueVal);
                foreach (ScoutQuality q in qualities)
                {
                    for (int k = 0; k <= S.MaxKnowledge; k += 10)
                    {
                        PlayerScoutReport r = ScoutingModel.Report(p, k, WorldSeed, 5, S, q);
                        foreach (ScoutedRange a in r.Attributes)
                        {
                            Assert.That(a.Min, Is.LessThanOrEqualTo(trueVal));
                            Assert.That(a.Max, Is.GreaterThanOrEqualTo(trueVal));
                        }

                        Assert.That(r.Overall.Min, Is.LessThanOrEqualTo(trueVal));
                        Assert.That(r.Overall.Max, Is.GreaterThanOrEqualTo(trueVal));
                        Assert.That(r.Potential.Min, Is.LessThanOrEqualTo(trueVal));
                        Assert.That(r.Potential.Max, Is.GreaterThanOrEqualTo(trueVal));
                    }
                }
            }
        }

        [Test]
        public void Report_CarriesRawKnowledge_NotTheScoutAdjustedOne()
        {
            // The "62% scouted" bar must mean weeks of work. If it moved with the current scout's
            // eyesight, reassigning a scout would look like the club had FORGOTTEN the player.
            Player p = Make(1, 55, 78);
            var great = new Scout { Id = 1, JudgingAbility = 95, JudgingPotential = 95 };

            PlayerScoutReport r = ScoutingModel.Report(p, 40, WorldSeed, 7, S, ScoutQuality.Of(great, S));
            Assert.That(r.Knowledge, Is.EqualTo(40));
        }

        // ------------------------------------------------------- discovery

        [Test]
        public void EverySquadSizeThePresetsUse_HoldsAStriker()
        {
            // Task 11.2 leans on this and task 11.1 got it wrong. A brief can filter by ROLE, and
            // which roles exist in the world outside the playable nations is decided entirely by
            // SquadTemplate.For. Largest-remainder allocation on its own gave the 5-man (Small) and
            // 7-man (Medium — the shipped default) data-only squads the heaviest roles only, so
            // there was not a single striker anywhere outside the playable and background leagues:
            // "find me a striker in South America" could never come back with a name, for ever.
            foreach (int size in new[] { 2, 5, 7, 11, 18, 20, SquadTemplate.TotalPlayers })
            {
                (PositionRole Role, int Count)[] template = SquadTemplate.For(size);

                int total = 0;
                foreach ((PositionRole _, int count) in template) total += count;

                Assert.That(total, Is.EqualTo(size), $"For({size}) must add up to {size}");
                Assert.That(CountOf(template, PositionRole.Goalkeeper), Is.GreaterThan(0),
                    $"For({size}) must field a keeper");
                Assert.That(CountOf(template, PositionRole.Striker), Is.GreaterThan(0),
                    $"For({size}) must field a shooter — role filters depend on it");
            }

            // The sizes the presets use for BACKGROUND clubs, and the Large database's data-only
            // clubs, must be untouched by the 11.2 fix: those compositions were already correct and
            // changing them would silently reshape every world generated since 11.1.
            Assert.That(SquadTemplate.For(SquadTemplate.TotalPlayers), Is.EqualTo(SquadTemplate.Default));
            AssertComposition(11, "GK1 CB2 FB2 DM1 CM2 AM1 W1 ST1");
            AssertComposition(18, "GK2 CB3 FB3 DM2 CM2 AM2 W2 ST2");
            AssertComposition(20, "GK2 CB3 FB3 DM2 CM3 AM2 W3 ST2");
            AssertComposition(5, "GK1 CB1 CM1 W1 ST1");
            AssertComposition(7, "GK1 CB1 FB1 CM1 AM1 W1 ST1");
        }

        [Test]
        public void Discovery_ReturnsOnlyPlayersMatchingTheBrief()
        {
            World world = SmallWorld();
            Club me = UserClub(world);

            var filters = new ScoutingFilters
            {
                Role = (int)PositionRole.Striker,
                MaxAge = 24,
                MinAbility = 20
            };

            var assignment = new ScoutingAssignment
            {
                ScoutId = 1,
                Area = ScoutingArea.ForContinent(Continent.SouthAmerica),
                Filters = filters
            };

            // How many players could possibly match on the PUBLIC facts — so a zero result reads as
            // "the generated world holds none" rather than "the filter is broken".
            int eligible = assignment.Area.Clubs(world)
                .Where(c => c.Id != me.Id)
                .SelectMany(c => c.Squad.Players)
                .Count(p => p.Role == PositionRole.Striker && p.Age <= 24);
            Assert.That(eligible, Is.GreaterThan(0), "The test world must hold young South American strikers");

            List<ScoutingDiscovery.Candidate> found = ScoutingDiscovery.Scan(
                world, assignment, new KnowledgeStore(), WorldSeed, me.Id, ScoutQuality.Neutral, 20, S);

            Assert.That(found.Count, Is.GreaterThan(0), "South America must hold some young strikers");
            Assert.That(found.Count, Is.LessThanOrEqualTo(20), "The scan honours its result cap");

            foreach (ScoutingDiscovery.Candidate c in found)
            {
                Player? p = world.FindPlayer(c.PlayerId);
                Assert.That(p, Is.Not.Null);
                Assert.That(p!.Role, Is.EqualTo(PositionRole.Striker), "The brief asked for strikers");
                Assert.That(p.Age, Is.LessThanOrEqualTo(24), "The brief asked for under-25s");
                Assert.That(c.EstimatedOverall, Is.GreaterThanOrEqualTo(20), "…and for a minimum ability");
                Assert.That(c.ClubId, Is.Not.EqualTo(me.Id), "A scout is not sent to look at our own squad");
            }

            // Best first, by what the SCOUT believes.
            for (int i = 1; i < found.Count; i++)
                Assert.That(found[i].EstimatedOverall, Is.LessThanOrEqualTo(found[i - 1].EstimatedOverall));

            TestContext.Out.WriteLine($"[11.2-discovery] {found.Count} young South American strikers found by filter");
        }

        [Test]
        public void Discovery_RanksOnTheScoutsEstimate_NeverOnTheTruth()
        {
            World world = SmallWorld();
            Club me = UserClub(world);
            Club target = TargetClub(world, me);

            var assignment = new ScoutingAssignment { ScoutId = 1, Area = ScoutingArea.ForClub(target.Id) };
            var knowledge = new KnowledgeStore();

            List<ScoutingDiscovery.Candidate> found = ScoutingDiscovery.Scan(
                world, assignment, knowledge, WorldSeed, me.Id, ScoutQuality.Neutral, 50, S);

            Assert.That(found.Count, Is.GreaterThan(0));

            int offCentre = 0;
            foreach (ScoutingDiscovery.Candidate c in found)
            {
                Player p = world.FindPlayer(c.PlayerId)!;
                int expected = ScoutingModel
                    .OverallOf(p, knowledge.Get(me.Id, p.Id), WorldSeed, me.Id, S, ScoutQuality.Neutral).Estimate;

                Assert.That(c.EstimatedOverall, Is.EqualTo(expected),
                    "The number the shortlist ranks on is the scouted estimate, not the true overall");

                if (c.EstimatedOverall != PlayerRating.Overall(p))
                    offCentre++;
            }

            Assert.That(offCentre, Is.GreaterThan(0),
                "At zero knowledge the scout's numbers must differ from the truth for at least some players");
        }

        [Test]
        public void Discovery_IsDeterministic()
        {
            World world = SmallWorld();
            Club me = UserClub(world);

            var assignment = new ScoutingAssignment
            {
                ScoutId = 1,
                Area = ScoutingArea.ForNation("ITA"),
                Filters = new ScoutingFilters { MinPotential = 40 }
            };

            string a = Ids(ScoutingDiscovery.Scan(world, assignment, new KnowledgeStore(), WorldSeed, me.Id, ScoutQuality.Neutral, 30, S));
            string b = Ids(ScoutingDiscovery.Scan(world, assignment, new KnowledgeStore(), WorldSeed, me.Id, ScoutQuality.Neutral, 30, S));

            Assert.That(b, Is.EqualTo(a), "The same brief on the same world always returns the same list");
        }

        [Test]
        public void EmptyBrief_MatchesEveryone_AndAnImpossibleOneMatchesNobody()
        {
            World world = SmallWorld();
            Club me = UserClub(world);
            Club target = TargetClub(world, me);

            var open = new ScoutingAssignment { ScoutId = 1, Area = ScoutingArea.ForClub(target.Id) };
            var impossible = new ScoutingAssignment
            {
                ScoutId = 1,
                Area = ScoutingArea.ForClub(target.Id),
                Filters = new ScoutingFilters { MinAbility = 101 }
            };

            Assert.That(open.Filters.IsEmpty, Is.True);
            Assert.That(
                ScoutingDiscovery.Scan(world, open, new KnowledgeStore(), WorldSeed, me.Id, ScoutQuality.Neutral, 100, S).Count,
                Is.EqualTo(target.Squad.Players.Count),
                "An empty brief brings back the whole squad");
            Assert.That(
                ScoutingDiscovery.Scan(world, impossible, new KnowledgeStore(), WorldSeed, me.Id, ScoutQuality.Neutral, 100, S),
                Is.Empty,
                "A brief nobody can satisfy returns nothing rather than something wrong");
        }

        // ------------------------------------------------------- the book, the stores, the save

        [Test]
        public void Shortlist_IsBoundedPerBrief_AndStopsRatherThanChurning()
        {
            World world = SmallWorld();
            Club me = UserClub(world);

            var book = new ScoutingAssignmentBook();
            var knowledge = new KnowledgeStore();
            var areas = new AreaKnowledgeStore();
            var reports = new ScoutingReportBook();
            var progressor = new ScoutingProgressor(S);

            var area = ScoutingArea.ForContinent(Continent.SouthAmerica);
            book.AddAssignment(me.Id, Brief(1, area));

            var firstNames = new List<int>();
            for (int week = 1; week <= 60; week++)
            {
                progressor.EvolveAreaWeek(world, me, book, knowledge, areas, reports, WorldSeed, week);
                if (week == 1)
                    firstNames.AddRange(reports.PlayersOfArea(me.Id, area.Key));
            }

            List<int> finalNames = reports.PlayersOfArea(me.Id, area.Key);

            Assert.That(finalNames.Count, Is.EqualTo(S.MaxReportsPerArea),
                "A long-running brief fills its shortlist and stops there");
            Assert.That(reports.Count(me.Id), Is.LessThanOrEqualTo(S.MaxReportsPerClub));

            foreach (int early in firstNames)
                Assert.That(finalNames, Does.Contain(early),
                    "The names found first must NOT be quietly evicted by later, worse ones");

            // Making room lets the scout work again.
            reports.Remove(me.Id, finalNames[0]);
            progressor.EvolveAreaWeek(world, me, book, knowledge, areas, reports, WorldSeed, 61);
            Assert.That(reports.CountOfArea(me.Id, area.Key), Is.EqualTo(S.MaxReportsPerArea),
                "Dismissing a report frees a slot the scout immediately fills");
        }

        [Test]
        public void AssignmentBook_OneBriefPerScout_AndCancellingKeepsTheKnowledge()
        {
            var book = new ScoutingAssignmentBook();
            const int clubId = 42;

            Assert.That(book.AddAssignment(clubId, Brief(1, ScoutingArea.ForNation("ITA"))), Is.False,
                "Filing the first brief replaces nothing");
            Assert.That(book.CountFor(clubId), Is.EqualTo(1));

            Assert.That(book.AddAssignment(clubId, Brief(1, ScoutingArea.ForNation("BRA"))), Is.True,
                "Sending the same scout elsewhere recalls him first");
            Assert.That(book.CountFor(clubId), Is.EqualTo(1), "One scout is never in two places");
            Assert.That(book.AssignmentOfScout(clubId, 1)!.Area.NationCode, Is.EqualTo("BRA"));

            book.AddAssignment(clubId, Brief(2, ScoutingArea.ForContinent(Continent.Africa)));
            Assert.That(book.CountFor(clubId), Is.EqualTo(2));

            Assert.That(book.RemoveAssignment(clubId, 1), Is.True);
            Assert.That(book.CountFor(clubId), Is.EqualTo(1));
            Assert.That(book.RemoveAssignment(clubId, 1), Is.False, "Recalling a scout who is home is a no-op");

            // Named watches live in the same book without disturbing the area briefs.
            book.Assign(clubId, 999);
            Assert.That(book.IsWatching(clubId, 999), Is.True);
            Assert.That(book.For(clubId), Is.EquivalentTo(new[] { 999 }),
                "For() still returns only the NAMED watches — the weekly tick must not see area briefs");
            Assert.That(book.CountFor(clubId), Is.EqualTo(2));
            Assert.That(book.HasAny(clubId), Is.True);

            book.Unassign(clubId, 999);
            Assert.That(book.IsWatching(clubId, 999), Is.False);
            Assert.That(book.CountFor(clubId), Is.EqualTo(1), "Unwatching a name leaves the area brief alone");
        }

        [Test]
        public void AreaOnlyClub_DoesNotFallBackToTheAiPolicy()
        {
            // A club running ONLY area briefs has assignments but no named watches. The whole-world
            // tick must treat it as "explicitly assigned" (do nothing) rather than "unmanaged"
            // (scout the league's standouts behind the player's back).
            World world = SmallWorld();
            Club me = UserClub(world);

            var book = new ScoutingAssignmentBook();
            book.AddAssignment(me.Id, Brief(1, ScoutingArea.ForContinent(Continent.SouthAmerica)));

            var knowledge = new KnowledgeStore();
            var progressor = new ScoutingProgressor(S);
            var playable = new List<League>(world.PlayableLeagues());
            for (int week = 0; week < 4; week++)
                progressor.EvolveWeek(playable, knowledge, book);

            bool learnedAnything = knowledge.Export().Any(e => e.ClubId == me.Id && e.Knowledge > 0);
            Assert.That(learnedAnything, Is.False,
                "The weekly tick must not silently scout for a club whose scouts are all out on area briefs");
        }

        [Test]
        public void AreaKey_RoundTrips_ForEveryKind()
        {
            var areas = new[]
            {
                ScoutingArea.ForPlayer(1234),
                ScoutingArea.ForClub(4321),
                ScoutingArea.ForNation("BRA"),
                ScoutingArea.ForContinent(Continent.Africa)
            };

            foreach (ScoutingArea area in areas)
            {
                ScoutingArea? back = ScoutingArea.Parse(area.Key);
                Assert.That(back, Is.Not.Null, $"'{area.Key}' must parse back");
                Assert.That(back!.Key, Is.EqualTo(area.Key));
                Assert.That(back.Kind, Is.EqualTo(area.Kind));
                Assert.That(back.SameAs(area), Is.True);
            }

            Assert.That(ScoutingArea.Parse(null), Is.Null);
            Assert.That(ScoutingArea.Parse(string.Empty), Is.Null);
            Assert.That(ScoutingArea.Parse("nonsense"), Is.Null);
        }

        [Test]
        public void Stores_RoundTrip_ThroughExportAndImport()
        {
            World world = SmallWorld();
            Club me = UserClub(world);

            var book = new ScoutingAssignmentBook();
            var knowledge = new KnowledgeStore();
            var areas = new AreaKnowledgeStore();
            var reports = new ScoutingReportBook();
            var progressor = new ScoutingProgressor(S);

            book.AddAssignment(me.Id, Brief(1, ScoutingArea.ForNation("ITA")));
            book.AddAssignment(me.Id, Brief(2, ScoutingArea.ForContinent(Continent.SouthAmerica)));
            for (int week = 1; week <= 6; week++)
                progressor.EvolveAreaWeek(world, me, book, knowledge, areas, reports, WorldSeed, week);

            // Save → load.
            var areas2 = new AreaKnowledgeStore();
            foreach ((int clubId, string areaKey, int k) in areas.Export())
                areas2.Import(clubId, areaKey, k);

            var reports2 = new ScoutingReportBook();
            foreach ((int clubId, ScoutReportEntry entry) in reports.Export())
                reports2.Import(clubId, entry);

            var book2 = new ScoutingAssignmentBook();
            foreach ((int clubId, ScoutingAssignment assignment) in book.ExportAll())
                book2.ImportAssignment(clubId, assignment.Clone());

            Assert.That(areas2.Export().Count(), Is.EqualTo(areas.Export().Count()));
            Assert.That(areas2.Get(me.Id, ScoutingArea.ForNation("ITA").Key),
                Is.EqualTo(areas.Get(me.Id, ScoutingArea.ForNation("ITA").Key)));

            Assert.That(reports2.Count(me.Id), Is.EqualTo(reports.Count(me.Id)));
            Assert.That(reports2.For(me.Id).Select(e => e.PlayerId).ToList(),
                Is.EqualTo(reports.For(me.Id).Select(e => e.PlayerId).ToList()));

            Assert.That(book2.CountFor(me.Id), Is.EqualTo(2));
            Assert.That(book2.AssignmentOfScout(me.Id, 2)!.Area.Continent, Is.EqualTo(Continent.SouthAmerica));
            Assert.That(book2.AssignmentOfScout(me.Id, 1)!.WeeksElapsed, Is.EqualTo(6));
        }

        [Test]
        public void AreaWeek_IsDeterministic_AndFreeOfRng()
        {
            World a = SmallWorld();
            World b = SmallWorld();

            string first = RunSixWeeks(a);
            string second = RunSixWeeks(b);

            Assert.That(second, Is.EqualTo(first), "Same world, same briefs ⇒ byte-identical scouting");
        }

        // ------------------------------------------------------- helpers

        private static string RunSixWeeks(World world)
        {
            Club me = UserClub(world);
            var book = new ScoutingAssignmentBook();
            var knowledge = new KnowledgeStore();
            var areas = new AreaKnowledgeStore();
            var reports = new ScoutingReportBook();
            var progressor = new ScoutingProgressor(S);

            book.AddAssignment(me.Id, Brief(1, ScoutingArea.ForClub(TargetClub(world, me).Id)));
            book.AddAssignment(me.Id, Brief(2, ScoutingArea.ForContinent(Continent.SouthAmerica)));

            for (int week = 1; week <= 6; week++)
                progressor.EvolveAreaWeek(world, me, book, knowledge, areas, reports, WorldSeed, week);

            var sb = new System.Text.StringBuilder();
            foreach (ScoutReportEntry entry in reports.For(me.Id))
            {
                sb.Append(entry.AreaKey).Append(':').Append(entry.PlayerId).Append('@').Append(entry.FoundWeek)
                  .Append('=').Append(knowledge.Get(me.Id, entry.PlayerId)).Append('|');
            }

            foreach ((int clubId, string areaKey, int k) in areas.Export().OrderBy(e => e.AreaKey))
                sb.Append(clubId).Append('#').Append(areaKey).Append('=').Append(k).Append('|');

            return sb.ToString();
        }

        private static ScoutingAssignment Brief(int scoutId, ScoutingArea area, ScoutingFilters? filters = null)
            => new ScoutingAssignment
            {
                ScoutId = scoutId,
                Area = area,
                Filters = filters ?? new ScoutingFilters()
            };

        private static World SmallWorld()
        {
            var options = new WorldGenerationOptions
            {
                Scope = new WorldScope
                {
                    Size = DatabaseSize.Small,
                    Playable = { new PlayableNation { Code = "ITA", PlayableTiers = 1 } }
                }
            };

            return new WorldGenerator(options).Generate(WorldSeed);
        }

        /// <summary>The user's club, staffed with two scouts (ids 1 and 2) at the default attributes.</summary>
        private static Club UserClub(World world)
        {
            Club club = world.PlayableLeagues()[0].Clubs[0];
            club.Scouts = new List<Scout>
            {
                new Scout { Id = 1, Name = "Chief Scout", Level = ScoutLevel },
                new Scout { Id = 2, Name = "Scout", Level = ScoutLevel }
            };

            return club;
        }

        private static Club TargetClub(World world, Club me)
        {
            foreach (Club club in world.PlayableLeagues()[0].Clubs)
            {
                if (club.Id != me.Id && club.Squad.Players.Count > 0)
                    return club;
            }

            throw new InvalidOperationException("The playable division must hold another club with a squad");
        }

        private static int CountOf((PositionRole Role, int Count)[] template, PositionRole role)
        {
            foreach ((PositionRole r, int count) in template)
            {
                if (r == role)
                    return count;
            }

            return 0;
        }

        /// <summary>Asserts a composition, written the way the test output reads: "GK1 CB2 ... ST1".</summary>
        private static void AssertComposition(int size, string expected)
        {
            var sb = new System.Text.StringBuilder();
            foreach ((PositionRole role, int count) in SquadTemplate.For(size))
            {
                if (count <= 0)
                    continue;

                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Abbrev(role)).Append(count);
            }

            Assert.That(sb.ToString(), Is.EqualTo(expected), $"SquadTemplate.For({size})");
        }

        private static string Abbrev(PositionRole role)
        {
            switch (role)
            {
                case PositionRole.Goalkeeper: return "GK";
                case PositionRole.CentreBack: return "CB";
                case PositionRole.FullBack: return "FB";
                case PositionRole.DefensiveMidfielder: return "DM";
                case PositionRole.CentralMidfielder: return "CM";
                case PositionRole.AttackingMidfielder: return "AM";
                case PositionRole.Winger: return "W";
                default: return "ST";
            }
        }

        private static double AverageKnowledge(int clubId, List<int> playerIds, KnowledgeStore knowledge)
        {
            if (playerIds.Count == 0)
                return 0d;

            long total = 0;
            foreach (int playerId in playerIds)
                total += knowledge.Get(clubId, playerId);

            return (double)total / playerIds.Count;
        }

        /// <summary>
        /// The band a mid-scale player (a true 55) reads at, at the given knowledge, with the bias
        /// zeroed. Clamp-free and selection-free: purely "how precisely can a brief at this
        /// knowledge read a typical footballer".
        /// </summary>
        private static int YardstickWidth(double knowledge)
            => ScoutingModel.Estimate(55, (int)knowledge, S.AttributeMaxHalfWidth, S.AttributeMinHalfWidth, 0, S).Width;

        private static double AverageOverallWidth(World world, int clubId, List<int> playerIds, KnowledgeStore knowledge)
        {
            if (playerIds.Count == 0)
                return 0d;

            long total = 0;
            foreach (int playerId in playerIds)
            {
                Player p = world.FindPlayer(playerId)!;
                total += ScoutingModel.Report(p, knowledge.Get(clubId, playerId), WorldSeed, clubId, S).Overall.Width;
            }

            return (double)total / playerIds.Count;
        }

        private static string Ids(List<ScoutingDiscovery.Candidate> candidates)
            => string.Join(",", candidates.Select(c => c.PlayerId + ":" + c.EstimatedOverall));

        // A player whose every attribute equals `skill` ⇒ Overall == skill (each role row sums to 100).
        private static Player Make(int id, int skill, int potential)
        {
            var p = new Player { Id = id, Role = PositionRole.FullBack, Age = 24 };
            for (int i = 0; i < PlayerAttributes.SkillCount; i++) p.Attributes[i] = skill;
            p.Development.Potential = potential;
            return p;
        }

        private static string Serialize(PlayerScoutReport r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(r.PlayerId).Append('|').Append(r.Knowledge)
              .Append('|').Append(r.Overall.Min).Append(',').Append(r.Overall.Max).Append(',').Append(r.Overall.Estimate)
              .Append('|').Append(r.Potential.Min).Append(',').Append(r.Potential.Max).Append(',').Append(r.Potential.Estimate);
            foreach (ScoutedRange a in r.Attributes)
                sb.Append('|').Append(a.Min).Append(',').Append(a.Max).Append(',').Append(a.Estimate);
            return sb.ToString();
        }
    }
}
