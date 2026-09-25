using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R6 on the pitch: with Brain = V11 the set pieces are read off the POSITION STREAM, the
    /// same picture the viewer gets — a penalty has a run-up, a free kick in range has a wall, a
    /// corner has men on the near post, the far post and the edge of the box, and throw-ins and
    /// goal kicks go short or long as the Tempo instruction says.
    /// </summary>
    [TestFixture]
    public class V11SetPieceTests
    {
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        // ------------------------------------------------------------ penalties

        /// <summary>
        /// The taker stands back and RUNS at the ball: in the frames before the strike he is
        /// visibly coming in from a distance, getting closer every frame. On V10 he stands on the
        /// ball until the kick, so this is the V11 behaviour and not a coincidence of steering.
        /// Penalties are made common here (every foul in the box stands) so a few matches carry
        /// several of them.
        /// </summary>
        [Test]
        public void Penalty_HasVisibleRunUpFrames_BeforeTheStrike()
        {
            MatchBalance cfg = V11();
            cfg.FoulInBoxPermille = 1000;

            int penalties = 0, withRunUp = 0;
            foreach (PositionStream s in PlayMany(cfg, 8, 35_000, null))
            {
                List<BallAction> a = s.Actions;
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Kind != BallActionKind.Penalty) continue;
                    int next = NextKick(a, i);
                    if (next < 0) continue;

                    penalties++;
                    Assert.That(a[next].Kind, Is.EqualTo(BallActionKind.Shot), "a penalty is struck");
                    Assert.That(a[next].Slot, Is.EqualTo(a[i].Slot), "by the man who stepped up");

                    if (RunUpFrames(s, a[i].Home, a[i].Slot, a[i].Tick, a[next].Tick) >= 2) withRunUp++;
                }
            }

            TestContext.Out.WriteLine($"[v11-penalty] {withRunUp}/{penalties} penalties with a run-up of >= 2 frames");
            Assert.That(penalties, Is.GreaterThanOrEqualTo(5), "the sample needs penalties in it");
            Assert.That(withRunUp, Is.EqualTo(penalties), "every penalty has a visible run-up");
        }

        /// <summary>
        /// Frames, ending at the strike, in which the taker is at least two metres off the ball and
        /// closer than he was a frame earlier. The strike's own frame is skipped when it already
        /// shows the ball off the spot.
        /// </summary>
        private static int RunUpFrames(PositionStream s, bool home, int slot, int awarded, int struck)
        {
            int streak = 0;
            int spotX = s.BallXY[awarded * 2], spotY = s.BallXY[awarded * 2 + 1];
            if (s.BallXY[struck * 2] != spotX || s.BallXY[struck * 2 + 1] != spotY) struck--;

            for (int f = struck; f > awarded + 1; f--)
            {
                int before = TakerToBall(s, home, slot, f - 1);
                int now = TakerToBall(s, home, slot, f);
                if (before < 20 || before <= now) break;
                streak++;
            }

            return streak;
        }

        private static int TakerToBall(PositionStream s, bool home, int slot, int frame)
        {
            Xy(s, home, slot, frame, out int x, out int y);
            return Dist(x, y, s.BallXY[frame * 2], s.BallXY[frame * 2 + 1]);
        }

        // ------------------------------------------------------------ free kicks

        /// <summary>
        /// Every free kick within 35 m of the goal it is aimed at is taken with a wall in front
        /// of it: at least <see cref="MatchBalance.WallMen"/> defenders nine-odd metres from the
        /// ball on the frame before it is kicked. V10 walls only inside its 32 m shooting range.
        /// </summary>
        [Test]
        public void FreeKickWithin35m_IsTakenWithAWall()
        {
            MatchBalance cfg = V11();
            int inRange = 0, beyondV10Range = 0, walled = 0;

            foreach (PositionStream s in PlayMany(cfg, 16, 36_000, null))
            {
                List<BallAction> a = s.Actions;
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Kind != BallActionKind.FreeKick) continue;
                    int next = NextKick(a, i);
                    if (next < 0) continue;

                    int spot = a[i].Tick;
                    int bx = s.BallXY[spot * 2], by = s.BallXY[spot * 2 + 1];
                    int goalX = a[i].Home ? Pitch.LengthDm : 0;
                    int range = Dist(bx, by, goalX, Pitch.CenterY);
                    if (range >= cfg.SetPieceRangeDm) continue;

                    inRange++;
                    if (range >= cfg.MaxShootRangeDm) beyondV10Range++;

                    int before = Math.Max(spot, a[next].Tick - 1);
                    int wall = 0;
                    for (int j = 0; j < s.PlayerCount; j++)
                    {
                        Xy(s, !a[i].Home, j, before, out int x, out int y);
                        int d = Dist(x, y, bx, by);
                        if (d >= cfg.FreeKickRetreatDm - 12 && d <= cfg.FreeKickRetreatDm + 15) wall++;
                    }

                    if (wall >= cfg.WallMen) walled++;
                }
            }

            TestContext.Out.WriteLine(
                $"[v11-wall] {walled}/{inRange} free kicks within 35 m walled ({beyondV10Range} of them 32-35 m)");
            Assert.That(inRange, Is.GreaterThan(10), "the sample needs free kicks in range");
            Assert.That(walled, Is.EqualTo(inRange), "every free kick within 35 m faces a wall");
        }

        /// <summary>
        /// And the kick itself is the taker's choice between the two options R6 names: straight
        /// at goal, or into the box. Nothing else is done with a free kick in range.
        /// </summary>
        [Test]
        public void FreeKickWithin35m_IsShotOrCrossed()
        {
            MatchBalance cfg = V11();
            int inRange = 0, chosen = 0;

            foreach (PositionStream s in PlayMany(cfg, 8, 37_000, null))
            {
                List<BallAction> a = s.Actions;
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Kind != BallActionKind.FreeKick) continue;
                    int next = NextKick(a, i);
                    if (next < 0) continue;
                    int spot = a[i].Tick;
                    int goalX = a[i].Home ? Pitch.LengthDm : 0;
                    if (Dist(s.BallXY[spot * 2], s.BallXY[spot * 2 + 1], goalX, Pitch.CenterY) >= cfg.SetPieceRangeDm) continue;

                    inRange++;
                    bool byTaker = a[next].Home == a[i].Home && a[next].Slot == a[i].Slot;
                    if (byTaker && (a[next].Kind == BallActionKind.Shot || a[next].Kind == BallActionKind.Cross)) chosen++;
                }
            }

            Assert.That(inRange, Is.GreaterThan(5));
            Assert.That(chosen, Is.EqualTo(inRange), "a free kick in range is shot or crossed by its taker");
        }

        // ------------------------------------------------------------ corners

        /// <summary>
        /// When a corner is delivered the attacking side has a man on the near post, one on the
        /// far post and one on the edge of the box, and the ball is crossed to one of the posts.
        /// </summary>
        [Test]
        public void Corner_IsDeliveredToRoleMen_OnNearPostFarPostAndEdge()
        {
            MatchBalance cfg = V11();
            int corners = 0, manned = 0;

            foreach (PositionStream s in PlayMany(cfg, 8, 38_000, null))
            {
                List<BallAction> a = s.Actions;
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Kind != BallActionKind.Corner) continue;
                    int next = NextKick(a, i);
                    if (next < 0) continue;

                    corners++;
                    Assert.That(a[next].Kind, Is.EqualTo(BallActionKind.Cross), "a corner is swung in");
                    Assert.That(a[next].Home, Is.EqualTo(a[i].Home));
                    Assert.That(a[next].TargetSlot, Is.GreaterThanOrEqualTo(0), "at a man");

                    int before = Math.Max(a[i].Tick, a[next].Tick - 1);
                    bool lowY = s.BallXY[a[i].Tick * 2 + 1] < Pitch.CenterY;
                    bool all = true;
                    foreach (CornerRole role in new[] { CornerRole.NearPost, CornerRole.FarPost, CornerRole.EdgeOfBox })
                    {
                        CornerRoles.Spot(role, a[i].Home, lowY, cfg, out int rx, out int ry);
                        bool there = false;
                        for (int j = 0; j < s.PlayerCount && !there; j++)
                        {
                            Xy(s, a[i].Home, j, before, out int x, out int y);
                            there = Dist(x, y, rx, ry) <= 30;
                        }

                        all &= there;
                    }

                    if (all) manned++;
                }
            }

            TestContext.Out.WriteLine($"[v11-corner] {manned}/{corners} corners with all three roles manned");
            Assert.That(corners, Is.GreaterThan(10));
            Assert.That(manned, Is.EqualTo(corners), "every corner has its near-post, far-post and edge men in place");
        }

        // ------------------------------------------------------------ throw-ins and goal kicks

        /// <summary>
        /// The build-up instruction (Tempo) decides how a goal kick and a throw-in are played:
        /// Slow plays it short to the nearest man, Fast plays it long to the furthest man forward.
        /// Measured as the distance from the restart spot to the man it was played to.
        /// </summary>
        [Test]
        public void GoalKicksAndThrowIns_FollowTheBuildUpInstruction()
        {
            MatchBalance cfg = V11();
            RestartLengths(cfg, Tempo.Slow, out double goalKickSlow, out double throwSlow, out int nSlow);
            RestartLengths(cfg, Tempo.Fast, out double goalKickFast, out double throwFast, out int nFast);

            TestContext.Out.WriteLine(
                $"[v11-restarts] goal kick {goalKickSlow:F0} dm slow vs {goalKickFast:F0} dm fast; " +
                $"throw-in {throwSlow:F0} dm slow vs {throwFast:F0} dm fast ({nSlow}/{nFast} restarts)");
            Assert.That(goalKickFast, Is.GreaterThan(goalKickSlow + 150), "a fast side kicks long, a slow one short");
            Assert.That(throwFast, Is.GreaterThan(throwSlow + 60), "a fast side throws long, a slow one short");
        }

        private void RestartLengths(MatchBalance cfg, Tempo tempo, out double goalKick, out double throwIn, out int count)
        {
            var instructions = new TacticInstructions(Mentality.Balanced, Pressing.Medium, tempo, Width.Normal);
            var context = new TacticContext(new Tactic(Formation.F433, instructions), new BalanceConfig().Tactics.FamiliarityMax);
            var tactics = new MatchTactics(context, context);

            var goalKicks = new List<int>();
            var throwIns = new List<int>();
            foreach (PositionStream s in PlayMany(cfg, 6, 39_000, tactics))
            {
                List<BallAction> a = s.Actions;
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Kind != BallActionKind.GoalKick && a[i].Kind != BallActionKind.ThrowIn) continue;
                    int next = NextKick(a, i);
                    if (next < 0) continue;

                    BallAction kick = a[next];
                    Assert.That(kick.Home == a[i].Home && kick.Slot == a[i].Slot && kick.TargetSlot >= 0, Is.True,
                        $"a {a[i].Kind} is played straight to a team-mate by its taker");

                    int spot = a[i].Tick;
                    Xy(s, kick.Home, kick.TargetSlot, kick.Tick, out int tx, out int ty);
                    int length = Dist(s.BallXY[spot * 2], s.BallXY[spot * 2 + 1], tx, ty);
                    (a[i].Kind == BallActionKind.GoalKick ? goalKicks : throwIns).Add(length);
                }
            }

            goalKick = goalKicks.Average();
            throwIn = throwIns.Average();
            count = goalKicks.Count + throwIns.Count;
        }

        // ------------------------------------------------------------ helpers

        private static MatchBalance V11()
        {
            MatchBalance cfg = new BalanceConfig().Match;
            cfg.Brain = MatchBrainVersion.V11;
            return cfg;
        }

        /// <summary>The first action after the restart at <paramref name="i"/>: the kick that puts it in play.</summary>
        private static int NextKick(List<BallAction> a, int i)
        {
            int j = i + 1;
            return j < a.Count && a[j].Kind != BallActionKind.HalfTime ? j : -1;
        }

        private static PositionStream[] PlayMany(MatchBalance cfg, int matches, ulong firstSeed, MatchTactics? tactics)
        {
            var streams = new PositionStream[matches];
            Parallel.For(0, matches, i =>
            {
                Club home = _league.Clubs[(2 * i) % _league.Clubs.Count];
                Club away = _league.Clubs[(2 * i + 1) % _league.Clubs.Count];
                streams[i] = new MatchSimulator(cfg).Generate(
                    LineupSelector.BestEleven(home), LineupSelector.BestEleven(away),
                    new MatchReport { HomeClubId = home.Id, AwayClubId = away.Id },
                    new Pcg32(firstSeed + (ulong)i), tactics);
            });
            return streams;
        }

        private static void Xy(PositionStream s, bool home, int slot, int frame, out int x, out int y)
        {
            int[] xy = home ? s.HomeXY : s.AwayXY;
            int at = (frame * s.PlayerCount + slot) * 2;
            x = xy[at];
            y = xy[at + 1];
        }

        private static int Dist(int ax, int ay, int bx, int by)
        {
            long dx = ax - bx, dy = ay - by;
            return (int)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
