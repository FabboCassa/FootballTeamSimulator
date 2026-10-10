using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Broadcast;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The visible keeper save (spec real-match-and-playing-styles R7): the renderer's reading of
    /// which save to accent at a playback position, the keeper's movement toward the ball it draws,
    /// and the commentary line that goes with it.
    /// </summary>
    [TestFixture]
    public class SaveHighlightTests
    {
        private const int Fpm = 300; // the 5 fps stream
        private const int Frames = 90 * Fpm;

        private static PositionStream Stream(int fpm = Fpm) => new PositionStream
        {
            TicksPerMinute = fpm,
            PlayerCount = 11,
            LastTick = 90 * fpm - 1,
            BallXY = new int[90 * fpm * 2],
            Owner = new int[90 * fpm]
        };

        private static void Add(PositionStream s, int frame, BallActionKind kind, bool home, int slot, int target = -1) =>
            s.Actions.Add(new BallAction(frame, kind, home, slot, target));

        [Test]
        public void ASave_AccentsTheKeeperFromTheSaveFrame_AndFadesOut()
        {
            PositionStream s = Stream();
            int f = 30 * Fpm;
            Add(s, f - 8, BallActionKind.Pass, true, 5, 9);
            Add(s, f, BallActionKind.Shot, true, 9);
            Add(s, f + 3, BallActionKind.Save, false, 0);
            var saves = new SaveHighlight(s);

            Assert.That(saves.Count, Is.EqualTo(1));
            Assert.That(saves.TryAt(f + 2.9, out _), Is.False, "nothing is accented before the keeper gets there");

            Assert.That(saves.TryAt(f + 3, out SaveMoment at), Is.True);
            Assert.That(at.SaveFrame, Is.EqualTo(f + 3));
            Assert.That(at.KeeperHome, Is.False, "the keeper is on the side that did not shoot");
            Assert.That(at.KeeperSlot, Is.EqualTo(0));
            Assert.That(at.DiveFrom, Is.EqualTo(f), "his movement toward the ball is drawn from the strike");
            Assert.That(at.Strength, Is.EqualTo(1f));

            int length = saves.LengthFrames;
            Assert.That(length, Is.EqualTo(10), "two seconds of match time at 5 fps");
            Assert.That(saves.TryAt(f + 3 + length / 2.0, out SaveMoment mid), Is.True);
            Assert.That(mid.Strength, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(saves.TryAt(f + 3 + length - 0.01, out SaveMoment late), Is.True);
            Assert.That(late.Strength, Is.GreaterThan(0f).And.LessThan(0.01f));
            Assert.That(saves.TryAt(f + 3 + length, out _), Is.False, "the accent is brief");

            IReadOnlyList<CommentaryLine> lines = CommentaryBuilder.Build(
                new MatchReport { HomeClubId = 1, AwayClubId = 2, Positions = s }, BroadcastTimeline.Empty);
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines[0].Icon, Is.EqualTo(CommentaryIcon.Save));
            Assert.That(lines[0].Frame, Is.EqualTo(at.SaveFrame), "the commentary line lands with the accent");
        }

        [Test]
        public void OnlySavesAreAccented_NotGoalsMissesOrBlocks()
        {
            PositionStream s = Stream();
            int f = 10 * Fpm;
            Add(s, f, BallActionKind.Shot, true, 9);
            Add(s, f + 2, BallActionKind.Goal, true, 9);
            Add(s, f + 600, BallActionKind.Shot, false, 7);
            Add(s, f + 603, BallActionKind.Miss, false, 7);
            Add(s, f + 1200, BallActionKind.Shot, true, 8);
            Add(s, f + 1201, BallActionKind.Block, false, 4);
            var saves = new SaveHighlight(s);

            Assert.That(saves.Count, Is.EqualTo(0));
            for (int t = 0; t < Frames; t += 7)
                Assert.That(saves.TryAt(t, out _), Is.False, $"frame {t}");
        }

        [Test]
        public void TheDive_IsBounded_WhenTheStrikeIsFarBackOrNotOnRecord()
        {
            PositionStream s = Stream();
            int f = 50 * Fpm;
            Add(s, f, BallActionKind.Shot, false, 9);
            Add(s, f + 60, BallActionKind.Save, true, 0);    // a strike twelve seconds earlier
            Add(s, f + 900, BallActionKind.Save, true, 0);   // a save with no strike of its own
            Add(s, 1, BallActionKind.Save, false, 0);         // at kick-off, nothing before it
            s.Actions.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            var saves = new SaveHighlight(s);

            Assert.That(saves.TryAt(f + 60, out SaveMoment far), Is.True);
            Assert.That(far.DiveFrom, Is.EqualTo(f + 60 - saves.MaxDiveFrames));

            Assert.That(saves.TryAt(f + 900, out SaveMoment none), Is.True);
            Assert.That(none.DiveFrom, Is.EqualTo(f + 900 - saves.LengthFrames));

            Assert.That(saves.TryAt(1, out SaveMoment first), Is.True);
            Assert.That(first.DiveFrom, Is.EqualTo(0));
        }

        [Test]
        public void ASaveJustAfterHalfTime_NeverDrawsThePathAcrossTheChangeOfEnds()
        {
            PositionStream s = Stream();
            int h = 45 * Fpm;
            Add(s, h - 3, BallActionKind.Shot, true, 9);      // a strike just before the whistle
            Add(s, h, BallActionKind.HalfTime, true, -1);
            Add(s, h + 2, BallActionKind.Save, false, 0);
            Add(s, h + 400, BallActionKind.Shot, false, 9);
            Add(s, h + 401, BallActionKind.Miss, false, 9);
            Add(s, h + 404, BallActionKind.Save, true, 0);   // no strike of its own, well into the half
            Add(s, h - 30, BallActionKind.Shot, false, 9);
            Add(s, h - 28, BallActionKind.Save, true, 0);    // a first-half save is not clamped by the whistle
            s.Actions.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            var saves = new SaveHighlight(s);

            Assert.That(saves.TryAt(h + 2, out SaveMoment early), Is.True);
            Assert.That(early.DiveFrom, Is.EqualTo(h), "the picture is mirrored from the whistle on");

            Assert.That(saves.TryAt(h + 404, out SaveMoment later), Is.True);
            Assert.That(later.DiveFrom, Is.EqualTo(h + 404 - saves.LengthFrames));

            Assert.That(saves.TryAt(h - 28, out SaveMoment first), Is.True);
            Assert.That(first.DiveFrom, Is.EqualTo(h - 30));
        }

        [Test]
        public void AFirstHalfSave_JustBeforeTheWhistle_EndsItsAccentBeforeThePictureTurns()
        {
            PositionStream s = Stream();
            int h = 45 * Fpm;
            Add(s, h - 6, BallActionKind.Shot, true, 9);
            Add(s, h - 4, BallActionKind.Save, false, 0);    // two of its ten frames would fall after the whistle
            Add(s, h, BallActionKind.HalfTime, true, -1);
            var saves = new SaveHighlight(s);

            Assert.That(saves.TryAt(h - 4, out SaveMoment at), Is.True);
            Assert.That(at.DiveFrom, Is.EqualTo(h - 6));
            Assert.That(saves.TryAt(h - 1.01, out _), Is.True, "still accented while the picture is first-half");
            Assert.That(saves.TryAt(h - 1, out _), Is.False, "from here the tokens are drawn on the mirrored frame");
            Assert.That(saves.TryAt(h, out _), Is.False);
            Assert.That(saves.TryAt(h + 3.5, out _), Is.False);
        }

        [Test]
        public void ASaveOnTheWhistleFrame_IsASecondHalfSave_WithNoPathBehindIt()
        {
            PositionStream s = Stream();
            int h = 45 * Fpm;
            Add(s, h - 2, BallActionKind.Shot, true, 9);
            Add(s, h, BallActionKind.HalfTime, true, -1);
            Add(s, h, BallActionKind.Save, false, 0);
            var saves = new SaveHighlight(s);

            Assert.That(saves.TryAt(h - 0.5, out _), Is.False);
            Assert.That(saves.TryAt(h, out SaveMoment m), Is.True);
            Assert.That(m.DiveFrom, Is.EqualTo(h), "no frame of the first half is joined to it");
            Assert.That(saves.TryAt(h + 9.5, out _), Is.True);
        }

        [Test]
        public void ARebound_SavedAgain_AccentsTheLatestSave()
        {
            PositionStream s = Stream();
            int f = 70 * Fpm;
            Add(s, f, BallActionKind.Shot, true, 9);
            Add(s, f + 2, BallActionKind.Save, false, 0);
            Add(s, f + 5, BallActionKind.Shot, true, 10);
            Add(s, f + 6, BallActionKind.Save, false, 0);
            var saves = new SaveHighlight(s);

            Assert.That(saves.TryAt(f + 4, out SaveMoment first), Is.True);
            Assert.That(first.SaveFrame, Is.EqualTo(f + 2));
            Assert.That(saves.TryAt(f + 6.5, out SaveMoment second), Is.True);
            Assert.That(second.SaveFrame, Is.EqualTo(f + 6));
            Assert.That(second.DiveFrom, Is.EqualTo(f + 5), "the second save dives from the rebound's strike");
        }

        [Test]
        public void TheAccent_LastsTheSameMatchTime_OnAnOldTwoFpsReplay()
        {
            var saves = new SaveHighlight(Stream(120));
            Assert.That(saves.LengthFrames, Is.EqualTo(4), "two seconds of match time at 2 fps");
            Assert.That(saves.MaxDiveFrames, Is.EqualTo(6), "three seconds of match time at 2 fps");
        }

        [Test]
        public void ARealMatch_AccentsEverySave_WithItsKeeper()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20261010));
            Club home = league.Clubs[0], away = league.Clubs[1];
            var plan = new MatchPlan(new MatchInput(LineupSelector.BestEleven(home), LineupSelector.BestEleven(away)));
            MatchReport report = new MatchEngine(new BalanceConfig()).Simulate(plan, new Pcg32(8686));
            PositionStream s = report.Positions!;
            List<BallAction> recorded = s.Actions.Where(a => a.Kind == BallActionKind.Save).ToList();
            Assume.That(recorded, Is.Not.Empty, "this seed must produce a save");

            var saves = new SaveHighlight(s);

            Assert.That(saves.Count, Is.EqualTo(recorded.Count));
            foreach (BallAction a in recorded)
            {
                Assert.That(saves.TryAt(a.Tick, out SaveMoment m), Is.True, $"save at frame {a.Tick}");
                Assert.That(m.SaveFrame, Is.EqualTo(a.Tick));
                Assert.That(m.KeeperHome, Is.EqualTo(a.Home));
                Assert.That(m.KeeperSlot, Is.EqualTo(a.Slot));
                Assert.That(m.DiveFrom, Is.InRange(a.Tick - saves.MaxDiveFrames, a.Tick));
            }
        }

        [Test]
        public void ReadingTheAccentEveryFrame_AllocatesNothing()
        {
            PositionStream s = Stream();
            for (int minute = 1; minute < 90; minute += 3)
            {
                int f = minute * Fpm;
                Add(s, f, BallActionKind.Shot, minute % 2 == 0, 9);
                Add(s, f + 4, BallActionKind.Save, minute % 2 != 0, 0);
            }

            Add(s, 46 * Fpm + 2, BallActionKind.HalfTime, true, -1); // inside the accent of the save at 46'
            s.Actions.Sort((x, y) => x.Tick.CompareTo(y.Tick));
            var saves = new SaveHighlight(s);
            saves.TryAt(0, out _); // warm-up, so JIT work is not counted

            long before = GC.GetAllocatedBytesForCurrentThread();
            int lit = 0;
            for (double pos = 0; pos < Frames; pos += 0.37)
                if (saves.TryAt(pos, out SaveMoment m) && m.Strength > 0f)
                    lit++;
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0), "the renderer reads this sixty times a second");
            Assert.That(lit, Is.GreaterThan(0));
        }
    }
}
