using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Free-ball spec R5, issue #183: on a cross the striker attacks the near post, the far-side wide
    /// player the far post, and one central midfielder arrives late at the penalty spot. Home attacks
    /// toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11BoxRunsTests
    {
        private const int Mid = Pitch.CenterY;
        private static readonly MatchBalance Cfg = CrossScene.Cfg;
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20261183));
        }

        [Test]
        public void TheRoleJobs_PutTheNearPostOnTheBallsSide_AndTheFarPostOnTheOther()
        {
            var jobs = new V11RoleJobs(Cfg);
            jobs.NearPost(true, ballYDm: 60, out int nx, out int ny);
            jobs.FarPost(true, ballYDm: 60, out int fx, out int fy);
            Assert.That(ny, Is.LessThan(Mid), "the near post is on the ball's side");
            Assert.That(fy, Is.GreaterThan(Mid), "the far post on the other");
            Assert.That(Pitch.LengthDm - nx, Is.EqualTo(Cfg.V11NearPostGoalLineDm));
            Assert.That(Pitch.LengthDm - fx, Is.EqualTo(Cfg.V11FarPostGoalLineDm));

            jobs.NearPost(false, ballYDm: Pitch.WidthDm - 60, out int ax, out int ay);
            Assert.That(ax, Is.EqualTo(Cfg.V11NearPostGoalLineDm), "mirrored for the away side");
            Assert.That(ay, Is.GreaterThan(Mid));
        }

        /// <summary>
        /// A winger on the ball up the touchline with the cross on: over two seconds, every tick, the
        /// striker is on the near-post run, the other winger on the far-post run and one central
        /// midfielder on the run to the penalty spot, each target where the job puts it (held onside
        /// of the line), and each of them closer to it at the end than at the start.
        /// </summary>
        [Test]
        public void OnACross_TheStrikerAttacksTheNearPost_TheFarWingerTheFarPost_AndAMidfielderTheSpot()
        {
            var scene = new CrossScene(_league);
            scene.Reset();
            int line = 990;
            int[] back = scene.BackLine;
            int[] lineY = { Mid - 120, Mid - 40, Mid + 40, Mid + 120 };
            for (int i = 0; i < back.Length && i < lineY.Length; i++) scene.Place(1, back[i], line, lineY[i]);

            scene.Place(0, scene.Carrier, 900, 50);
            scene.Place(0, scene.Striker, 940, Mid + 40);
            scene.Place(0, scene.FarWinger, 860, Pitch.WidthDm - 80);
            foreach (int cm in scene.Midfielders) scene.Place(0, cm, 780, Mid + 30);
            scene.GiveBall(scene.Carrier);

            int[] movers = new[] { scene.Striker, scene.FarWinger }.Concat(scene.Midfielders).ToArray();
            var jobs = new V11RoleJobs(Cfg);
            jobs.NearPost(true, 50, out int nearX, out int nearY);
            jobs.FarPost(true, 50, out int farX, out int farY);
            jobs.BoxSpot(true, out int spotX, out int spotY);
            var start = movers.ToDictionary(s => s, s => (scene.X(0, s), scene.Y(0, s)));

            // The team brains need a beat to see the side on the ball.
            for (int t = 0; t < 3; t++) scene.Tick(movers);
            int arrivals = 0;
            for (int t = 0; t < 2 * Cfg.TicksPerSecond; t++)
            {
                scene.Tick(movers);
                MovementRecord striker = scene.Last(scene.Striker), far = scene.Last(scene.FarWinger);
                Assert.That(striker.Job, Is.EqualTo(MovementJob.NearPost), $"tick {t}: the striker");
                Assert.That(striker.TargetYDm, Is.EqualTo(nearY).Within(2), $"tick {t}: at the near post");
                Assert.That(striker.TargetXDm, Is.EqualTo(Math.Min(nearX, striker.OffsideLineXDm - Cfg.V11OnsideHoldDm)).Within(2),
                    $"tick {t}: as deep as the line lets him");
                Assert.That(far.Job, Is.EqualTo(MovementJob.FarPost), $"tick {t}: the far winger");
                Assert.That(far.TargetYDm, Is.EqualTo(farY).Within(2), $"tick {t}: at the far post");
                Assert.That(far.TargetXDm, Is.EqualTo(Math.Min(farX, far.OffsideLineXDm - Cfg.V11OnsideHoldDm)).Within(2));

                var spot = scene.Midfielders.Select(scene.Last).Where(r => r.Job == MovementJob.BoxArrival).ToList();
                Assert.That(spot.Count, Is.EqualTo(1), $"tick {t}: one central midfielder arrives");
                Assert.That(spot[0].TargetYDm, Is.EqualTo(spotY).Within(1));
                arrivals++;
            }

            Assert.That(arrivals, Is.EqualTo(2 * Cfg.TicksPerSecond));
            Assert.That(Closer(scene, start, scene.Striker, nearX, nearY), Is.True, "the striker gets there");
            Assert.That(Closer(scene, start, scene.FarWinger, farX, farY), Is.True, "the far winger gets there");
            Assert.That(scene.Midfielders.Any(cm => Closer(scene, start, cm, spotX, spotY)), Is.True, "a midfielder gets there");
        }

        private static bool Closer(CrossScene scene, Dictionary<int, (int X, int Y)> start, int slot, int tx, int ty)
        {
            (int x0, int y0) = start[slot];
            long before = (long)(x0 - tx) * (x0 - tx) + (long)(y0 - ty) * (y0 - ty);
            long after = (long)(scene.X(0, slot) - tx) * (scene.X(0, slot) - tx) + (long)(scene.Y(0, slot) - ty) * (scene.Y(0, slot) - ty);
            return after < before;
        }

        /// <summary>
        /// In a 4-3-3 match off the trace: the near-post run is only ever a striker's and the far-post
        /// run a winger's on the other flank from the ball, one of each per side and tick, on the ball's
        /// side and the other side of the goal respectively.
        /// </summary>
        [Test]
        public void InAMatch_TheNearPostIsTheStrikers_AndTheFarPostTheFarWingers()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20261106));
            var cfg = new BalanceConfig();
            var tactic = new TacticContext(new Tactic(Formation.F433, new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Normal)), cfg.Tactics.FamiliarityMax);
            var log = new MovementLog();
            Lineup home = LineupSelector.BestEleven(league.Clubs[3], Formation.F433);
            Lineup away = LineupSelector.BestEleven(league.Clubs[8], Formation.F433);
            for (int side = 0; side < 2; side++)
            for (int slot = 0; slot < home.Slots.Count; slot++)
                log.Watch(side, slot);
            new MatchEngine(cfg) { MovementSink = log }.Simulate(home, away, new Pcg32(4242), new MatchTactics(tactic, tactic));

            PositionRole Role(MovementRecord r) => (r.Side == 0 ? home : away).Slots[r.Slot].Role;
            var near = log.Records.Where(r => r.Job == MovementJob.NearPost).ToList();
            var far = log.Records.Where(r => r.Job == MovementJob.FarPost).ToList();

            Assert.That(near.Count, Is.GreaterThan(50));
            Assert.That(far.Count, Is.GreaterThan(50));
            Assert.That(near.Select(Role).Distinct(), Is.EqualTo(new[] { PositionRole.Striker }));
            Assert.That(far.Select(Role).Distinct(), Is.EqualTo(new[] { PositionRole.Winger }));
            Assert.That(near.GroupBy(r => (r.Tick, r.Side)).Max(g => g.Count()), Is.EqualTo(1));
            Assert.That(far.GroupBy(r => (r.Tick, r.Side)).Max(g => g.Count()), Is.EqualTo(1));
            Assert.That(near.Concat(far).All(r => r.InPossession), Is.True);

            // Each post run on its side of the goal while the crosser is on the ball: the near one on
            // his flank, the far one across.
            bool OnTheBall(MovementRecord r) => r.BallOwnerSide == r.Side && Math.Abs(r.BallYDm - Mid) > 100;
            Assert.That(near.Count(OnTheBall), Is.GreaterThan(20));
            foreach (MovementRecord r in near.Where(OnTheBall))
                Assert.That(Math.Sign(r.TargetYDm - Mid), Is.EqualTo(Math.Sign(r.BallYDm - Mid)), $"near post at tick {r.Tick}");
            foreach (MovementRecord r in far.Where(OnTheBall))
                Assert.That(Math.Sign(r.TargetYDm - Mid), Is.EqualTo(-Math.Sign(r.BallYDm - Mid)), $"far post at tick {r.Tick}");
        }
    }
}
