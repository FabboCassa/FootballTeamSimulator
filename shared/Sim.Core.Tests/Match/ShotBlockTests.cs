using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R2's blocked shots (real-match spec, issue #80): a strike can be charged down by an
    /// outfield defender standing in its lane, the odds falling with his distance from the line
    /// of the ball, and a block is filed as a block rather than as on or off target.
    ///
    /// Read off watched matches, the way the harness reads them: for every strike, the lane is
    /// the segment from the ball on the strike's frame to the centre of the goal, and the nearest
    /// outfield defender's distance to it says how open it was. Fixed seeds, so the counts are
    /// deterministic; the thresholds are loose on purpose, the 1,000-match harness owns the band.
    /// </summary>
    [TestFixture]
    public class ShotBlockTests
    {
        private const int Matches = 30;
        private const ulong FirstSeed = 80_000;

        /// <summary>A defender this near the lane is standing in it.</summary>
        private const int OnLaneDm = 10;

        /// <summary>No outfield defender this near the lane: it is empty.</summary>
        private const int EmptyLaneDm = 40;

        private readonly List<Strike> _strikes = new List<Strike>();
        private ShotPassMetrics _pooled = null!;
        private int _blocks;
        private readonly List<string> _blockFaults = new List<string>();

        private readonly struct Strike
        {
            public Strike(int laneDm, bool blocked)
            {
                LaneDm = laneDm;
                Blocked = blocked;
            }

            /// <summary>The nearest outfield defender's distance to the lane on the strike's frame.</summary>
            public int LaneDm { get; }
            public bool Blocked { get; }
        }

        [OneTimeSetUp]
        public void PlayMatches()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Club a = league.Clubs[9], b = league.Clubs[10];
            Lineup la = LineupSelector.BestEleven(a), lb = LineupSelector.BestEleven(b);
            var engine = new MatchEngine(new BalanceConfig(), applyCondition: true, applyMatchFatigue: true,
                applyPositioning: true, generatePositions: true);
            var analyzer = new MatchAnalyzer();
            var shotPass = new ShotPassAnalyzer();

            for (int i = 0; i < Matches; i++)
            {
                MatchReport r = i % 2 == 0
                    ? engine.Simulate(la, lb, new Pcg32(FirstSeed + (ulong)i))
                    : engine.Simulate(lb, la, new Pcg32(FirstSeed + (ulong)i));

                ShotPassMetrics m = shotPass.Measure(r)!;
                _pooled = _pooled == null ? m : _pooled.Plus(m);

                analyzer.Measure(r);
                ReadStrikes(r.Positions!, analyzer.KeeperSlot(true), analyzer.KeeperSlot(false));
                CheckBlocks(r.Positions!.Actions, i);
            }
        }

        [Test]
        public void AShotThroughADefenderOnTheLane_IsBlockedFarMoreOften_ThanThroughAnEmptyLane()
        {
            double onLane = BlockedPercent(s => s.LaneDm <= OnLaneDm, out int onLaneShots);
            double empty = BlockedPercent(s => s.LaneDm > EmptyLaneDm, out int emptyShots);
            TestContext.Out.WriteLine($"blocked: on the lane {onLane:F1}% of {onLaneShots}, empty lane {empty:F1}% of {emptyShots}");

            Assert.That(onLaneShots, Is.GreaterThan(20), "the sample must hold strikes through a defender");
            Assert.That(emptyShots, Is.GreaterThan(20), "and strikes through an empty lane");
            Assert.That(onLane, Is.GreaterThanOrEqualTo(30), "a man standing on the line charges a good share of them down");
            Assert.That(onLane, Is.GreaterThan(3 * empty), "far more often than when nobody is there");
        }

        /// <summary>
        /// A block ends the strike: it is filed against the defending side, straight after the
        /// strike it stopped, and that strike is never ALSO filed as a save or a miss — however
        /// the ball comes off him, until somebody strikes again.
        /// </summary>
        [Test]
        public void ABlockedShot_IsRecordedAsBlocked_NotAsOnOrOffTarget()
        {
            Assert.That(_blocks, Is.GreaterThan(0), "the sample must hold blocks");
            Assert.That(_blockFaults, Is.Empty);
            Assert.That(_pooled.ShotsBlocked, Is.EqualTo(_blocks), "the harness counts every one of them as blocked");
        }

        // ------------------------------------------------------------------ reading the stream

        private void ReadStrikes(PositionStream s, int homeKeeper, int awayKeeper)
        {
            List<BallAction> actions = s.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction shot = actions[i];
                if (shot.Kind != BallActionKind.Shot) continue;

                int o = Outcome(actions, i);
                int keeper = shot.Home ? awayKeeper : homeKeeper;
                _strikes.Add(new Strike(
                    NearestDefenderToLane(s, shot.Tick, shot.Home, keeper),
                    o >= 0 && actions[o].Kind == BallActionKind.Block));
            }
        }

        private void CheckBlocks(List<BallAction> actions, int match)
        {
            BallAction? strike = null;
            bool settled = false;
            foreach (BallAction a in actions)
            {
                switch (a.Kind)
                {
                    case BallActionKind.Shot:
                        strike = a;
                        settled = false;
                        break;
                    case BallActionKind.Block:
                        _blocks++;
                        if (strike == null || settled || a.Home == strike.Value.Home)
                            _blockFaults.Add($"match {match} tick {a.Tick}: a block that stops no live strike by the other side");
                        settled = true;
                        break;
                    case BallActionKind.Save:
                    case BallActionKind.Miss:
                        if (settled && strike != null)
                            _blockFaults.Add($"match {match} tick {a.Tick}: {a.Kind} filed for a strike already settled");
                        settled = true;
                        break;
                }
            }
        }

        /// <summary>The index of the Save, Goal, Miss or Block that settles the strike; -1 when another strike comes first.</summary>
        private static int Outcome(List<BallAction> actions, int index)
        {
            for (int j = index + 1; j < actions.Count; j++)
            {
                BallActionKind kind = actions[j].Kind;
                if (kind == BallActionKind.Shot) return -1;
                if (kind == BallActionKind.Save || kind == BallActionKind.Goal
                    || kind == BallActionKind.Miss || kind == BallActionKind.Block)
                    return j;
            }

            return -1;
        }

        /// <summary>
        /// The nearest outfield defender's distance, on the strike's frame, to the strike's lane:
        /// from the ball to the centre of the goal it is struck at.
        /// </summary>
        private static int NearestDefenderToLane(PositionStream s, int t, bool home, int keeper)
        {
            PitchPoint ball = s.BallAt(t);
            int goalX = home ? Pitch.LengthDm : 0;
            int[] foes = home ? s.AwayXY : s.HomeXY;

            double nearest = double.MaxValue;
            for (int slot = 0; slot < s.PlayerCount; slot++)
            {
                if (slot == keeper) continue;
                double d = DistanceToSegment(s.PlayerX(foes, t, slot), s.PlayerY(foes, t, slot),
                    ball.X, ball.Y, goalX, Pitch.CenterY);
                if (d < nearest) nearest = d;
            }

            return (int)Math.Round(nearest);
        }

        private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay;
            double length = vx * vx + vy * vy;
            double t = length == 0 ? 0 : ((px - ax) * vx + (py - ay) * vy) / length;
            t = Math.Max(0, Math.Min(1, t));
            double dx = px - (ax + t * vx), dy = py - (ay + t * vy);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private double BlockedPercent(Func<Strike, bool> lane, out int shots)
        {
            int blocked = 0;
            shots = 0;
            foreach (Strike s in _strikes)
            {
                if (!lane(s)) continue;
                shots++;
                if (s.Blocked) blocked++;
            }

            return shots == 0 ? 0 : 100.0 * blocked / shots;
        }
    }
}
