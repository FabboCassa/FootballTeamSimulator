using System.Linq;
using NUnit.Framework;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The striker offside-position reading of the free-ball spec (R2, doc §10.3), pinned against
    /// hand-built streams. Both sides' slot 10 is the Striker. On the scripted stream the away back
    /// line (its stacked outfielders) stands at x = 625 with the keeper at 1010, so for a home
    /// striker the second-last opponent is at 625; the home line stands at 425, the second-last
    /// opponent of an away striker.
    /// </summary>
    [TestFixture]
    public class StrikerOffsideAnalyzerTests
    {
        private const int CY = Pitch.CenterY;
        private const int StrikerSlot = 10;

        private static readonly PositionRole[] Roles = Enumerable.Range(0, Players)
            .Select(i => i == 0 ? PositionRole.Goalkeeper : i == StrikerSlot ? PositionRole.Striker : PositionRole.CentralMidfielder)
            .ToArray();

        private static StrikerOffsideMetrics Measure(PositionStream s) =>
            StrikerOffsideAnalyzer.Analyze(new MatchReport { Positions = s }, Roles, Roles)!;

        private static void Striker(PositionStream s, bool home, int from, int to, int x)
        {
            for (int t = from; t <= to; t++) Put(s, home, t, StrikerSlot, x, CY);
        }

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(StrikerOffsideAnalyzer.Analyze(new MatchReport(), Roles, Roles), Is.Null);
        }

        [Test]
        public void BeyondTheLineTheBallAndHalfway_IsOffsidePosition()
        {
            PositionStream s = NewStream(10);
            Hold(s, 0, 9, true, 5, 600, CY);
            Striker(s, true, 0, 9, 800);

            StrikerOffsideMetrics m = Measure(s);

            Assert.That(m.PossessionFrames, Is.EqualTo(10), "home holds all ten frames; away never has it");
            Assert.That(m.OffsideFrames, Is.EqualTo(10));
            Assert.That(m.OffsidePercent, Is.EqualTo(100.0));
        }

        [TestCase(625 + 3, 0)]
        [TestCase(625 + 4, 10)]
        public void TheMargin_IsMoreThanThirtyCentimetres(int strikerX, int offsideFrames)
        {
            PositionStream s = NewStream(10);
            Hold(s, 0, 9, true, 5, 560, CY);
            Striker(s, true, 0, 9, strikerX);

            Assert.That(Measure(s).OffsideFrames, Is.EqualTo(offsideFrames));
        }

        [Test]
        public void BehindTheBall_IsNotOffsidePosition()
        {
            PositionStream s = NewStream(10);
            Hold(s, 0, 9, true, 5, 850, CY);
            Striker(s, true, 0, 9, 800);

            StrikerOffsideMetrics m = Measure(s);

            Assert.That(m.PossessionFrames, Is.EqualTo(10));
            Assert.That(m.OffsideFrames, Is.EqualTo(0));
        }

        [Test]
        public void InHisOwnHalf_IsNotOffsidePosition()
        {
            PositionStream s = NewStream(10);
            for (int t = 0; t < 10; t++)
            for (int i = 1; i < Players; i++)
                Put(s, false, t, i, 400, CY);                 // the away line pushed up to 400
            Hold(s, 0, 9, true, 5, 300, CY);
            Striker(s, true, 0, 9, 450);                      // beyond the line and the ball, short of 525

            Assert.That(Measure(s).OffsideFrames, Is.EqualTo(0));
        }

        [Test]
        public void AnAwayStriker_ReadsTowardTheHomeGoal()
        {
            PositionStream s = NewStream(10);
            Hold(s, 0, 9, false, 5, 500, CY);
            Striker(s, false, 0, 9, 300);                     // past the home line at 425, the ball and halfway

            StrikerOffsideMetrics m = Measure(s);

            Assert.That(m.PossessionFrames, Is.EqualTo(10));
            Assert.That(m.OffsideFrames, Is.EqualTo(10));
        }

        [Test]
        public void Possession_CarriesThroughALooseBall_UntilTheOtherSideHoldsIt()
        {
            PositionStream s = NewStream(15);
            Hold(s, 0, 2, true, 5, 600, CY);                  // frames 3-9 loose: still home's
            Hold(s, 10, 14, false, 5, 600, CY);
            Striker(s, true, 0, 14, 800);

            StrikerOffsideMetrics m = Measure(s);

            Assert.That(m.PossessionFrames, Is.EqualTo(15), "10 home striker-frames, then 5 away");
            Assert.That(m.OffsideFrames, Is.EqualTo(10), "the away striker stands in his stack, onside");
        }

        [Test]
        public void NobodysBall_AndADeadBall_AreNotCounted()
        {
            PositionStream s = NewStream(15);
            Hold(s, 3, 6, true, 5, 600, CY);                  // frames 0-2: nobody has held it yet
            Act(s, 7, BallActionKind.ThrowIn, true);          // and nobody takes the throw-in
            Striker(s, true, 0, 14, 800);

            StrikerOffsideMetrics m = Measure(s);

            Assert.That(m.PossessionFrames, Is.EqualTo(4));
            Assert.That(m.OffsideFrames, Is.EqualTo(4));
        }

        [Test]
        public void ASentOffStriker_IsNoLongerCounted()
        {
            PositionStream s = NewStream(10);
            Hold(s, 0, 9, true, 5, 600, CY);
            Striker(s, true, 0, 9, 800);
            Act(s, 5, BallActionKind.RedCard, true, StrikerSlot);

            Assert.That(Measure(s).PossessionFrames, Is.EqualTo(5));
        }

        [Test]
        public void ASentOffDefender_LeavesTheLine()
        {
            PositionStream s = NewStream(10);
            for (int t = 0; t < 10; t++)
            {
                for (int i = 1; i < Players; i++) Put(s, false, t, i, 700, CY);
                Put(s, false, t, 1, 900, CY);                 // the last man: second-last opponent at 900
            }

            Hold(s, 0, 9, true, 5, 600, CY);
            Striker(s, true, 0, 9, 800);
            Act(s, 5, BallActionKind.RedCard, false, 1);      // from frame 5 the second-last is at 700

            Assert.That(Measure(s).OffsideFrames, Is.EqualTo(5));
        }

        [Test]
        public void ASideWithNoStrikerRole_CountsNoFrame()
        {
            PositionStream s = NewStream(10);
            Hold(s, 0, 9, true, 5, 600, CY);
            Striker(s, true, 0, 9, 800);
            PositionRole[] noStriker = Roles.Select(r => r == PositionRole.Striker ? PositionRole.Winger : r).ToArray();

            StrikerOffsideMetrics m = StrikerOffsideAnalyzer.Analyze(new MatchReport { Positions = s }, noStriker, Roles)!;

            Assert.That(m.PossessionFrames, Is.EqualTo(0));
        }

        [Test]
        public void Offsides_CountEveryCallOfBothSides_PerTeamMatch()
        {
            PositionStream s = NewStream(10);
            Act(s, 2, BallActionKind.Offside, true, StrikerSlot);
            Act(s, 4, BallActionKind.Offside, true, StrikerSlot);
            Act(s, 6, BallActionKind.Offside, false, StrikerSlot);

            StrikerOffsideMetrics m = Measure(s);

            Assert.That(m.Offsides, Is.EqualTo(3));
            Assert.That(m.OffsidesPerTeamMatch, Is.EqualTo(1.5));
        }

        [Test]
        public void Tally_PoolsMatches_AndPrintsTheBands()
        {
            PositionStream s = NewStream(10);
            Hold(s, 0, 9, true, 5, 600, CY);
            Striker(s, true, 0, 4, 800);                      // offside on 5 of 10 frames
            Act(s, 2, BallActionKind.Offside, true, StrikerSlot);
            var tally = new StrikerOscillationTally();
            tally.Add(Measure(s));
            tally.Add(Measure(s));

            var rows = tally.Rows();

            Assert.That(tally.Striker.Matches, Is.EqualTo(2));
            Assert.That(rows[0].Band.Name, Is.EqualTo(RealismReference.Tracking.StrikerOffsidePercent.Name));
            Assert.That(rows[0].Value, Is.EqualTo(50.0));
            Assert.That(rows[0].InBand, Is.False);
            Assert.That(rows[1].Band.Name, Is.EqualTo(RealismReference.PitchEvents.OffsidesPerTeamMatch.Name));
            Assert.That(rows[1].Value, Is.EqualTo(0.5), "2 offsides in 2 matches: half a call per team-match");
            Assert.That(rows[1].InBand, Is.False);
            Assert.That(rows[2].Band.Name, Is.EqualTo(RealismReference.Tracking.ReversalsPerPlayerMinute.Name));

            string text = tally.Format("scripted");
            Assert.That(text, Does.Contain("report only"));
            Assert.That(text, Does.Contain("scripted (2 matches)"));
            Assert.That(text, Does.Contain("OUT"));
        }
    }
}
