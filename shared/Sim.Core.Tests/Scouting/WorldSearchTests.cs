using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Scouting;

namespace Sim.Core.Tests.Scouting
{
    /// <summary>
    /// Task 11.3 acceptance: SEARCHING A WORLD OF TENS OF THOUSANDS OF PLAYERS.
    ///
    /// THE ✅ — with a Large database the scouting screen opens instantly and a search across the
    /// whole world returns in well under a second. That is the performance half, and it is what
    /// <see cref="WorldPlayerIndex"/> exists for: one flat pass over primitive arrays instead of a
    /// walk down nations → leagues → clubs → squads.
    ///
    /// The DESIGN half is the user's rule for what a search may show: you can look anywhere, but
    /// what you READ is governed by knowledge — except that famous players are public property. So
    /// these tests pin, in both directions:
    ///   • fame is derived from PUBLIC facts only (ability, nation, division, club, age) and never
    ///     from potential, so the search screen can never do a scout's real job — finding the boy
    ///     nobody has heard of;
    ///   • the free knowledge fame grants is a FLOOR, never a ceiling: scouting still improves a
    ///     famous player's read, and free knowledge stays a TAIL rather than a mass (the first run of
    ///     this fixture, 2026-08-21, is what caught it: at a threshold of 55 two thirds of a shipped
    ///     world read as "known" for nothing, and the balance number moved to 66);
    ///   • the index returns EXACTLY what the task 11.2 walk returned — same players, same order —
    ///     so switching the discovery scan onto it changes performance and nothing else.
    ///
    /// Nothing here is reachable from the match engine: the golden master is untouched.
    /// </summary>
    [TestFixture]
    public class WorldSearchTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static ScoutingBalance S => Cfg.Scouting;
        private const ulong WorldSeed = 20260821UL;
        private const int Observer = 999_001; // a club id no generated world uses

        // ------------------------------------------------------- fame: what the public already knows

        [Test]
        public void Fame_RisesWithTheSpotlight_AtEqualAbility()
        {
            int star = PublicKnowledge.Fame(overall: 78, age: 27, clubStrength: 80, division: 1, nationReputation: 90, S);
            int journeyman = PublicKnowledge.Fame(overall: 78, age: 27, clubStrength: 45, division: 3, nationReputation: 40, S);

            Assert.That(star, Is.GreaterThan(journeyman),
                "The same player is more famous in a big nation's top flight than in a small nation's third tier");
            Assert.That(PublicKnowledge.Floor(star, S), Is.GreaterThan(PublicKnowledge.Floor(journeyman, S)),
                "and that must translate into more free knowledge, not just a bigger number");
        }

        [Test]
        public void Fame_RisesWithAbility_InTheSameSurroundings()
        {
            int great = PublicKnowledge.Fame(92, 27, 70, 1, 80, S);
            int ordinary = PublicKnowledge.Fame(58, 27, 70, 1, 80, S);

            Assert.That(great, Is.GreaterThan(ordinary));
            Assert.That(PublicKnowledge.Floor(ordinary, S), Is.LessThan(PublicKnowledge.Floor(great, S)));
        }

        [Test]
        public void Fame_IgnoresPotential_SoTheSearchScreenCannotDoAScoutsJob()
        {
            // Two identical players, one a future great. Fame is computed from public facts only, so
            // the wonderkid is exactly as anonymous as his twin until somebody goes and watches him.
            Player ordinary = MakePlayer(1, age: 18, quality: 55, potential: 58);
            Player wonderkid = MakePlayer(2, age: 18, quality: 55, potential: 95);

            var club = new Club { Id = 10, Strength = 60 };
            var league = new League { Id = 1, Division = 1, NationCode = "ITA" };
            var nation = new Nation { Code = "ITA", Reputation = 80 };

            Assert.That(PublicKnowledge.FameOf(wonderkid, club, league, nation, S),
                Is.EqualTo(PublicKnowledge.FameOf(ordinary, club, league, nation, S)),
                "Potential must play NO part in fame — that is the whole reason scouts still exist");
        }

        [Test]
        public void Fame_LagsForTheVeryYoung()
        {
            int settled = PublicKnowledge.Fame(80, 26, 75, 1, 85, S);
            int teenager = PublicKnowledge.Fame(80, 17, 75, 1, 85, S);

            Assert.That(teenager, Is.LessThan(settled), "Reputation lags talent: a 17-year-old is not a household name yet");
        }

        [Test]
        public void PublicKnowledge_IsAFloorAndNeverACeiling()
        {
            int floor = PublicKnowledge.Floor(PublicKnowledge.Fame(90, 28, 80, 1, 90, S), S);
            Assert.That(floor, Is.GreaterThan(0), "A world-class player in a top league must be publicly known");

            Assert.That(PublicKnowledge.Effective(0, floor), Is.EqualTo(floor), "Nobody scouted him: fame is what you get");
            Assert.That(PublicKnowledge.Effective(S.MaxKnowledge, floor), Is.EqualTo(S.MaxKnowledge),
                "A club that has actually scouted him keeps its own, better knowledge");
            Assert.That(floor, Is.LessThan(S.MaxKnowledge),
                "Even the most famous player alive is 'known', not 'measured' — scouting him must still be worth doing");
        }

        [Test]
        public void AFamousPlayer_ReadsTighterThanAnAnonymousOne_WithoutAnyScouting()
        {
            // Measured on a NEUTRAL YARDSTICK player (a true 55, mid-scale) read at each knowledge
            // level, NOT on the two real players: selection by ability pushes a sample against the
            // [1, 100] clamp, which flatters its band width. That trap cost task 11.2 a red test.
            int famous = PublicKnowledge.Floor(PublicKnowledge.Fame(88, 28, 78, 1, 88, S), S);
            int anonymous = PublicKnowledge.Floor(PublicKnowledge.Fame(52, 22, 40, 3, 45, S), S);

            Assert.That(anonymous, Is.EqualTo(0), "A modest player in a small third division is nobody");

            Player yardstick = MakePlayer(1, age: 25, quality: 55, potential: 60);
            int famousBand = ScoutingModel.OverallOf(yardstick, famous, WorldSeed, Observer, S, ScoutQuality.Neutral).Width;
            int anonymousBand = ScoutingModel.OverallOf(yardstick, anonymous, WorldSeed, Observer, S, ScoutQuality.Neutral).Width;

            Assert.That(famousBand, Is.LessThan(anonymousBand),
                $"Fame must narrow the read without a scout (famous ±{famousBand / 2} vs anonymous ±{anonymousBand / 2})");
            TestContext.Out.WriteLine($"[11.3] free knowledge: famous {famous}/{S.MaxKnowledge} (band {famousBand}) vs anonymous {anonymous} (band {anonymousBand})");
        }

        [Test]
        public void PublicKnowledge_IsATailAndNotAMass()
        {
            // The user's rule for 11.3 is "you can look at anyone, but unless he has been watched you
            // will not see much — except the famous ones". That makes the SHAPE of this distribution
            // the acceptance criterion: free knowledge has to be a tail, not a mass.
            //
            // Measured on a LARGE database, and that is not a detail. A Small database is, by
            // construction, nothing but elite clubs: the playable nation plus the SPINES of top-flight
            // clubs in reputable nations. There is barely an obscure player in it to find, so
            // measuring anonymity there measures the preset, not the mechanic. The first version of
            // this test did exactly that and failed — and the finding was worth having: at the
            // original threshold of 55 even that elite world came out two thirds "known" for free,
            // which is what moved PublicFameThreshold to 66.
            World large = LargeWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(large, S);

            var floors = new List<int>(index.Count);
            for (int slot = 0; slot < index.Count; slot++)
                floors.Add(index.FloorAt(slot));

            Assert.That(floors.Count, Is.GreaterThan(0));
            floors.Sort();

            int median = floors[floors.Count / 2];
            int p90 = floors[floors.Count * 9 / 10];
            int p99 = floors[floors.Count * 99 / 100];
            int best = floors[floors.Count - 1];

            TestContext.Out.WriteLine("[11.3] LARGE  " + Distribution(floors));
            TestContext.Out.WriteLine("[11.3] SMALL  " + Distribution(FloorsOf(SmallWorld())));

            Assert.That(median, Is.LessThanOrEqualTo(S.MaxKnowledge / 10),
                $"The player in the middle of the database must still be someone you have to go and look at " +
                $"(median {median}, p90 {p90}, p99 {p99} of {S.MaxKnowledge})");
            Assert.That(best, Is.GreaterThanOrEqualTo(S.MaxKnowledge / 4),
                "…while the best player in the world must be readable without a scout, or the mechanic does nothing");
            Assert.That(best, Is.LessThan(S.MaxKnowledge),
                "…and never fully known: scouting him must still be worth doing");
        }

        private static List<int> FloorsOf(World world)
        {
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);
            var floors = new List<int>(index.Count);
            for (int slot = 0; slot < index.Count; slot++)
                floors.Add(index.FloorAt(slot));

            floors.Sort();
            return floors;
        }

        /// <summary>The line we read to tune the fame table, printed for both presets.</summary>
        private static string Distribution(List<int> floors)
        {
            int anonymous = 0, faint = 0, known = 0, household = 0;
            foreach (int floor in floors)
            {
                if (floor <= 0) anonymous++;
                else if (floor < S.MaxKnowledge / 4) faint++;
                else if (floor < S.MaxKnowledge / 2) known++;
                else household++;
            }

            return $"free knowledge over {floors.Count} players: none {anonymous} · faint {faint} · " +
                   $"known {known} · household {household} | median {floors[floors.Count / 2]} · " +
                   $"p90 {floors[floors.Count * 9 / 10]} · p99 {floors[floors.Count * 99 / 100]} · " +
                   $"max {floors[floors.Count - 1]} (of {S.MaxKnowledge})";
        }

        // ------------------------------------------------------- the index: same answers, faster

        [Test]
        public void Index_HoldsEveryPlayerInTheWorld()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);

            Assert.That(index.Count, Is.EqualTo(world.PlayerCount()));

            foreach (Nation nation in world.Nations)
                foreach (League league in nation.Leagues)
                    foreach (Club club in league.Clubs)
                        foreach (Player player in club.Squad.Players)
                            Assert.That(index.Holds(player.Id), Is.True, $"{player.FullName} is missing from the index");
        }

        [Test]
        public void Index_EnumeratesAnArea_InExactlyTheOrderTheWalkDid()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);

            foreach (ScoutingArea area in AreasToCheck(world))
            {
                List<int> walked = new List<int>();
                foreach (Club club in area.Clubs(world))
                    foreach (Player player in club.Squad.Players)
                        walked.Add(player.Id);

                List<int> indexed = new List<int>();
                List<int> bounds = index.Bounds(area);
                for (int b = 0; b < bounds.Count; b += 2)
                    for (int slot = bounds[b]; slot < bounds[b + 1]; slot++)
                        indexed.Add(index.PlayerAt(slot)!.Id);

                Assert.That(indexed, Is.EqualTo(walked), $"Area {area.Key} must enumerate identically");
            }
        }

        [Test]
        public void Discovery_ReturnsTheSameNames_WithAndWithoutTheIndex()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);
            var knowledge = new KnowledgeStore();
            Club me = world.PlayableLeagues()[0].Clubs[0];

            var filters = new ScoutingFilters { Role = (int)PositionRole.Striker, MaxAge = 26 };

            foreach (ScoutingArea area in AreasToCheck(world))
            {
                var brief = new ScoutingAssignment { ScoutId = 1, Area = area, Filters = filters };

                List<ScoutingDiscovery.Candidate> walked = ScoutingDiscovery.Scan(
                    world, brief, knowledge, WorldSeed, me.Id, ScoutQuality.Neutral, 40, S);
                List<ScoutingDiscovery.Candidate> indexed = ScoutingDiscovery.Scan(
                    world, brief, knowledge, WorldSeed, me.Id, ScoutQuality.Neutral, 40, S, index);

                Assert.That(indexed.Select(c => c.PlayerId).ToList(),
                    Is.EqualTo(walked.Select(c => c.PlayerId).ToList()),
                    $"The indexed scan of {area.Key} must return the walk's names, in the walk's order");
                Assert.That(indexed.Select(c => c.EstimatedOverall).ToList(),
                    Is.EqualTo(walked.Select(c => c.EstimatedOverall).ToList()),
                    "…and the same numbers");
            }
        }

        [Test]
        public void Discovery_NeverDiscoversTheNamedTargetItself()
        {
            // A Player brief is a direct observation, not a place to go looking. The task 11.2 walk
            // yields no clubs for it, and the index — which CAN address a single player — must not
            // quietly start filing the man himself as a fresh discovery.
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);
            Club me = world.PlayableLeagues()[0].Clubs[0];
            Player target = world.PlayableLeagues()[0].Clubs[1].Squad.Players[0];

            var brief = new ScoutingAssignment
            {
                ScoutId = 1,
                Area = ScoutingArea.ForPlayer(target.Id),
                Filters = new ScoutingFilters()
            };

            var knowledge = new KnowledgeStore();
            Assert.That(ScoutingDiscovery.Scan(world, brief, knowledge, WorldSeed, me.Id, ScoutQuality.Neutral, 10, S),
                Is.Empty, "the walk");
            Assert.That(ScoutingDiscovery.Scan(world, brief, knowledge, WorldSeed, me.Id, ScoutQuality.Neutral, 10, S, index),
                Is.Empty, "the indexed scan");
        }

        // ------------------------------------------------------- the search itself

        [Test]
        public void Search_RespectsTheFilters()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);

            var query = new PlayerSearchQuery
            {
                Filters = new ScoutingFilters { Role = (int)PositionRole.Striker, MaxAge = 24 },
                ObserverClubId = Observer,
                PageSize = 25
            };

            PlayerSearchPage page = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);

            Assert.That(page.Total, Is.GreaterThan(0), "The world must hold young strikers at all");
            foreach (PlayerSearchHit hit in page.Hits)
            {
                Assert.That(hit.Player.Role, Is.EqualTo(PositionRole.Striker));
                Assert.That(hit.Player.Age, Is.LessThanOrEqualTo(24));
            }
        }

        [Test]
        public void Search_RestrictedToAnArea_NeverLeavesIt()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);

            Nation nation = world.Nations[0];
            var query = new PlayerSearchQuery
            {
                Area = ScoutingArea.ForNation(nation.Code),
                ObserverClubId = Observer,
                PageSize = 50
            };

            PlayerSearchPage page = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);
            Assert.That(page.Total, Is.GreaterThan(0));

            var clubsOfNation = new HashSet<int>();
            foreach (League league in nation.Leagues)
                foreach (Club club in league.Clubs)
                    clubsOfNation.Add(club.Id);

            foreach (PlayerSearchHit hit in page.Hits)
                Assert.That(clubsOfNation.Contains(hit.ClubId), Is.True, $"{hit.Player.FullName} is not in {nation.Name}");
        }

        [Test]
        public void Search_PagesWithoutLosingOrRepeatingAnybody()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);

            var query = new PlayerSearchQuery { ObserverClubId = Observer, PageSize = 20 };
            PlayerSearchPage first = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);

            Assert.That(first.PageCount, Is.GreaterThan(1), "The test needs a world big enough to page");

            var seen = new List<int>();
            for (int p = 0; p < first.PageCount; p++)
            {
                query.Page = p;
                PlayerSearchPage page = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);
                Assert.That(page.Page, Is.EqualTo(p));
                seen.AddRange(page.Hits.Select(h => h.PlayerId));
            }

            Assert.That(seen.Count, Is.EqualTo(first.Total), "Every match must appear on exactly one page");
            Assert.That(seen.Distinct().Count(), Is.EqualTo(seen.Count), "…and only once");
        }

        [Test]
        public void Search_SortsByFameByDefault_AndTheOrderIsTotal()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);

            var query = new PlayerSearchQuery { ObserverClubId = Observer, PageSize = 30 };
            PlayerSearchPage page = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);

            for (int i = 1; i < page.Hits.Count; i++)
                Assert.That(page.Hits[i].Fame, Is.LessThanOrEqualTo(page.Hits[i - 1].Fame),
                    "The default page is the players a manager would plausibly have heard of, most famous first");

            // Total order ⇒ the same query twice is the same list, on any platform.
            PlayerSearchPage again = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);
            Assert.That(again.Hits.Select(h => h.PlayerId).ToList(),
                Is.EqualTo(page.Hits.Select(h => h.PlayerId).ToList()));
        }

        [Test]
        public void Search_FindsAPlayerByName()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);

            Player target = world.PlayableLeagues()[0].Clubs[1].Squad.Players[0];

            var query = new PlayerSearchQuery
            {
                Text = target.LastName.ToUpperInvariant(), // the search is case-insensitive
                ObserverClubId = Observer,
                PageSize = 100
            };

            PlayerSearchPage page = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);
            Assert.That(page.Hits.Any(h => h.PlayerId == target.Id), Is.True,
                $"Searching '{target.LastName}' must find {target.FullName}");
        }

        [Test]
        public void Search_LeavesYourOwnPlayersOut()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);
            Club me = world.PlayableLeagues()[0].Clubs[0];

            var query = new PlayerSearchQuery { ObserverClubId = me.Id, PageSize = 1000 };
            PlayerSearchPage page = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);

            foreach (PlayerSearchHit hit in page.Hits)
                Assert.That(hit.ClubId, Is.Not.EqualTo(me.Id), "You do not scout your own squad");
        }

        [Test]
        public void Search_ScoutedOnly_ReturnsOnlyWhatYourOwnScoutsHaveSeen()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);
            Club target = world.PlayableLeagues()[0].Clubs[2];

            var knowledge = new KnowledgeStore();
            var watched = new HashSet<int>();
            foreach (Player player in target.Squad.Players.Take(3))
            {
                knowledge.Set(Observer, player.Id, 40, S);
                watched.Add(player.Id);
            }

            var query = new PlayerSearchQuery { ObserverClubId = Observer, ScoutedOnly = true, PageSize = 100 };
            PlayerSearchPage page = index.Search(query, knowledge, WorldSeed, ScoutQuality.Neutral);

            Assert.That(page.Total, Is.EqualTo(watched.Count));
            foreach (PlayerSearchHit hit in page.Hits)
            {
                Assert.That(watched.Contains(hit.PlayerId), Is.True);
                Assert.That(hit.ScoutedKnowledge, Is.EqualTo(40));
            }
        }

        [Test]
        public void Search_ReadsAPlayerAtTheBetterOfScoutingAndFame()
        {
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);

            // The most famous player in the world, found through the search itself.
            var query = new PlayerSearchQuery { ObserverClubId = Observer, PageSize = 1 };
            PlayerSearchHit star = index.Search(query, null, WorldSeed, ScoutQuality.Neutral).Hits[0];

            Assert.That(star.Knowledge, Is.EqualTo(index.FloorOf(star.PlayerId)),
                "With no scouting of his own, the club reads him at exactly what fame gives away");
            Assert.That(star.ScoutedKnowledge, Is.EqualTo(0));

            var knowledge = new KnowledgeStore();
            knowledge.Set(Observer, star.PlayerId, S.MaxKnowledge, S);
            PlayerSearchHit scouted = index.Search(query, knowledge, WorldSeed, ScoutQuality.Neutral).Hits[0];

            Assert.That(scouted.PlayerId, Is.EqualTo(star.PlayerId));
            Assert.That(scouted.Knowledge, Is.EqualTo(S.MaxKnowledge));
            Assert.That(scouted.Overall.Width, Is.LessThan(star.Overall.Width),
                "Scouting a famous player must still tighten the read — otherwise nobody would bother");
        }

        [Test]
        public void Search_NeverReportsABandThatExcludesTheTruth()
        {
            // The task 5.4 invariant, re-checked on the path 11.3 adds: whatever knowledge the search
            // reads a player at, the true overall is inside the band it prints.
            World world = SmallWorld();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);

            var query = new PlayerSearchQuery { ObserverClubId = Observer, PageSize = 200 };
            PlayerSearchPage page = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);

            foreach (PlayerSearchHit hit in page.Hits)
            {
                int truth = PlayerRating.Overall(hit.Player);
                Assert.That(truth, Is.InRange(hit.Overall.Min, hit.Overall.Max),
                    $"{hit.Player.FullName}: true {truth} outside [{hit.Overall.Min}, {hit.Overall.Max}]");
            }
        }

        [Test]
        public void Search_IsDeterministic_AcrossTwoIdenticallyGeneratedWorlds()
        {
            WorldPlayerIndex a = WorldPlayerIndex.Build(SmallWorld(), S);
            WorldPlayerIndex b = WorldPlayerIndex.Build(SmallWorld(), S);

            var query = new PlayerSearchQuery
            {
                Filters = new ScoutingFilters { MinAge = 18, MaxAge = 23 },
                Sort = PlayerSearchSort.Ability,
                ObserverClubId = Observer,
                PageSize = 40
            };

            PlayerSearchPage first = a.Search(query, null, WorldSeed, ScoutQuality.Neutral);
            PlayerSearchPage second = b.Search(query, null, WorldSeed, ScoutQuality.Neutral);

            Assert.That(second.Total, Is.EqualTo(first.Total));
            Assert.That(second.Hits.Select(h => h.PlayerId).ToList(),
                Is.EqualTo(first.Hits.Select(h => h.PlayerId).ToList()));
        }

        [Test]
        public void WholeWorldSweep_TouchesEveryPlayerAndStaysWellInsideTheBudget()
        {
            // The ✅'s performance half. The real measurement is the harness's world scenario on the
            // user's machine (and the in-client bench on the weakest target); this is the regression
            // guard — a generous bound that a rewritten scan walking the object graph again would blow.
            World world = SmallWorld();

            var build = Stopwatch.StartNew();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, S);
            build.Stop();

            var query = new PlayerSearchQuery { ObserverClubId = Observer, PageSize = 20 };

            index.Search(query, null, WorldSeed, ScoutQuality.Neutral); // warm the JIT
            var clock = Stopwatch.StartNew();
            PlayerSearchPage page = index.Search(query, null, WorldSeed, ScoutQuality.Neutral);
            clock.Stop();

            TestContext.Out.WriteLine(
                $"[11.3] index {index.Count} players built in {build.Elapsed.TotalMilliseconds:F1}ms · " +
                $"whole-world query {clock.Elapsed.TotalMilliseconds:F1}ms · {page.Total} matches, {page.Scanned} scanned");

            Assert.That(page.Scanned, Is.EqualTo(index.Count), "A world-wide query touches every entry exactly once");
            Assert.That(clock.Elapsed.TotalMilliseconds, Is.LessThan(1000),
                "A whole-world search must return in well under a second");
        }

        // ------------------------------------------------------- fixtures

        /// <summary>
        /// The whole atlas, as deep as it goes — the world the search screen exists for, and the only
        /// one that actually holds an obscure population (background leagues carry full squads, not
        /// just the spine of a good club). ~26,000 players; generation is milliseconds.
        /// </summary>
        private static World LargeWorld()
        {
            var options = new WorldGenerationOptions
            {
                Scope = new WorldScope
                {
                    Size = DatabaseSize.Large,
                    Playable = { new PlayableNation { Code = "ITA", PlayableTiers = 1 } }
                }
            };

            return new WorldGenerator(options).Generate(WorldSeed);
        }

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

        /// <summary>One area of each kind, so every branch of the index's slicing is exercised.</summary>
        private static List<ScoutingArea> AreasToCheck(World world)
        {
            League league = world.PlayableLeagues()[0];
            return new List<ScoutingArea>
            {
                ScoutingArea.ForClub(league.Clubs[1].Id),
                ScoutingArea.ForNation(world.Nations[0].Code),
                ScoutingArea.ForContinent(Continent.Europe),
                ScoutingArea.ForContinent(Continent.SouthAmerica)
            };
        }

        /// <summary>A hand-built player at a flat attribute level, so his overall is predictable.</summary>
        private static Player MakePlayer(int id, int age, int quality, int potential)
        {
            var player = new Player
            {
                Id = id,
                FirstName = "Test",
                LastName = "Player" + id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Age = age,
                Role = PositionRole.Striker
            };

            for (int i = 0; i < PlayerAttributes.SkillCount; i++)
                player.Attributes[i] = quality;

            player.Development.Potential = potential;
            return player;
        }
    }
}
