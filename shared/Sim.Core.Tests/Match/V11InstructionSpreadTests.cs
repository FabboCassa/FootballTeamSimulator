using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R8 on V11: the instruction tables engine v10 was tuned with are read by V11 at a spread of
    /// their own — the distance of each extreme from the neutral middle entry scaled per axis — so
    /// that no instruction dominates the tactic tournament. The middle entry stays the identity.
    /// </summary>
    [TestFixture]
    public class V11InstructionSpreadTests
    {
        private static readonly int[] Push = { -130, 0, 140 };
        private static readonly int[] Percents = { 60, 100, 150 };

        [Test]
        public void Pick_AtFullSpread_IsTheTable()
        {
            for (int i = 0; i < Push.Length; i++)
                Assert.That(InstructionTable.Pick(Push, i, 100), Is.EqualTo(Push[i]));
        }

        [Test]
        public void Pick_ScalesTheDistanceFromTheMiddleEntry()
        {
            Assert.That(InstructionTable.Pick(Push, 0, 50), Is.EqualTo(-65));
            Assert.That(InstructionTable.Pick(Push, 2, 50), Is.EqualTo(70));
            Assert.That(InstructionTable.Percent(Percents, 0, 50), Is.EqualTo(80));
            Assert.That(InstructionTable.Percent(Percents, 2, 50), Is.EqualTo(125));
        }

        [Test]
        public void TheMiddleEntry_IsTheIdentity_AtAnySpread()
        {
            foreach (int spread in new[] { 0, 30, 100, 170 })
            {
                Assert.That(InstructionTable.Pick(Push, 1, spread), Is.EqualTo(0), $"spread {spread}");
                Assert.That(InstructionTable.Percent(Percents, 1, spread), Is.EqualTo(100), $"spread {spread}");
            }
        }

        [Test]
        public void AMissingEntry_KeepsItsFallback()
        {
            Assert.That(InstructionTable.Pick(new[] { 5 }, 2, 50), Is.EqualTo(0), "a missing additive entry is 0");
            Assert.That(InstructionTable.Percent(new[] { 90 }, 2, 50), Is.EqualTo(100), "a missing percentage is 100");
        }

        [Test]
        public void V11Positioning_ReadsTheMentalityPushAtTheV11Spread()
        {
            MatchBalance full = new BalanceConfig().Match, half = new BalanceConfig().Match;
            full.V11MentalitySpreadPercent = 100;
            half.V11MentalitySpreadPercent = 50;
            PositionRole[] shape = Formations.Roles(Formation.F433);

            int BackLine(MatchBalance cfg, Mentality mentality)
            {
                new V11Positioning(cfg).PhaseSpot(V11Slot.InFormation(shape, 1, cfg), true, TeamPhase.Progression, false,
                    new TacticInstructions(mentality, Pressing.Medium, Tempo.Normal, Width.Normal),
                    Pitch.CenterX, Pitch.CenterY, out int x, out int _);
                return x;
            }

            int fullPush = BackLine(full, Mentality.Attacking) - BackLine(full, Mentality.Balanced);
            int halfPush = BackLine(half, Mentality.Attacking) - BackLine(half, Mentality.Balanced);
            Assert.That(fullPush, Is.GreaterThan(0), "an attacking side holds a higher line");
            Assert.That(halfPush, Is.EqualTo(fullPush / 2).Within(1), "at half the spread, half the push");
            Assert.That(BackLine(half, Mentality.Balanced), Is.EqualTo(BackLine(full, Mentality.Balanced)), "neutral is untouched");
        }
    }
}
