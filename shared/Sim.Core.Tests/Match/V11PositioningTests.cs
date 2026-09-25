using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R2, the phase target spot (watchable-match spec, task 6): where a man belongs is his place
    /// in the formation, moved by the phase his side is in and by the instructions — width,
    /// mentality and line height (the Pressing instruction, out of possession). Scripted: no match
    /// is played. Home (side 0) attacks toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11PositioningTests
    {
        // Formations.Roles(F433): GK, CB, CB, FB, FB, DM, CM, CM, W, W, ST.
        private const int CentreBack = 1;
        private const int FullBack = 3;
        private const int Midfielder = 6;
        private const int Striker = 10;

        private static readonly PositionRole[] Shape = Formations.Roles(Formation.F433);

        private MatchBalance _cfg = null!;
        private V11Positioning _positioning = null!;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _positioning = new V11Positioning(_cfg);
        }

        private static TacticInstructions With(
            Mentality mentality = Mentality.Balanced, Pressing pressing = Pressing.Medium, Width width = Width.Normal) =>
            new TacticInstructions(mentality, pressing, Tempo.Normal, width);

        private (int X, int Y) Spot(
            int slot, TeamPhase phase, bool inPossession, TacticInstructions instructions,
            bool home = true, int ballXDm = Pitch.CenterX, int ballYDm = Pitch.CenterY)
        {
            _positioning.PhaseSpot(V11Slot.InFormation(Shape, slot, _cfg), home, phase, inPossession,
                instructions, ballXDm, ballYDm, out int x, out int y);
            return (x, y);
        }

        private static int OffCentre((int X, int Y) spot) => Math.Abs(spot.Y - Pitch.CenterY);

        [Test]
        public void Spot_IsTheFormationShape_LinesInOrderAndFullBacksWide()
        {
            TacticInstructions neutral = With();
            var back = Spot(CentreBack, TeamPhase.Progression, true, neutral);
            var middle = Spot(Midfielder, TeamPhase.Progression, true, neutral);
            var front = Spot(Striker, TeamPhase.Progression, true, neutral);
            var wide = Spot(FullBack, TeamPhase.Progression, true, neutral);

            Assert.That(back.X, Is.LessThan(middle.X), "the back line stands behind the midfield");
            Assert.That(middle.X, Is.LessThan(front.X), "the midfield stands behind the striker");
            Assert.That(wide.X, Is.EqualTo(back.X), "a full-back is on the back line");
            Assert.That(OffCentre(wide), Is.GreaterThan(OffCentre(back)), "a full-back is wider than a centre-back");
            Assert.That(OffCentre(front), Is.LessThan(OffCentre(wide)), "the striker is central");
        }

        [Test]
        public void Spot_ForTheAwaySide_IsTheHomeSpotMirrored()
        {
            for (int slot = 1; slot < Shape.Length; slot++)
            {
                var home = Spot(slot, TeamPhase.FinalThird, true, With(), home: true);
                var away = Spot(slot, TeamPhase.FinalThird, true, With(), home: false);
                Assert.That(away.X, Is.EqualTo(Pitch.LengthDm - home.X), $"slot {slot} along the length");
                Assert.That(away.Y, Is.EqualTo(Pitch.WidthDm - home.Y), $"slot {slot} across the width");
            }
        }

        [Test]
        public void Phase_InPossession_PushesTheBlockUpTowardTheFinalThird()
        {
            TacticInstructions neutral = With();
            int buildUp = Spot(CentreBack, TeamPhase.BuildUp, true, neutral).X;
            int progression = Spot(CentreBack, TeamPhase.Progression, true, neutral).X;
            int finalThird = Spot(CentreBack, TeamPhase.FinalThird, true, neutral).X;

            Assert.That(buildUp, Is.LessThan(progression));
            Assert.That(progression, Is.LessThan(finalThird));
        }

        [Test]
        public void Phase_OutOfPossession_DropsInDefenceTransition_AndIsCompactAndNarrow()
        {
            TacticInstructions neutral = With();
            var holding = Spot(Midfielder, TeamPhase.Progression, false, neutral);
            var dropping = Spot(Midfielder, TeamPhase.DefenceTransition, false, neutral);
            var attacking = Spot(Midfielder, TeamPhase.Progression, true, neutral);

            Assert.That(dropping.X, Is.LessThan(holding.X), "losing the ball drops the block");
            Assert.That(holding.X, Is.LessThan(attacking.X), "without the ball the block sits deeper");

            int attackingWidth = OffCentre(Spot(FullBack, TeamPhase.Progression, true, neutral));
            int defendingWidth = OffCentre(Spot(FullBack, TeamPhase.Progression, false, neutral));
            Assert.That(defendingWidth, Is.LessThan(attackingWidth), "without the ball the block is narrower");
        }

        [Test]
        public void Width_SpreadsTheShape()
        {
            int narrow = OffCentre(Spot(FullBack, TeamPhase.Progression, true, With(width: Width.Narrow)));
            int normal = OffCentre(Spot(FullBack, TeamPhase.Progression, true, With(width: Width.Normal)));
            int wide = OffCentre(Spot(FullBack, TeamPhase.Progression, true, With(width: Width.Wide)));

            Assert.That(narrow, Is.LessThan(normal));
            Assert.That(normal, Is.LessThan(wide));
        }

        [Test]
        public void Mentality_RaisesTheBlock()
        {
            foreach (bool inPossession in new[] { true, false })
            {
                int defensive = Spot(CentreBack, TeamPhase.Progression, inPossession, With(Mentality.Defensive)).X;
                int balanced = Spot(CentreBack, TeamPhase.Progression, inPossession, With(Mentality.Balanced)).X;
                int attacking = Spot(CentreBack, TeamPhase.Progression, inPossession, With(Mentality.Attacking)).X;

                Assert.That(defensive, Is.LessThan(balanced), $"in possession: {inPossession}");
                Assert.That(balanced, Is.LessThan(attacking), $"in possession: {inPossession}");
            }
        }

        [Test]
        public void LineHeight_IsThePressingInstruction_OutOfPossessionOnly()
        {
            int low = Spot(CentreBack, TeamPhase.Progression, false, With(pressing: Pressing.Low)).X;
            int medium = Spot(CentreBack, TeamPhase.Progression, false, With(pressing: Pressing.Medium)).X;
            int high = Spot(CentreBack, TeamPhase.Progression, false, With(pressing: Pressing.High)).X;
            Assert.That(low, Is.LessThan(medium));
            Assert.That(medium, Is.LessThan(high));

            Assert.That(Spot(CentreBack, TeamPhase.Progression, true, With(pressing: Pressing.High)).X,
                Is.EqualTo(Spot(CentreBack, TeamPhase.Progression, true, With(pressing: Pressing.Low)).X),
                "with the ball the press has nothing to say about the line");
        }

        [Test]
        public void Onside_AManBeyondTheLineIsHeldJustOnside_OthersAreLeftAlone()
        {
            const int line = 800;
            int hold = _cfg.V11OnsideHoldDm;

            Assert.That(_positioning.Onside(true, 850, line), Is.EqualTo(line - hold));
            Assert.That(_positioning.Onside(true, 600, line), Is.EqualTo(600));
            Assert.That(_positioning.Onside(false, Pitch.LengthDm - 850, Pitch.LengthDm - line),
                Is.EqualTo(Pitch.LengthDm - line + hold), "away attacks toward x = 0");
        }
    }
}
