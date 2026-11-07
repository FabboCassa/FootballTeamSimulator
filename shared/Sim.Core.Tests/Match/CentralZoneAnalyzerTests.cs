using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Issue #206: the central-zone and penalty-area spells of <see cref="CentralZoneAnalyzer"/>,
    /// pinned against hand-built streams. Home attacks the goal at x = 1050 dm: (950, 340) is 10 m
    /// from its centre, in the zone and the box; (900, 520) is in the box (15 m deep, 18 m off the
    /// centre) but 24 m from the goal centre, outside the zone. The away keeper never spoils "free".
    /// </summary>
    [TestFixture]
    public class CentralZoneAnalyzerTests
    {
        private const int CY = Pitch.CenterY;
        private const int Striker = 9;
        private const int Mate = 6;
        private const int Defender = 4;

        private static CentralZoneMetrics Measure(PositionStream s) => CentralZoneAnalyzer.Analyze(new MatchReport { Positions = s })!;

        private static void Carry(PositionStream s, int from, int to, bool home, int slot, int x, int y)
        {
            for (int t = from; t <= to; t++) Put(s, home, t, slot, x, y);
            Hold(s, from, to, home, slot, x, y);
        }

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(CentralZoneAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        [Test]
        public void ASpellInTheZone_CountsOnceForTheZoneAndTheBox_AndIsUnpressedWithNoDefenderWithin3m()
        {
            PositionStream s = NewStream(40);
            Carry(s, 10, 14, true, Striker, 950, CY);
            Act(s, 15, BallActionKind.Shot, true, Striker);

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.ZoneSpells(true), Is.EqualTo(1));
            Assert.That(m.ZoneUnpressed(true), Is.EqualTo(1));
            Assert.That(m.BoxSpells(true), Is.EqualTo(1));
            Assert.That(m.ZoneSpells(false) + m.BoxSpells(false), Is.Zero, "the other side had no spell");
        }

        [Test]
        public void ADefenderWithin3mOnArrival_MakesItPressed_AndOneComingLaterDoesNot()
        {
            PositionStream s = NewStream(40);
            Carry(s, 10, 14, true, Striker, 950, CY);
            Put(s, false, 10, Defender, 950, CY + 30);   // 3 m off him on the frame he arrives
            Act(s, 15, BallActionKind.Pass, true, Striker, Mate);
            Carry(s, 20, 24, true, Mate, 945, CY);
            for (int t = 21; t <= 24; t++) Put(s, false, t, Defender, 945, CY + 10);   // closes him a frame late

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.ZoneSpells(true), Is.EqualTo(2));
            Assert.That(m.ZoneUnpressed(true), Is.EqualTo(1), "the mate was free on the frame he reached the zone");
        }

        [Test]
        public void ASpellInTheBoxOutsideTheZone_IsABoxSpellOnly()
        {
            PositionStream s = NewStream(40);
            Carry(s, 10, 14, true, Striker, 900, 520);
            Act(s, 15, BallActionKind.Cross, true, Striker, Mate);

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.BoxSpells(true), Is.EqualTo(1));
            Assert.That(m.ZoneSpells(true), Is.Zero);
        }

        [Test]
        public void ACarryIntoTheZone_IsCountedOnTheFrameItGetsThere()
        {
            PositionStream s = NewStream(40);
            Carry(s, 10, 12, true, Striker, 820, CY);
            Carry(s, 13, 15, true, Striker, 940, CY);
            Put(s, false, 11, Defender, 820, CY + 10);   // tight on him outside the zone only

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.ZoneSpells(true), Is.EqualTo(1));
            Assert.That(m.ZoneUnpressed(true), Is.EqualTo(1));
            Assert.That(m.BoxSpells(true), Is.EqualTo(1));
        }

        [Test]
        public void AFirstTimeShot_ByAManWhoNeverHeldIt_IsASpellWhereHeStood()
        {
            PositionStream s = NewStream(40);
            Carry(s, 10, 12, true, Mate, 700, CY);
            Act(s, 13, BallActionKind.Cross, true, Mate, Striker);
            for (int t = 13; t <= 20; t++) Put(s, true, t, Striker, 960, CY);
            Act(s, 18, BallActionKind.Shot, true, Striker);

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.ZoneSpells(true), Is.EqualTo(1), "the header is a spell in the zone");
            Assert.That(m.BoxSpells(true), Is.EqualTo(1));
        }

        [Test]
        public void ThePenaltyTakersSpell_IsASetPiece_AndLeftOut()
        {
            PositionStream s = NewStream(40);
            Act(s, 8, BallActionKind.Penalty, true, Striker);
            Carry(s, 10, 12, true, Striker, 940, CY);
            Act(s, 13, BallActionKind.Shot, true, Striker);

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.ZoneSpells(true) + m.BoxSpells(true), Is.Zero);
        }

        [Test]
        public void TheAwaySide_AttacksTheGoalAtXZero()
        {
            PositionStream s = NewStream(40);
            Carry(s, 10, 14, false, Striker, 100, CY);
            Act(s, 15, BallActionKind.Shot, false, Striker);

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.ZoneSpells(false), Is.EqualTo(1));
            Assert.That(m.BoxSpells(false), Is.EqualTo(1));
            Assert.That(m.ZoneSpells(true), Is.Zero);
        }

        [Test]
        public void TheTally_ReportsPerTeamMatch_AndPoolsTheUnpressedShare()
        {
            PositionStream s = NewStream(40);
            Carry(s, 10, 14, true, Striker, 950, CY);
            Act(s, 15, BallActionKind.Pass, true, Striker, Mate);
            Carry(s, 20, 24, true, Mate, 945, CY);
            Put(s, false, 20, Defender, 945, CY + 10);

            var tally = new CentralZoneTally();
            tally.Add(Measure(s));
            tally.Add(Measure(NewStream(40)));

            Assert.That(tally.ZoneSpellsPerTeamMatch, Is.EqualTo(0.5), "two spells over two matches of two sides");
            Assert.That(tally.ZoneUnpressedPercent, Is.EqualTo(50.0));
            Assert.That(tally.Format("t"), Does.Contain(RealismReference.Events.CentralZonePossessionsPerTeamMatch.Name));
        }
        // ---- issue #210: where the zone and box spells come from ----

        /// <summary>The receiver holds the ball from frame 16 to 18 at (x, y), after a pass played on frame 13.</summary>
        private static PositionStream Reception(int fromX, int fromY, BallActionKind kind, int toX, int toY)
        {
            PositionStream s = NewStream(40);
            Carry(s, 10, 12, true, Mate, fromX, fromY);
            Act(s, 13, kind, true, Mate, Striker);
            Carry(s, 16, 18, true, Striker, toX, toY);
            return s;
        }

        [Test]
        public void ACarryIntoTheZone_IsACarry_AndItsShotAndGoalAreCreditedToIt()
        {
            PositionStream s = NewStream(40);
            Carry(s, 10, 12, true, Striker, 820, CY);
            Carry(s, 13, 15, true, Striker, 940, CY);
            Act(s, 16, BallActionKind.Shot, true, Striker);
            Act(s, 17, BallActionKind.Goal, true, Striker);

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.ZoneSpells(true, ChanceSource.Carry), Is.EqualTo(1));
            Assert.That(m.BoxSpells(true, ChanceSource.Carry), Is.EqualTo(1));
            Assert.That(m.Shots(true, ChanceSource.Carry), Is.EqualTo(1));
            Assert.That(m.Goals(true, ChanceSource.Carry), Is.EqualTo(1));
            Assert.That(m.XgPermille(true, ChanceSource.Carry), Is.GreaterThan(100), "11 m out, straight on");
            Assert.That(m.ZoneCarriesFromBand(true, 1), Is.EqualTo(1), "the carry began 23 m from the goal centre");
        }

        [Test]
        public void APassReceivedInTheZone_IsAPass_AndPressedWithADefenderWithin3mOnArrival()
        {
            PositionStream s = Reception(750, CY, BallActionKind.Pass, 950, CY);
            Put(s, false, 16, Defender, 950, CY + 20);

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.ZoneSpells(true, ChanceSource.Pass), Is.EqualTo(1));
            Assert.That(m.ZonePressed(true, ChanceSource.Pass), Is.EqualTo(1));
            Assert.That(m.ZoneSpells(true, ChanceSource.Carry), Is.Zero, "he had it in the zone from his first frame");
        }

        [Test]
        public void APassStruckFromBehindTheLineToAManTakingItBeyondIt_IsAThroughBall()
        {
            // The away outfielders stand at x = 625 (the second-last line); the passer is behind it,
            // the receiver level with it on release and beyond it when he takes the ball.
            PositionStream s = Reception(600, CY, BallActionKind.Pass, 950, CY);
            Put(s, true, 12, Striker, 610, CY + 50);

            Assert.That(Measure(s).ZoneSpells(true, ChanceSource.ThroughBall), Is.EqualTo(1));
        }

        [Test]
        public void ABallFromTheWideFinalThirdIntoTheBox_IsACross()
        {
            PositionStream s = Reception(900, CY + 260, BallActionKind.Cross, 960, CY);

            Assert.That(Measure(s).ZoneSpells(true, ChanceSource.Cross), Is.EqualTo(1));
        }

        [Test]
        public void ABallPlayedBackFromTheByline_IsACutBack()
        {
            PositionStream s = Reception(1010, CY + 150, BallActionKind.Pass, 930, CY);

            Assert.That(Measure(s).ZoneSpells(true, ChanceSource.CutBack), Is.EqualTo(1));
        }

        [Test]
        public void ABallOffTheKeepersSave_IsARebound_AndOneOffTheirClearance_ASecondBall()
        {
            PositionStream s = NewStream(60);
            Carry(s, 10, 12, true, Striker, 950, CY);
            Act(s, 13, BallActionKind.Shot, true, Striker);
            Act(s, 14, BallActionKind.Save, false, Keeper);
            Carry(s, 16, 18, true, Mate, 955, CY + 20);
            Act(s, 19, BallActionKind.Pass, true, Mate, Striker);
            Act(s, 21, BallActionKind.Clearance, false, Defender);
            Carry(s, 24, 26, true, Striker, 945, CY - 20);

            CentralZoneMetrics m = Measure(s);

            Assert.That(m.ZoneSpells(true, ChanceSource.Rebound), Is.EqualTo(1));
            Assert.That(m.ZoneSpells(true, ChanceSource.SecondBall), Is.EqualTo(1));
        }

        [Test]
        public void TheTally_PrintsEverySource()
        {
            var tally = new CentralZoneTally();
            tally.Add(Measure(Reception(750, CY, BallActionKind.Pass, 950, CY)));

            string text = tally.Format("t");

            Assert.That(tally.ZoneSpellsFrom(ChanceSource.Pass), Is.EqualTo(0.5));
            foreach (ChanceSource source in System.Enum.GetValues(typeof(ChanceSource)))
                Assert.That(text, Does.Contain(source.ToString()));
        }
    }
}
