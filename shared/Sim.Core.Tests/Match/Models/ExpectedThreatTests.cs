using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Match.Movement.Models;

namespace Sim.Core.Tests.Match.Models
{
    /// <summary>
    /// The expected-threat grid (watchable-match-engine R3): what having the ball at a spot is
    /// worth, read off an integer grid held in config.
    /// </summary>
    [TestFixture]
    public class ExpectedThreatTests
    {
        private static readonly ActionModelBalance Cfg = new MatchBalance().ActionModels;

        [Test]
        public void Grid_IsHeldInConfig_WithOneValuePerCell()
        {
            Assert.That(Cfg.XtGridPer10k.Length, Is.EqualTo(Cfg.XtColumns * Cfg.XtRows));
            Assert.That(Cfg.XtColumns, Is.GreaterThan(1));
            Assert.That(Cfg.XtRows, Is.GreaterThan(1));
        }

        [Test]
        public void Value_NeverFalls_TowardTheAttackedGoal_AlongTheCentreLine()
        {
            int previous = -1;
            for (int x = 0; x <= Pitch.LengthDm; x += 5)
            {
                int value = ExpectedThreat.ValuePer10k(x, Pitch.CenterY, attacksHighX: true, Cfg);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous), $"xT fell at x={x}dm");
                previous = value;
            }

            int ownGoal = ExpectedThreat.ValuePer10k(0, Pitch.CenterY, true, Cfg);
            int theirBox = ExpectedThreat.ValuePer10k(Pitch.LengthDm - 60, Pitch.CenterY, true, Cfg);
            Assert.That(theirBox, Is.GreaterThan(ownGoal * 10), "the box is worth far more than the own goal line");
        }

        [Test]
        public void Value_NeverFalls_TowardTheAttackedGoal_ForTheSideAttackingLowX()
        {
            int previous = -1;
            for (int x = Pitch.LengthDm; x >= 0; x -= 5)
            {
                int value = ExpectedThreat.ValuePer10k(x, Pitch.CenterY, attacksHighX: false, Cfg);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous), $"xT fell at x={x}dm");
                previous = value;
            }
        }

        [Test]
        public void Value_IsMirrored_BetweenTheTwoSides()
        {
            for (int x = 0; x <= Pitch.LengthDm; x += 75)
            for (int y = 0; y <= Pitch.WidthDm; y += 85)
            {
                Assert.That(
                    ExpectedThreat.ValuePer10k(Pitch.LengthDm - x, Pitch.WidthDm - y, false, Cfg),
                    Is.EqualTo(ExpectedThreat.ValuePer10k(x, y, true, Cfg)));
            }
        }

        [Test]
        public void Centre_IsWorthMore_ThanTheWing_InTheFinalThird()
        {
            int x = Pitch.LengthDm - 100;
            Assert.That(ExpectedThreat.ValuePer10k(x, Pitch.CenterY, true, Cfg),
                Is.GreaterThan(ExpectedThreat.ValuePer10k(x, 20, true, Cfg)));
        }

        [Test]
        public void Gain_IsPositive_ForAForwardPass_AndNegative_ForABackPass()
        {
            int forward = ExpectedThreat.GainPer10k(600, Pitch.CenterY, 900, Pitch.CenterY, true, Cfg);
            int back = ExpectedThreat.GainPer10k(900, Pitch.CenterY, 600, Pitch.CenterY, true, Cfg);
            Assert.That(forward, Is.GreaterThan(0));
            Assert.That(back, Is.EqualTo(-forward));
        }

        [Test]
        public void OffPitchCoordinates_AreClampedOntoIt()
        {
            Assert.That(ExpectedThreat.ValuePer10k(-50, -50, true, Cfg),
                Is.EqualTo(ExpectedThreat.ValuePer10k(0, 0, true, Cfg)));
            Assert.That(ExpectedThreat.ValuePer10k(Pitch.LengthDm + 50, Pitch.WidthDm + 50, true, Cfg),
                Is.EqualTo(ExpectedThreat.ValuePer10k(Pitch.LengthDm, Pitch.WidthDm, true, Cfg)));
        }

        [Test]
        public void MalformedGrid_ReadsAsNoThreat_InsteadOfThrowing()
        {
            var broken = new ActionModelBalance { XtGridPer10k = new[] { 1, 2, 3 } };
            Assert.That(ExpectedThreat.ValuePer10k(500, 300, true, broken), Is.EqualTo(0));
        }

        [Test]
        public void Value_DoesNotAllocate()
        {
            ExpectedThreat.ValuePer10k(1, 1, true, Cfg);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int sum = 0;
            for (int i = 0; i < 1000; i++) sum += ExpectedThreat.ValuePer10k(i, i % Pitch.WidthDm, true, Cfg);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0));
            Assert.That(sum, Is.GreaterThan(0));
        }
    }
}
