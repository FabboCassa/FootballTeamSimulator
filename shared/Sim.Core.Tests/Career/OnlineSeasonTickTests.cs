using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Condition;
using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// Task 8.4: the server-authoritative weekly tick for an online private-league season
    /// (<see cref="OnlineSeasonTick"/>) plus the <see cref="WorldStateHasher"/> that lets the client and
    /// server agree on the evolved state. THE ✅ (agreement): two identical worlds advanced through the
    /// same rounds with the same submitted training plans produce the byte-identical world-state hash, so
    /// a client re-running the same Sim.Core progressors derives exactly what the server stored. Plus the
    /// supporting guarantees — playing drains fitness while rest recovers it, the hash is order-independent,
    /// and a fresh world starts neutral and actually evolves.
    /// </summary>
    [TestFixture]
    public class OnlineSeasonTickTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        // ---------------------------------------------------- THE ✅: client and server agree

        [Test]
        public void EvolveWeek_IsDeterministic_ForTheWholeWorld_TheAgreementCheck()
        {
            League worldA = new LeagueGenerator().Generate(new Pcg32(8_040_001));
            League worldB = new LeagueGenerator().Generate(new Pcg32(8_040_001));

            const ulong worldSeed = 8_040_001;
            var plansA = UserPlans(worldA, TeamTrainingFocus.Attacking);
            var plansB = UserPlans(worldB, TeamTrainingFocus.Attacking);

            for (int round = 1; round <= 4; round++)
            {
                OnlineSeasonTick.EvolveWeek(new[] { worldA }, PlayedFirstTwoClubs(worldA), plansA, worldSeed, round, Cfg);
                OnlineSeasonTick.EvolveWeek(new[] { worldB }, PlayedFirstTwoClubs(worldB), plansB, worldSeed, round, Cfg);
            }

            ulong hashA = WorldStateHasher.Hash(new[] { worldA });
            ulong hashB = WorldStateHasher.Hash(new[] { worldB });
            TestContext.Out.WriteLine($"[online-tick] agreement hash after 4 rounds = {WorldStateHasher.ToHex(hashA)}");

            Assert.That(hashB, Is.EqualTo(hashA),
                "Two identical worlds advanced identically must produce the same state hash (client == server).");
        }

        // ---------------------------------------------------- playing tires, rest recovers

        [Test]
        public void Playing_DrainsFitness_While_Resting_Recovers()
        {
            League world = new LeagueGenerator().Generate(new Pcg32(8_040_002));

            // A starter of a club that plays, and every player of a club that sits the round out.
            Club playing = world.Clubs[0];
            Club idle = world.Clubs[world.Clubs.Count - 1];
            HashSet<int> starterIds = StarterIds(playing);
            Player starter = FindPlayer(playing, First(starterIds));

            // Low stamina guarantees the match-day drain exceeds a week's recovery (a very high-stamina
            // player could drain less than he recovers and cap back at full), so the drop is observable.
            starter.Attributes.Stamina = 20;
            Assert.That(starter.Condition.Fitness, Is.EqualTo(AttributeScale.MaxCondition), "fresh world starts fully fit");

            var played = new Dictionary<int, ConditionProgressor.Participation>
            {
                [playing.Id] = new ConditionProgressor.Participation(starterIds, TeamResult.Win),
            };
            OnlineSeasonTick.EvolveWeek(new[] { world }, played, trainingPlans: null, worldSeed: 8_040_002, round: 1, Cfg);

            TestContext.Out.WriteLine(
                $"[online-tick] starter fitness after a played week = {starter.Condition.Fitness} (drained then partly recovered)");

            Assert.That(starter.Condition.Fitness, Is.LessThan(AttributeScale.MaxCondition),
                "A player who started still ends the week below full fitness (played > recovered).");
            foreach (Player p in idle.Squad.Players)
                Assert.That(p.Condition.Fitness, Is.EqualTo(AttributeScale.MaxCondition),
                    "A club that rested the whole week stays fully fit.");
        }

        // ---------------------------------------------------- the hash is order-independent

        [Test]
        public void WorldStateHash_IsOrderIndependent()
        {
            League world = new LeagueGenerator().Generate(new Pcg32(8_040_003));

            var forward = new List<Club>(world.Clubs);
            var reversed = new List<Club>(world.Clubs);
            reversed.Reverse();

            Assert.That(WorldStateHasher.Hash(reversed), Is.EqualTo(WorldStateHasher.Hash(forward)),
                "The state hash must not depend on the order clubs are visited in.");
        }

        // ---------------------------------------------------- fresh = neutral, and a tick moves it

        [Test]
        public void FreshWorld_IsNeutral_And_ATickChangesTheHash()
        {
            League world = new LeagueGenerator().Generate(new Pcg32(8_040_004));

            // Generation starts every player at neutral form and full fitness (morale carries a small
            // generated spread, so it is not asserted here).
            foreach (Club c in world.Clubs)
                foreach (Player p in c.Squad.Players)
                    Assert.Multiple(() =>
                    {
                        Assert.That(p.Condition.Form, Is.EqualTo(Cfg.Condition.FormNeutral));
                        Assert.That(p.Condition.Fitness, Is.EqualTo(AttributeScale.MaxCondition));
                    });

            ulong before = WorldStateHasher.Hash(new[] { world });
            OnlineSeasonTick.EvolveWeek(new[] { world }, PlayedFirstTwoClubs(world), trainingPlans: null, worldSeed: 8_040_004, round: 1, Cfg);
            ulong after = WorldStateHasher.Hash(new[] { world });

            Assert.That(after, Is.Not.EqualTo(before), "A week of play must evolve (and so change the hash of) the world state.");
        }

        // ---------------------------------------------------- helpers

        private static Dictionary<int, TrainingPlan> UserPlans(League world, TeamTrainingFocus focus) =>
            new Dictionary<int, TrainingPlan> { [world.Clubs[0].Id] = new TrainingPlan { TeamFocus = focus } };

        /// <summary>Marks the first two clubs as having played this round (one win, one loss); the rest rest.</summary>
        private static Dictionary<int, ConditionProgressor.Participation> PlayedFirstTwoClubs(League world) =>
            new Dictionary<int, ConditionProgressor.Participation>
            {
                [world.Clubs[0].Id] = new ConditionProgressor.Participation(StarterIds(world.Clubs[0]), TeamResult.Win),
                [world.Clubs[1].Id] = new ConditionProgressor.Participation(StarterIds(world.Clubs[1]), TeamResult.Loss),
            };

        private static HashSet<int> StarterIds(Club club)
        {
            var ids = new HashSet<int>();
            foreach (LineupSlot slot in LineupSelector.BestEleven(club).Slots)
                if (slot.Player != null) ids.Add(slot.Player.Id);
            return ids;
        }

        private static int First(HashSet<int> ids)
        {
            foreach (int id in ids) return id;
            return 0;
        }

        private static Player FindPlayer(Club club, int id)
        {
            foreach (Player p in club.Squad.Players)
                if (p.Id == id) return p;
            return club.Squad.Players[0];
        }
    }
}
