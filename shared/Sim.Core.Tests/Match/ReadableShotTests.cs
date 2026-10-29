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
    /// R12 of believable-match (issue #153): what the renderer draws of a strike. The shot line runs
    /// from the shooter to the ball and stays while a goal ends in the net or a wide shot runs past
    /// the line; a goal lights the net briefly; a save stretches the keeper toward the impact; the
    /// ball's shadow and scale follow its height. All of it read allocation-free, every repaint.
    /// </summary>
    [TestFixture]
    public class ReadableShotTests
    {
        private const int Fpm = 300; // the 5 fps stream
        private const int Frames = 90 * Fpm;
        private const int GoalHalfWidthDm = 37;
        private static readonly MatchBalance Cfg = new BalanceConfig().Match;

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

        // ------------------------------------------------------------- the shot line

        [Test]
        public void AShotThatScores_KeepsItsLineToTheBall_WhileTheBallRunsIntoTheNet()
        {
            PositionStream s = Stream();
            int f = 20 * Fpm;
            Add(s, f - 6, BallActionKind.Pass, true, 4, 9);
            Add(s, f, BallActionKind.Shot, true, 9);
            Add(s, f + 4, BallActionKind.Goal, true, 9);
            var shots = new ShotHighlight(s);

            Assert.That(shots.HoldFrames, Is.EqualTo(5), "one second of match time at 5 fps");
            Assert.That(shots.TryShotAt(f - 0.5, out _), Is.False, "nothing is drawn before the strike");

            Assert.That(shots.TryShotAt(f, out ShotMoment at), Is.True);
            Assert.That(at.StrikeFrame, Is.EqualTo(f));
            Assert.That(at.ShooterHome, Is.True);
            Assert.That(at.ShooterSlot, Is.EqualTo(9));
            Assert.That(at.Outcome, Is.EqualTo(BallActionKind.Goal));
            Assert.That(at.OutcomeFrame, Is.EqualTo(f + 4));
            Assert.That(at.Strength, Is.EqualTo(1f));

            Assert.That(shots.TryShotAt(f + 4, out ShotMoment crossing), Is.True);
            Assert.That(crossing.Strength, Is.EqualTo(1f), "full while the ball is still travelling");
            Assert.That(shots.TryShotAt(f + 4 + 2.5, out ShotMoment fading), Is.True, "the ball runs on into the net");
            Assert.That(fading.Strength, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(shots.TryShotAt(f + 4 + 5, out _), Is.False, "then the line is gone");
        }

        [Test]
        public void AWideShot_KeepsItsLine_AsTheBallRunsPastTheLine()
        {
            PositionStream s = Stream();
            int f = 33 * Fpm;
            Add(s, f, BallActionKind.Shot, false, 10);
            Add(s, f + 6, BallActionKind.Miss, false, 10);
            var shots = new ShotHighlight(s);

            Assert.That(shots.TryShotAt(f + 8, out ShotMoment m), Is.True);
            Assert.That(m.Outcome, Is.EqualTo(BallActionKind.Miss));
            Assert.That(m.ShooterHome, Is.False);
            Assert.That(m.ShooterSlot, Is.EqualTo(10));
            Assert.That(m.Strength, Is.GreaterThan(0f).And.LessThan(1f));
        }

        [TestCase(BallActionKind.Save, false)]
        [TestCase(BallActionKind.Block, false)]
        [TestCase(BallActionKind.Clearance, false)]
        public void AStrikeThatIsStopped_EndsItsLineOnTheStop(BallActionKind stop, bool stopperHome)
        {
            PositionStream s = Stream();
            int f = 50 * Fpm;
            Add(s, f, BallActionKind.Shot, true, 8);
            Add(s, f + 3, stop, stopperHome, 0);
            var shots = new ShotHighlight(s);

            Assert.That(shots.TryShotAt(f + 3, out ShotMoment m), Is.True, "the line reaches the ball where it is stopped");
            Assert.That(m.Outcome, Is.EqualTo(stop));
            Assert.That(m.Strength, Is.EqualTo(1f));
            Assert.That(shots.TryShotAt(f + 3.5, out _), Is.False, "a rebound is not the shot any more");
        }

        [Test]
        public void AStrikeNothingSettles_DropsItsLine_AfterTheLongestFlight()
        {
            PositionStream s = Stream();
            int f = 60 * Fpm;
            Add(s, f, BallActionKind.Shot, true, 9);
            var shots = new ShotHighlight(s);

            Assert.That(shots.MaxFlightFrames, Is.EqualTo(20), "four seconds of match time at 5 fps");
            Assert.That(shots.TryShotAt(f + 20, out ShotMoment m), Is.True);
            Assert.That(m.Outcome, Is.EqualTo(BallActionKind.Shot), "unsettled");
            Assert.That(m.OutcomeFrame, Is.EqualTo(-1));
            Assert.That(shots.TryShotAt(f + 20.01, out _), Is.False);
        }

        [Test]
        public void ARebound_StruckAgain_DrawsTheLatestStrike()
        {
            PositionStream s = Stream();
            int f = 70 * Fpm;
            Add(s, f, BallActionKind.Shot, true, 9);
            Add(s, f + 2, BallActionKind.Save, false, 0);
            Add(s, f + 5, BallActionKind.Shot, true, 10);
            Add(s, f + 7, BallActionKind.Goal, true, 10);
            var shots = new ShotHighlight(s);

            Assert.That(shots.ShotCount, Is.EqualTo(2));
            Assert.That(shots.TryShotAt(f + 4, out _), Is.False, "between the save and the rebound");
            Assert.That(shots.TryShotAt(f + 6, out ShotMoment m), Is.True);
            Assert.That(m.StrikeFrame, Is.EqualTo(f + 5));
            Assert.That(m.ShooterSlot, Is.EqualTo(10));
        }

        // ------------------------------------------------------------- the net

        [TestCase(true, Pitch.LengthDm, 1)]
        [TestCase(false, 0, -1)]
        public void AGoal_LightsTheNetItWentInto_Briefly(bool home, int lineX, int beyond)
        {
            PositionStream s = Stream();
            int f = 25 * Fpm;
            Add(s, f, BallActionKind.Shot, home, 9);
            Add(s, f + 4, BallActionKind.Goal, home, 9);
            Add(s, f + 4 + 45, BallActionKind.Kickoff, !home, 9);
            var shots = new ShotHighlight(s);

            Assert.That(shots.GoalCount, Is.EqualTo(1));
            Assert.That(shots.NetFrames, Is.EqualTo(10), "two seconds of match time at 5 fps");
            Assert.That(shots.TryNetAt(f + 3.9, out _), Is.False, "not before the ball is over the line");

            Assert.That(shots.TryNetAt(f + 4, out NetMoment net), Is.True);
            Assert.That(net.GoalFrame, Is.EqualTo(f + 4));
            Assert.That(net.LineX, Is.EqualTo(lineX), "the goal the scorers attack");
            Assert.That(net.BackX, Is.EqualTo(lineX + beyond * Cfg.GoalNetDepthDm), "as deep as the simulated net");
            Assert.That(net.MinY, Is.EqualTo(Pitch.CenterY - GoalHalfWidthDm));
            Assert.That(net.MaxY, Is.EqualTo(Pitch.CenterY + GoalHalfWidthDm));
            Assert.That(net.Strength, Is.EqualTo(1f));

            Assert.That(shots.TryNetAt(f + 4 + 5, out NetMoment mid), Is.True);
            Assert.That(mid.Strength, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(shots.TryNetAt(f + 4 + 10, out _), Is.False, "the highlight is brief");
        }

        [Test]
        public void SavesMissesAndBlocks_LightNoNet()
        {
            PositionStream s = Stream();
            int f = 10 * Fpm;
            Add(s, f, BallActionKind.Shot, true, 9);
            Add(s, f + 2, BallActionKind.Save, false, 0);
            Add(s, f + 300, BallActionKind.Shot, false, 7);
            Add(s, f + 303, BallActionKind.Miss, false, 7);
            Add(s, f + 600, BallActionKind.Shot, true, 9);
            Add(s, f + 601, BallActionKind.Block, false, 4);
            var shots = new ShotHighlight(s);

            Assert.That(shots.GoalCount, Is.Zero);
            for (double pos = f; pos < f + 700; pos += 0.5)
                Assert.That(shots.TryNetAt(pos, out _), Is.False, $"position {pos}");
        }

        [Test]
        public void NothingIsDrawn_AcrossTheChangeOfEnds()
        {
            PositionStream s = Stream();
            int h = 45 * Fpm;
            Add(s, h - 6, BallActionKind.Shot, true, 9);
            Add(s, h - 4, BallActionKind.Goal, true, 9);
            Add(s, h, BallActionKind.HalfTime, true, -1);
            var shots = new ShotHighlight(s);

            Assert.That(shots.TryShotAt(h - 1.01, out _), Is.True, "still the first-half picture");
            Assert.That(shots.TryNetAt(h - 1.01, out _), Is.True);
            Assert.That(shots.TryShotAt(h - 1, out _), Is.False, "from here the picture is mirrored");
            Assert.That(shots.TryNetAt(h - 1, out _), Is.False);
            Assert.That(shots.TryNetAt(h + 1, out _), Is.False);
        }

        [Test]
        public void TheTimings_AreMatchTime_OnAnOldTwoFpsReplay()
        {
            var shots = new ShotHighlight(Stream(120));
            Assert.That(shots.HoldFrames, Is.EqualTo(2));
            Assert.That(shots.MaxFlightFrames, Is.EqualTo(8));
            Assert.That(shots.NetFrames, Is.EqualTo(4));
        }

        // ------------------------------------------------------------- the dive

        [Test]
        public void TheDive_ReachesTowardTheImpact_AsFarAsTheKeeperDives()
        {
            Assert.That(SaveHighlight.DiveToward(100, 340, 100, 360, 100, 300, 20, out int x, out int y), Is.True);
            Assert.That((x, y), Is.EqualTo((100, 320)), "toward the ball, by the reach");

            Assert.That(SaveHighlight.DiveToward(100, 340, 100, 360, 100, 100, 20, out x, out y), Is.True);
            Assert.That((x, y), Is.EqualTo((100, 320)), "a ball far beyond him: the same stretch");

            Assert.That(SaveHighlight.DiveToward(100, 340, 100, 360, 130, 380, 25, out x, out y), Is.True);
            Assert.That((x, y), Is.EqualTo((115, 360)), "on the diagonal: 3-4-5");
        }

        [Test]
        public void AKeeperOnTheBall_DivesTheWayHeWasGoing()
        {
            Assert.That(SaveHighlight.DiveToward(100, 340, 130, 340, 100, 340, 20, out int x, out int y), Is.True);
            Assert.That((x, y), Is.EqualTo((80, 340)), "from where he set off, through the impact");

            Assert.That(SaveHighlight.DiveToward(100, 340, 100, 340, 100, 340, 20, out _, out _), Is.False,
                "standing on the ball: no direction to draw");
        }

        [Test]
        public void TheDiveReach_IsTheSimulatedDive()
        {
            var saves = new SaveHighlight(Stream());
            Assert.That(saves.DiveReachDm, Is.EqualTo(Cfg.KeeperDiveFromDm));
        }

        // ------------------------------------------------------------- the ball's height

        [Test]
        public void ABallOnTheGrass_IsDrawnAtItsSize_OnItsShadow()
        {
            BallLook ground = BallLook.At(0f);
            Assert.That(ground.Scale, Is.EqualTo(1f));
            Assert.That(ground.LiftDm, Is.EqualTo(0f));
            Assert.That(ground.ShadowScale, Is.EqualTo(1f));
            Assert.That(ground.ShadowAlpha, Is.EqualTo(BallLook.ShadowAlphaGround));
        }

        [Test]
        public void ARisingBall_GrowsAndLifts_OffAFadingShadow_UpToAFullHeight()
        {
            BallLook low = BallLook.At(15f), high = BallLook.At(BallLook.FullHeightDm), sky = BallLook.At(400f);

            Assert.That(low.Scale, Is.GreaterThan(1f).And.LessThan(high.Scale));
            Assert.That(low.LiftDm, Is.GreaterThan(0f).And.LessThan(high.LiftDm));
            Assert.That(low.ShadowAlpha, Is.LessThan(BallLook.ShadowAlphaGround).And.GreaterThan(high.ShadowAlpha));
            Assert.That(low.ShadowScale, Is.LessThan(1f).And.GreaterThan(high.ShadowScale));

            Assert.That(high.Scale, Is.EqualTo(BallLook.MaxScale));
            Assert.That(high.LiftDm, Is.EqualTo(BallLook.MaxLiftDm));
            Assert.That(high.ShadowAlpha, Is.EqualTo(BallLook.ShadowAlphaHigh));
            Assert.That(sky, Is.EqualTo(high), "capped, so a lob does not leave the pitch");
            Assert.That(BallLook.At(-5f), Is.EqualTo(BallLook.At(0f)));
        }

        [Test]
        public void TheHeight_IsInterpolatedBetweenFrames_AndZeroOnAnOldReplay()
        {
            int[] z = { 0, 10, 30 };
            Assert.That(BallLook.HeightAt(z, 0), Is.EqualTo(0f));
            Assert.That(BallLook.HeightAt(z, 1.5), Is.EqualTo(20f).Within(1e-4f));
            Assert.That(BallLook.HeightAt(z, 2), Is.EqualTo(30f));
            Assert.That(BallLook.HeightAt(z, 7.2), Is.EqualTo(30f), "past the end: the last frame");
            Assert.That(BallLook.HeightAt(z, -1), Is.EqualTo(0f));
            Assert.That(BallLook.HeightAt(Array.Empty<int>(), 12.5), Is.EqualTo(0f), "no height recorded: on the grass");
        }

        // ------------------------------------------------------------- a real match

        [Test]
        public void ARealMatch_DrawsEveryGoalInItsNet_EveryWideShotPastTheLine_AndEveryDiveTowardTheBall()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            int goals = 0, wides = 0, dives = 0;
            for (ulong seed = 14_800; seed < 14_806 && (goals == 0 || wides == 0 || dives == 0); seed++)
            {
                MatchReport report = new MatchEngine().Simulate(
                    LineupSelector.BestEleven(league.Clubs[9]),
                    LineupSelector.BestEleven(league.Clubs[10]), new Pcg32(seed));
                PositionStream s = report.Positions!.Unpack();
                goals += Goals(s);
                wides += Wides(s);
                dives += Dives(s);
            }

            TestContext.Out.WriteLine($"[readable-shot] goals {goals}, wide shots {wides}, dives {dives}");
            Assert.That(goals, Is.GreaterThan(0));
            Assert.That(wides, Is.GreaterThan(0));
            Assert.That(dives, Is.GreaterThan(0));
        }

        private static int Goals(PositionStream s)
        {
            var shots = new ShotHighlight(s);
            List<BallAction> actions = s.Actions;
            int read = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction goal = actions[i];
                if (goal.Kind != BallActionKind.Goal) continue;
                read++;

                Assert.That(shots.TryNetAt(goal.Tick, out NetMoment net), Is.True, $"goal at frame {goal.Tick}");
                Assert.That(net.LineX, Is.EqualTo(goal.Home ? Pitch.LengthDm : 0));
                for (int t = goal.Tick; t < goal.Tick + shots.NetFrames && t < s.TickCount; t++)
                {
                    if (!shots.TryNetAt(t, out _)) break;
                    int x = s.BallXY[t * 2], y = s.BallXY[t * 2 + 1];
                    Assert.That(x, Is.InRange(Math.Min(net.LineX, net.BackX), Math.Max(net.LineX, net.BackX)),
                        $"goal at frame {goal.Tick}: the lit net misses the ball on frame {t}");
                    Assert.That(y, Is.InRange(net.MinY, net.MaxY), $"goal at frame {goal.Tick}: wide of the net on frame {t}");
                }

                if (i > 0 && actions[i - 1].Kind == BallActionKind.Shot)
                {
                    Assert.That(shots.TryShotAt(goal.Tick, out ShotMoment line), Is.True, $"goal at frame {goal.Tick}");
                    Assert.That(line.StrikeFrame, Is.EqualTo(actions[i - 1].Tick));
                    Assert.That(line.Outcome, Is.EqualTo(BallActionKind.Goal));
                }
            }

            return read;
        }

        private static int Wides(PositionStream s)
        {
            var shots = new ShotHighlight(s);
            List<BallAction> actions = s.Actions;
            int read = 0;
            for (int i = 1; i < actions.Count; i++)
            {
                BallAction miss = actions[i], shot = actions[i - 1];
                if (miss.Kind != BallActionKind.Miss || shot.Kind != BallActionKind.Shot) continue;
                int t = miss.Tick + 1;
                if (!shots.TryShotAt(t, out ShotMoment line)) continue;   // the whistle cut it short
                read++;

                Assert.That(line.StrikeFrame, Is.EqualTo(shot.Tick));
                Assert.That(line.Outcome, Is.EqualTo(BallActionKind.Miss));
                int lineX = shot.Home ? Pitch.LengthDm : 0;
                int depth = lineX == 0 ? -s.BallXY[t * 2] : s.BallXY[t * 2] - lineX;
                Assert.That(depth, Is.GreaterThan(0), $"miss at frame {miss.Tick}: the line ends short of the goal line");
            }

            return read;
        }

        private static int Dives(PositionStream s)
        {
            var saves = new SaveHighlight(s);
            int read = 0;
            foreach (BallAction a in s.Actions.Where(a => a.Kind == BallActionKind.Save))
            {
                Assert.That(saves.TryAt(a.Tick, out SaveMoment m), Is.True, $"save at frame {a.Tick}");
                int[] side = m.KeeperHome ? s.HomeXY : s.AwayXY;
                PitchPoint keeper = s.PlayerAt(side, m.SaveFrame, m.KeeperSlot);
                PitchPoint from = s.PlayerAt(side, m.DiveFrom, m.KeeperSlot);
                PitchPoint impact = s.BallAt(m.SaveFrame);
                if (!SaveHighlight.DiveToward(keeper.X, keeper.Y, from.X, from.Y, impact.X, impact.Y,
                        saves.DiveReachDm, out int x, out int y))
                    continue;
                read++;

                Assert.That(Distance(keeper.X, keeper.Y, x, y), Is.EqualTo(saves.DiveReachDm).Within(1.5),
                    $"save at frame {a.Tick}: the stretch is the dive's reach");
                if (impact.X != keeper.X || impact.Y != keeper.Y)
                {
                    long dot = (long)(x - keeper.X) * (impact.X - keeper.X) + (long)(y - keeper.Y) * (impact.Y - keeper.Y);
                    Assert.That(dot, Is.GreaterThan(0), $"save at frame {a.Tick}: the dive does not point at the ball");
                }
            }

            return read;
        }

        private static double Distance(int ax, int ay, int bx, int by)
        {
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ------------------------------------------------------------- per frame

        [Test]
        public void ReadingTheShotPictureEveryFrame_AllocatesNothing()
        {
            PositionStream s = Stream();
            for (int minute = 1; minute < 90; minute += 3)
            {
                int f = minute * Fpm;
                Add(s, f, BallActionKind.Shot, minute % 2 == 0, 9);
                Add(s, f + 4, minute % 6 == 1 ? BallActionKind.Goal : BallActionKind.Miss, minute % 2 == 0, 9);
            }

            Add(s, 46 * Fpm + 2, BallActionKind.HalfTime, true, -1);
            s.Actions.Sort((x, y) => x.Tick.CompareTo(y.Tick));
            int[] z = new int[Frames];
            for (int t = 0; t < Frames; t++)
                z[t] = t % 97;
            var shots = new ShotHighlight(s);
            shots.TryShotAt(0, out _);
            shots.TryNetAt(0, out _);
            BallLook.At(BallLook.HeightAt(z, 0));
            SaveHighlight.DiveToward(0, 0, 1, 1, 5, 5, 20, out _, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            int lit = 0;
            float sum = 0f;
            for (double pos = 0; pos < Frames; pos += 0.37)
            {
                if (shots.TryShotAt(pos, out ShotMoment m) && m.Strength > 0f) lit++;
                if (shots.TryNetAt(pos, out NetMoment n) && n.Strength > 0f) lit++;
                sum += BallLook.At(BallLook.HeightAt(z, pos)).Scale;
                if (SaveHighlight.DiveToward(0, 0, 3, 4, (int)pos % 50, 7, 20, out int x, out _)) sum += x;
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0), "the renderer reads this sixty times a second");
            Assert.That(lit, Is.GreaterThan(0));
            Assert.That(sum, Is.GreaterThan(0f));
        }
    }
}
