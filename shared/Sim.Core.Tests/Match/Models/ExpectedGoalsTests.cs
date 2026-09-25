using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Match.Movement.Models;

namespace Sim.Core.Tests.Match.Models
{
    /// <summary>
    /// The xG shot model (watchable-match-engine R3): distance, angle, pressure and the shooter.
    /// </summary>
    [TestFixture]
    public class ExpectedGoalsTests
    {
        private static readonly ActionModelBalance Cfg = new MatchBalance().ActionModels;

        private const int Average = 50;

        private static int Xg(int metresOut, int offCentreDm = 0, int pressure = 0, int skill = Average)
            => ExpectedGoals.Permille(Pitch.LengthDm - metresOut * 10, Pitch.CenterY + offCentreDm,
                attacksHighX: true, pressure, skill, skill, Cfg);

        [Test]
        public void SixYardCentral_BeatsEdgeOfTheBox_BeatsThirtyMetres()
        {
            int sixYard = Xg(5);
            int edgeOfBox = Xg(17);
            int thirty = Xg(30);

            Assert.That(sixYard, Is.GreaterThan(edgeOfBox));
            Assert.That(edgeOfBox, Is.GreaterThan(thirty));
            Assert.That(thirty, Is.GreaterThan(0));
        }

        [Test]
        public void SixYardCentral_IsARealChance_AndThirtyMetres_IsAHopeful_One()
        {
            Assert.That(Xg(5), Is.InRange(300, 700));
            Assert.That(Xg(17), Is.InRange(50, 160));
            Assert.That(Xg(30), Is.InRange(5, 40));
        }

        [Test]
        public void Angle_LowersIt_AtTheSameDistance()
        {
            // Same 12 m from the goal centre: straight on, then round toward the byline.
            int central = ExpectedGoals.Permille(Pitch.LengthDm - 120, Pitch.CenterY, true, 0, Average, Average, Cfg);
            int angled = ExpectedGoals.Permille(Pitch.LengthDm - 72, Pitch.CenterY + 96, true, 0, Average, Average, Cfg);
            int tight = ExpectedGoals.Permille(Pitch.LengthDm - 20, Pitch.CenterY + 118, true, 0, Average, Average, Cfg);

            Assert.That(angled, Is.LessThan(central));
            Assert.That(tight, Is.LessThan(angled));
        }

        [Test]
        public void Pressure_LowersIt()
        {
            Assert.That(Xg(11, pressure: 500), Is.LessThan(Xg(11, pressure: 0)));
            Assert.That(Xg(11, pressure: 1000), Is.LessThan(Xg(11, pressure: 500)));
        }

        [Test]
        public void ABetterFinisher_RaisesIt()
        {
            Assert.That(Xg(11, skill: 90), Is.GreaterThan(Xg(11, skill: Average)));
            Assert.That(Xg(11, skill: 10), Is.LessThan(Xg(11, skill: Average)));
        }

        [Test]
        public void TheSideAttackingLowX_SeesTheMirroredChance()
        {
            int home = ExpectedGoals.Permille(Pitch.LengthDm - 140, Pitch.CenterY + 60, true, 300, 70, 60, Cfg);
            int away = ExpectedGoals.Permille(140, Pitch.CenterY - 60, false, 300, 70, 60, Cfg);
            Assert.That(away, Is.EqualTo(home));
        }

        [Test]
        public void IsAlwaysAProbability()
        {
            for (int x = 0; x <= Pitch.LengthDm; x += 50)
            for (int y = 0; y <= Pitch.WidthDm; y += 40)
            {
                int xg = ExpectedGoals.Permille(x, y, true, 0, 100, 100, Cfg);
                Assert.That(xg, Is.InRange(1, 999), $"({x},{y})");
            }

            Assert.That(ExpectedGoals.Permille(Pitch.LengthDm, Pitch.CenterY, true, 0, 100, 100, Cfg), Is.InRange(1, 999));
        }

        [Test]
        public void Permille_DoesNotAllocate()
        {
            Xg(11);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int sum = 0;
            for (int i = 0; i < 1000; i++) sum += Xg(5 + i % 30, i % 200 - 100, i % 1000);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0));
            Assert.That(sum, Is.GreaterThan(0));
        }
    }
}
