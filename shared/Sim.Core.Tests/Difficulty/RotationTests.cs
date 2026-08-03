using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Difficulty;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Difficulty
{
    /// <summary>
    /// Task 10.1: squad rotation is its own lever.
    ///
    /// The balance harness found that the 5.7 difficulty lever inverts once condition is live — Easy−Hard
    /// went from +6.9 points a season on the bare engine to −0.5 with condition on, and the isolation run
    /// pinned it on the lineup selector: a competence miss BENCHES the best candidate, which with live
    /// condition is the same thing as RESTING him, and rest is worth points. So a weaker AI was quietly
    /// being handed fresher legs, and the two effects cancelled.
    ///
    /// <see cref="RotationPolicy"/> separates them: competence is quality, rotation is freshness, and both
    /// now pull the same way (a good AI rests tired players AND picks the best of the fresh).
    ///
    /// The safety property that keeps every golden master valid: a fully fit squad is NEVER re-ranked, and
    /// <see cref="RotationPolicy.None"/> is the pre-10.1 selector exactly.
    /// </summary>
    [TestFixture]
    public class RotationTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static RotationPolicy FullRotation =>
            new RotationPolicy(100, Cfg.Difficulty.RotationFitnessTarget, Cfg.Difficulty.RotationPenaltyPerFitnessPoint);

        // ---------------------------------------------------------- identity (the golden-master guarantee)

        [Test]
        public void FreshSquad_OrNoPolicy_PicksExactlyTheOldEleven()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(555));
            Club club = league.Clubs[0];
            PositionRole[] f = LineupSelector.DefaultFormation;

            // Generation starts everyone at full fitness, so this is the state every condition-free test
            // (and every golden master) runs in.
            foreach (Player p in club.Squad.Players)
                Assert.That(p.Condition.Fitness, Is.EqualTo(AttributeScale.MaxCondition),
                    "Generated players start fully fit - the identity below depends on it");

            Lineup best = LineupSelector.BestEleven(club, f);
            Lineup rotatedFresh = LineupSelector.CompetentEleven(club, f, 100, 3, FullRotation, new Pcg32(999));
            Lineup noPolicy = LineupSelector.CompetentEleven(club, f, 100, 3, RotationPolicy.None, new Pcg32(999));

            Assert.That(Ids(rotatedFresh), Is.EqualTo(Ids(best)),
                "A fully fit squad must rank exactly as before - rotation only ever discounts tired players");
            Assert.That(Ids(noPolicy), Is.EqualTo(Ids(best)),
                "RotationPolicy.None must reproduce the pre-10.1 selection");

            // And the policy itself is the identity on a fresh player at any percentage.
            Assert.That(FullRotation.Adjust(80, AttributeScale.MaxCondition), Is.EqualTo(80));
            Assert.That(RotationPolicy.None.Adjust(80, 10), Is.EqualTo(80));
        }

        // ---------------------------------------------------------- the ✅: tired players get rested

        [Test]
        public void ARotatingManager_RestsTheDrainedStarter_AndAManagerWhoNeverRotatesDoesNot()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(4_242));
            Club club = league.Clubs[0];
            PositionRole[] f = LineupSelector.DefaultFormation;

            // Drain the best striker to the bone; leave everyone else fresh.
            Lineup best = LineupSelector.BestEleven(club, f);
            Player drained = SlotPlayer(best, PositionRole.Striker);
            drained.Condition.Fitness = 40;

            Lineup rotating = LineupSelector.CompetentEleven(club, f, 100, 3, FullRotation, new Pcg32(1));
            Lineup fixedXi = LineupSelector.CompetentEleven(club, f, 100, 3, RotationPolicy.None, new Pcg32(1));

            TestContext.Out.WriteLine(
                $"[rotation] drained {drained.FullName} to fitness {drained.Condition.Fitness}: " +
                $"rotating manager fields him = {Ids(rotating).Contains(drained.Id)}, " +
                $"never-rotating manager fields him = {Ids(fixedXi).Contains(drained.Id)}");

            Assert.That(Ids(fixedXi), Does.Contain(drained.Id),
                "A manager who never rotates plays his best striker however tired he is");
            Assert.That(Ids(rotating), Does.Not.Contain(drained.Id),
                "A fully rotating manager rests a starter drained 50 points below the fitness target");
            Assert.That(rotating.Slots.Count, Is.EqualTo(Lineup.Size),
                "Resting someone must still produce a legal eleven");

            // The discount is proportional, not a switch: two points of tiredness cost two rating points,
            // fifty cost fifty. (Asserted on the policy itself rather than on a selection, so it cannot
            // turn into a coin flip between two players of nearly equal ability.)
            int target = Cfg.Difficulty.RotationFitnessTarget;
            Assert.That(FullRotation.Adjust(80, target - 2), Is.EqualTo(78));
            Assert.That(FullRotation.Adjust(80, target - 50), Is.EqualTo(30));
            Assert.That(new RotationPolicy(50, target, 1).Adjust(80, target - 50), Is.EqualTo(55),
                "Half rotation must cost half the discount");
        }

        // ---------------------------------------------------------- the levers are now independent

        [Test]
        public void RotationAndCompetence_AreSeparateLevers_AndPullTheSameWay()
        {
            var cfg = new BalanceConfig();
            DifficultySettings easy = DifficultyModel.Resolve(DifficultyLevel.Easy, cfg);
            DifficultySettings normal = DifficultyModel.Resolve(DifficultyLevel.Normal, cfg);
            DifficultySettings hard = DifficultyModel.Resolve(DifficultyLevel.Hard, cfg);

            TestContext.Out.WriteLine(
                $"[rotation-levels] competence {easy.AiLineupCompetence}/{normal.AiLineupCompetence}/{hard.AiLineupCompetence}, " +
                $"rotation {easy.AiRotationPercent}/{normal.AiRotationPercent}/{hard.AiRotationPercent} (Easy/Normal/Hard)");

            // Both levers must rise together: a harder AI picks better AND manages fatigue better. If they
            // ever pointed opposite ways again we would be back to the inversion 10.1 found.
            Assert.That(easy.AiLineupCompetence, Is.LessThan(hard.AiLineupCompetence));
            Assert.That(easy.AiRotationPercent, Is.LessThan(hard.AiRotationPercent));
            Assert.That(normal.AiRotationPercent, Is.InRange(easy.AiRotationPercent, hard.AiRotationPercent));

            // The match context carries the policy, and the human club is never touched by either lever.
            DifficultyContext ctxHard = DifficultyModel.MatchContext(7, hard, cfg.Difficulty);
            Assert.That(ctxHard.Rotation.IsActive, Is.True, "Hard's AI must rotate");
            Assert.That(ctxHard.DegradesAiLineups, Is.True,
                "Hard no longer means 'best XI always' - it means best FRESH XI");

            DifficultyContext ctxEasy = DifficultyModel.MatchContext(7, easy, cfg.Difficulty);
            Assert.That(ctxEasy.Rotation.IsActive, Is.False, "Easy's AI must never rotate");
        }

        // ---------------------------------------------------------- determinism

        [Test]
        public void Rotation_IsDeterministic_GivenTheSameSeedAndCondition()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(90_210));
            Club club = league.Clubs[3];
            PositionRole[] f = LineupSelector.DefaultFormation;

            int i = 0;
            foreach (Player p in club.Squad.Players)
                p.Condition.Fitness = 50 + (i++ * 7) % 50; // a spread of tiredness, deterministically

            Lineup a = LineupSelector.CompetentEleven(club, f, 78, 3, FullRotation, new Pcg32(11));
            Lineup b = LineupSelector.CompetentEleven(club, f, 78, 3, FullRotation, new Pcg32(11));
            Assert.That(Ids(b), Is.EqualTo(Ids(a)),
                "Same seed, same condition, same XI - rotation adds no hidden state");
        }

        // ---------------------------------------------------------- helpers

        private static List<int> Ids(Lineup lineup)
        {
            var ids = new List<int>();
            foreach (LineupSlot slot in lineup.Slots) ids.Add(slot.Player.Id);
            return ids;
        }

        private static Player SlotPlayer(Lineup lineup, PositionRole role)
        {
            foreach (LineupSlot slot in lineup.Slots)
                if (slot.Role == role) return slot.Player;
            return lineup.Slots[0].Player;
        }
    }
}
