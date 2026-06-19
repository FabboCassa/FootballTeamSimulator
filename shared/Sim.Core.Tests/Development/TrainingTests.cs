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
    /// Task 4.3 acceptance: weekly team + individual training focuses drive development
    /// and tactic familiarity. THE ✅: two identical save seeds trained differently
    /// diverge in attributes after a season. Plus the supporting guarantees — growth is
    /// gated by potential, decline is capped (anti-frustration), the whole world evolves,
    /// and everything is deterministic.
    /// </summary>
    [TestFixture]
    public class TrainingTests
    {
        private const int WeeksPerSeason = 38;
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static DevelopmentBalance D => Cfg.Development;

        // Skill indices (PlayerAttributes order).
        private const int Dribbling = 5, Shooting = 6, Defending = 7;

        // ------------------------------------------------------------ THE ✅: divergence

        [Test]
        public void SameSeed_DifferentTraining_DivergesAfterASeason()
        {
            // Two identical worlds (same generation seed == same save), same club.
            Club attackingClub = MakeClubWithHeadroom(seed: 4_030_001, clubIndex: 7);
            Club defendingClub = MakeClubWithHeadroom(seed: 4_030_001, clubIndex: 7);

            // Sanity: identical generation => identical starting attributes.
            Assert.That(SkillSum(defendingClub, Shooting), Is.EqualTo(SkillSum(attackingClub, Shooting)),
                "Same seed must produce identical squads before training");

            // Same RNG stream each week (so only the focus differs), different focus.
            TrainSeason(attackingClub, TeamTrainingFocus.Attacking, baseSeed: 555);
            TrainSeason(defendingClub, TeamTrainingFocus.Defending, baseSeed: 555);

            int atkShooting = SkillSum(attackingClub, Shooting) + SkillSum(attackingClub, Dribbling);
            int defShooting = SkillSum(defendingClub, Shooting) + SkillSum(defendingClub, Dribbling);
            int atkDefending = SkillSum(attackingClub, Defending);
            int defDefending = SkillSum(defendingClub, Defending);

            TestContext.Out.WriteLine(
                $"[training-diverge] attacking focus: shoot+drib {atkShooting}, defend {atkDefending} | " +
                $"defending focus: shoot+drib {defShooting}, defend {defDefending}");

            Assert.That(atkShooting, Is.GreaterThan(defShooting),
                "An attacking-focused squad must end the season sharper in shooting/dribbling");
            Assert.That(defDefending, Is.GreaterThan(atkDefending),
                "A defending-focused squad must end the season stronger defensively");
        }

        // ------------------------------------------------------------ determinism

        [Test]
        public void Training_IsDeterministic_PerSeedAndPlan()
        {
            Club a = MakeClubWithHeadroom(seed: 909, clubIndex: 3);
            Club b = MakeClubWithHeadroom(seed: 909, clubIndex: 3);

            TrainSeason(a, TeamTrainingFocus.Technical, baseSeed: 42);
            TrainSeason(b, TeamTrainingFocus.Technical, baseSeed: 42);

            foreach (var (pa, pb) in Pairs(a, b))
                for (int s = 0; s < PlayerAttributes.SkillCount; s++)
                    Assert.That(pb.Attributes[s], Is.EqualTo(pa.Attributes[s]),
                        $"Same seed + same plan must replay identically (player {pa.Id}, skill {s})");
        }

        // ------------------------------------------------------------ growth gated by potential

        [Test]
        public void Growth_SaturatesAtPotential_NeverExceedsIt()
        {
            // Give a little headroom, then hammer it with the most aggressive focus over
            // several seasons. The per-increment gate must stop growth exactly at potential.
            Club club = MakeClub(seed: 1234, clubIndex: 5);
            foreach (Player p in club.Squad.Players)
                p.Development.Potential = AttributeScale.ClampSkill(PlayerRating.Overall(p) + 5);

            TrainSeason(club, TeamTrainingFocus.Attacking, baseSeed: 99,
                individual: IndividualTrainingFocus.Attacking, seasons: 3);

            foreach (Player p in club.Squad.Players)
                Assert.That(PlayerRating.Overall(p), Is.LessThanOrEqualTo(p.Development.Potential),
                    $"Growth must never push a player past his potential (player {p.Id})");
        }

        // ------------------------------------------------------------ decline is real but capped

        [Test]
        public void AtPotential_DeclinesGently_ButNeverBelowTheFloor()
        {
            Club club = MakeClub(seed: 2468, clubIndex: 2);

            // Veterans: sitting exactly at potential, so they're in the decline regime.
            int declined = 0;
            foreach (Player p in club.Squad.Players)
                p.Development.Potential = PlayerRating.Overall(p);

            var before = new Dictionary<int, int>();
            foreach (Player p in club.Squad.Players)
                before[p.Id] = PlayerRating.Overall(p);

            // Untrained group (Tactical = low protection on every skill) over two seasons.
            TrainSeason(club, TeamTrainingFocus.Tactical, baseSeed: 7, seasons: 2);

            foreach (Player p in club.Squad.Players)
            {
                int overall = PlayerRating.Overall(p);
                int floor = p.Development.Potential - D.DeclineFloorPoints;
                Assert.That(overall, Is.GreaterThanOrEqualTo(floor),
                    $"Decline must be capped at potential − {D.DeclineFloorPoints} (player {p.Id})");
                Assert.That(overall, Is.LessThanOrEqualTo(p.Development.Potential),
                    "A declining player can't be above his potential");
                if (overall < before[p.Id]) declined++;
            }

            TestContext.Out.WriteLine($"[training-decline] {declined}/{club.Squad.Players.Count} at-potential players lost ground over 2 seasons (capped at −{D.DeclineFloorPoints})");
            Assert.That(declined, Is.GreaterThan(0),
                "Players parked at their potential must visibly decline over time (the world doesn't stagnate)");
        }

        // ------------------------------------------------------------ tactic familiarity half

        [Test]
        public void TacticalFocus_IsTheOnlyFocusThatGainsFamiliarity()
        {
            Assert.That(TrainingModel.TacticalFamiliarityGain(TeamTrainingFocus.Tactical, D),
                Is.EqualTo(D.TacticalFocusFamiliarityGainPerWeek).And.GreaterThan(0));

            foreach (TeamTrainingFocus focus in new[]
                     { TeamTrainingFocus.Balanced, TeamTrainingFocus.Attacking,
                       TeamTrainingFocus.Defending, TeamTrainingFocus.Physical, TeamTrainingFocus.Technical })
                Assert.That(TrainingModel.TacticalFamiliarityGain(focus, D), Is.Zero,
                    $"{focus} must not drill tactic familiarity");
        }

        // ------------------------------------------------------------ whole world evolves

        [Test]
        public void WholeWorld_Evolves_AndIsDeterministic_AiClubsTrainToo()
        {
            League worldA = new LeagueGenerator().Generate(new Pcg32(31_337));
            League worldB = new LeagueGenerator().Generate(new Pcg32(31_337));

            // The user controls only his own club's plan; everyone else trains the default.
            int userClubId = worldA.Clubs[0].Id;
            var plans = new Dictionary<int, TrainingPlan>
            {
                [userClubId] = new TrainingPlan { TeamFocus = TeamTrainingFocus.Physical }
            };

            var progA = new TrainingProgressor(D);
            var progB = new TrainingProgressor(D);
            for (int week = 0; week < WeeksPerSeason; week++)
            {
                progA.EvolveWeek(new[] { worldA }, plans, worldSeed: 88, week);
                progB.EvolveWeek(new[] { worldB }, plans, worldSeed: 88, week);
            }

            // Deterministic: two identical worlds + identical plans + same seed agree everywhere.
            for (int c = 0; c < worldA.Clubs.Count; c++)
                foreach (var (pa, pb) in Pairs(worldA.Clubs[c], worldB.Clubs[c]))
                    for (int s = 0; s < PlayerAttributes.SkillCount; s++)
                        Assert.That(pb.Attributes[s], Is.EqualTo(pa.Attributes[s]),
                            "Whole-world training must be deterministic");

            // An AI club (no plan of its own) must still have evolved — the world develops.
            League fresh = new LeagueGenerator().Generate(new Pcg32(31_337));
            Club aiBefore = fresh.Clubs[5];
            Club aiAfter = worldA.Clubs[5];
            int changed = SkillTotal(aiAfter) - SkillTotal(aiBefore);
            TestContext.Out.WriteLine($"[training-world] AI club skill-total change over a season: {changed}");
            Assert.That(changed, Is.Not.Zero, "AI clubs (balanced default) must develop too — the world isn't frozen");
        }

        // ------------------------------------------------------------ helpers

        private static Club MakeClub(int seed, int clubIndex) =>
            new LeagueGenerator().Generate(new Pcg32((ulong)seed)).Clubs[clubIndex];

        /// <summary>A generated club with potential lifted so every player can grow (isolates the focus effect).</summary>
        private static Club MakeClubWithHeadroom(int seed, int clubIndex)
        {
            Club club = MakeClub(seed, clubIndex);
            foreach (Player p in club.Squad.Players)
                p.Development.Potential = AttributeScale.ClampSkill(PlayerRating.Overall(p) + 20);
            return club;
        }

        private static void TrainSeason(
            Club club, TeamTrainingFocus team, int baseSeed,
            IndividualTrainingFocus individual = IndividualTrainingFocus.None, int seasons = 1)
        {
            for (int week = 0; week < WeeksPerSeason * seasons; week++)
            {
                var rng = new Pcg32((ulong)baseSeed, (ulong)week);
                foreach (Player p in club.Squad.Players)
                    TrainingModel.ApplyTrainingWeek(p, team, individual, rng, D);
            }
        }

        private static int SkillSum(Club club, int skillIndex)
        {
            int sum = 0;
            foreach (Player p in club.Squad.Players) sum += p.Attributes[skillIndex];
            return sum;
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
