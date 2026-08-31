using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The movement layer (tasks 1.5 · 13.1). What is pinned here is the CONTRACT the
    /// renderer plays back — determinism, the ball on the shooter at the event tick, no
    /// teleports, the ball at somebody's feet — not the choreography itself, which is
    /// balance and is meant to be tuned.
    /// </summary>
    [TestFixture]
    public class PositionStreamTests
    {
        private static League _league = null!;
        private static Club _midA = null!;
        private static Club _midB = null!;
        private static readonly MatchBalance Cfg = new BalanceConfig().Match;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _midA = _league.Clubs[9];
            _midB = _league.Clubs[10];
        }

        private static MatchReport Play(ulong seed) => new MatchEngine().Simulate(
            LineupSelector.BestEleven(_midA), LineupSelector.BestEleven(_midB), new Pcg32(seed));

        private static int SlotIndexOf(Lineup lineup, int playerId) =>
            lineup.Slots.FindIndex(s => s.Player.Id == playerId);

        /// <summary>The hardest a player can move in one tick: full pace, sprinting.</summary>
        private static int MaxStepDm =>
            (Cfg.PlayerSpeedBaseDmPerTick + Cfg.PlayerSpeedPaceDmPerTick) * Cfg.PlayerSprintPercent / 100;

        // ------------------------------------------------------------ determinism

        [Test]
        public void Stream_IsDeterministic_PerSeed()
        {
            string a = JsonSerializer.Serialize(Play(42).Positions);
            string b = JsonSerializer.Serialize(Play(42).Positions);

            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentStreams()
        {
            string a = JsonSerializer.Serialize(Play(42).Positions);
            string b = JsonSerializer.Serialize(Play(43).Positions);

            Assert.That(b, Is.Not.EqualTo(a));
        }

        [Test]
        public void SkippingTheStream_LeavesTheResultUntouched()
        {
            Lineup home = LineupSelector.BestEleven(_midA);
            Lineup away = LineupSelector.BestEleven(_midB);

            for (ulong seed = 500; seed < 510; seed++)
            {
                MatchReport withStream = new MatchEngine()
                    .Simulate(LineupSelector.BestEleven(_midA), LineupSelector.BestEleven(_midB), new Pcg32(seed));
                MatchReport without = new MatchEngine(generatePositions: false)
                    .Simulate(LineupSelector.BestEleven(_midA), LineupSelector.BestEleven(_midB), new Pcg32(seed));

                Assert.That(without.Positions, Is.Null, "the opt-out must skip the stream");
                withStream.Positions = null;
                Assert.That(
                    JsonSerializer.Serialize(without), Is.EqualTo(JsonSerializer.Serialize(withStream)),
                    $"seed {seed}: generating the movement stream must not move the result by one bit");
            }

            Assert.That(home.Slots, Has.Count.EqualTo(Lineup.Size));
            Assert.That(away.Slots, Has.Count.EqualTo(Lineup.Size));
        }

        // ------------------------------------------------------------ event consistency

        [Test]
        public void Ball_CoincidesWithShooter_AtEveryEventTick()
        {
            Lineup home = LineupSelector.BestEleven(_midA);
            Lineup away = LineupSelector.BestEleven(_midB);
            int goalsChecked = 0;

            for (ulong seed = 100; seed < 120; seed++)
            {
                MatchReport r = Play(seed);
                PositionStream stream = r.Positions!;

                foreach (MatchEvent e in r.Events)
                {
                    int tick = stream.TickOfMinute(e.Minute);
                    bool homeShoots = e.ClubId == r.HomeClubId;
                    int slot = SlotIndexOf(homeShoots ? home : away, e.PlayerId);
                    Assert.That(slot, Is.GreaterThanOrEqualTo(0), "Shooter must be in the lineup");

                    PitchPoint shooter = homeShoots ? stream.HomeAt(tick, slot) : stream.AwayAt(tick, slot);
                    PitchPoint ball = stream.BallAt(tick);
                    Assert.That(shooter.X, Is.EqualTo(ball.X), $"seed {seed} minute {e.Minute}: shooter X != ball X");
                    Assert.That(shooter.Y, Is.EqualTo(ball.Y), $"seed {seed} minute {e.Minute}: shooter Y != ball Y");

                    if (e.Type == MatchEventType.Goal) goalsChecked++;
                }
            }

            Assert.That(goalsChecked, Is.GreaterThan(20), "Sanity: enough goals sampled");
        }

        [Test]
        public void EveryEvent_HasAShotAction_AtItsTick()
        {
            Lineup home = LineupSelector.BestEleven(_midA);
            Lineup away = LineupSelector.BestEleven(_midB);

            for (ulong seed = 200; seed < 215; seed++)
            {
                MatchReport r = Play(seed);
                PositionStream stream = r.Positions!;

                foreach (MatchEvent e in r.Events)
                {
                    int tick = stream.TickOfMinute(e.Minute);
                    bool homeShoots = e.ClubId == r.HomeClubId;
                    int slot = SlotIndexOf(homeShoots ? home : away, e.PlayerId);

                    List<BallAction> shots = stream.Actions
                        .Where(a => a.Tick == tick && a.Kind == BallActionKind.Shot).ToList();

                    Assert.That(shots, Is.Not.Empty, $"seed {seed} minute {e.Minute}: no shot action at the event tick");
                    Assert.That(shots.Any(a => a.Home == homeShoots && a.Slot == slot), Is.True,
                        $"seed {seed} minute {e.Minute}: the shot is not credited to the scripted shooter");

                    BallActionKind expected = e.Type == MatchEventType.Goal ? BallActionKind.Goal
                        : e.Type == MatchEventType.ChanceSaved ? BallActionKind.Save
                        : BallActionKind.Miss;

                    bool resolved = stream.Actions.Any(a =>
                        a.Kind == expected && a.Tick > tick && a.Tick <= tick + Cfg.ShotFlightTicks + 1);
                    bool atFullTime = tick + Cfg.ShotFlightTicks > stream.LastTick;

                    Assert.That(resolved || atFullTime, Is.True,
                        $"seed {seed} minute {e.Minute}: the shot never resolves as {expected}");
                }
            }
        }

        [Test]
        public void Shots_AreStruck_TowardTheAttackedGoal()
        {
            for (ulong seed = 300; seed < 312; seed++)
            {
                MatchReport r = Play(seed);
                PositionStream stream = r.Positions!;

                foreach (MatchEvent e in r.Events)
                {
                    int tick = stream.TickOfMinute(e.Minute);

                    // The shooter needs his run-up: a chance inside the first couple of
                    // minutes fires before a full ShooterApproachTicks window exists, and a
                    // deep-lying scorer physically cannot be in the box yet. Everything after
                    // that is pinned.
                    if (tick < Cfg.ShooterApproachTicks) continue;

                    PitchPoint ball = stream.BallAt(tick);
                    bool homeShoots = e.ClubId == r.HomeClubId;

                    // Home attacks toward X = LengthDm, away toward X = 0. The shot is taken
                    // from open play now rather than a fixed spot, so what is pinned is the
                    // half, not a coordinate.
                    if (homeShoots)
                        Assert.That(ball.X, Is.GreaterThan(Pitch.CenterX),
                            $"seed {seed} minute {e.Minute}: home shot from its own half");
                    else
                        Assert.That(ball.X, Is.LessThan(Pitch.CenterX),
                            $"seed {seed} minute {e.Minute}: away shot from its own half");
                }
            }
        }

        // ------------------------------------------------------------ structure & bounds

        [Test]
        public void Frames_CoverFullMatch()
        {
            PositionStream stream = Play(7).Positions!;
            int frames = 90 * stream.TicksPerMinute + 1; // tick 0 (kickoff) .. tick 90*tpm

            Assert.That(stream.TicksPerMinute, Is.GreaterThan(0));
            Assert.That(stream.PlayerCount, Is.EqualTo(Lineup.Size));
            Assert.That(stream.LastTick, Is.EqualTo(90 * stream.TicksPerMinute));
            Assert.That(stream.TickCount, Is.EqualTo(frames));
            Assert.That(stream.BallXY.Length, Is.EqualTo(frames * 2));
            Assert.That(stream.HomeXY.Length, Is.EqualTo(frames * Lineup.Size * 2));
            Assert.That(stream.AwayXY.Length, Is.EqualTo(frames * Lineup.Size * 2));
            Assert.That(stream.Owner.Length, Is.EqualTo(frames));
            Assert.That(stream.HomePlayerIds.Length, Is.EqualTo(Lineup.Size));
            Assert.That(stream.AwayShirts.Length, Is.EqualTo(Lineup.Size));
        }

        [Test]
        public void AllPositions_StayOnPitch()
        {
            for (ulong seed = 320; seed < 325; seed++)
            {
                PositionStream stream = Play(seed).Positions!;
                for (int t = 0; t <= stream.LastTick; t++)
                {
                    AssertOnPitch(stream.BallAt(t));
                    for (int i = 0; i < stream.PlayerCount; i++)
                    {
                        AssertOnPitch(stream.HomeAt(t, i));
                        AssertOnPitch(stream.AwayAt(t, i));
                    }
                }
            }
        }

        private static void AssertOnPitch(PitchPoint p)
        {
            Assert.That(p.X, Is.InRange(0, Pitch.LengthDm));
            Assert.That(p.Y, Is.InRange(0, Pitch.WidthDm));
        }

        [Test]
        public void KickoffFrame_HasBallAtCentre()
        {
            PitchPoint ball = Play(11).Positions!.BallAt(0);

            Assert.That(ball.X, Is.EqualTo(Pitch.CenterX));
            Assert.That(ball.Y, Is.EqualTo(Pitch.CenterY));
        }

        [Test]
        public void Teams_FaceEachOther_AtKickoff()
        {
            Lineup home = LineupSelector.BestEleven(_midA);
            Lineup away = LineupSelector.BestEleven(_midB);
            PositionStream stream = Play(13).Positions!;

            int homeGk = home.Slots.FindIndex(s => s.Role == PositionRole.Goalkeeper);
            int awayGk = away.Slots.FindIndex(s => s.Role == PositionRole.Goalkeeper);
            Assert.That(stream.HomeAt(0, homeGk).X, Is.LessThan(Pitch.LengthDm / 4), "Home GK near X=0");
            Assert.That(stream.AwayAt(0, awayGk).X, Is.GreaterThan(Pitch.LengthDm * 3 / 4), "Away GK near X=LengthDm");

            double homeX = Enumerable.Range(0, stream.PlayerCount).Average(i => stream.HomeAt(0, i).X);
            double awayX = Enumerable.Range(0, stream.PlayerCount).Average(i => stream.AwayAt(0, i).X);
            Assert.That(homeX, Is.LessThan(awayX), "Home side sits left of the away side overall");
        }

        // ------------------------------------------------------------ the movement itself

        [Test]
        public void NobodyTeleports()
        {
            int worst = 0;
            for (ulong seed = 400; seed < 404; seed++)
            {
                PositionStream stream = Play(seed).Positions!;
                for (int t = 1; t <= stream.LastTick; t++)
                {
                    for (int i = 0; i < stream.PlayerCount; i++)
                    {
                        worst = System.Math.Max(worst, Step(stream.HomeAt(t - 1, i), stream.HomeAt(t, i)));
                        worst = System.Math.Max(worst, Step(stream.AwayAt(t - 1, i), stream.AwayAt(t, i)));
                    }
                }
            }

            TestContext.Out.WriteLine($"[movement] worst single-tick step {worst}dm (cap {MaxStepDm}dm)");
            Assert.That(worst, Is.LessThanOrEqualTo(MaxStepDm + 2),
                "A player must never cover more ground in one tick than his sprint allows");
        }

        private static int Step(PitchPoint a, PitchPoint b)
        {
            int dx = a.X - b.X, dy = a.Y - b.Y;
            return (int)System.Math.Sqrt(dx * dx + dy * dy);
        }

        [Test]
        public void TheBallIsAtSomebodysFeet_MostOfTheMatch()
        {
            PositionStream stream = Play(21).Positions!;
            int owned = 0, checkedTicks = 0;

            for (int t = 0; t <= stream.LastTick; t++)
            {
                checkedTicks++;
                if (!stream.TryOwner(stream.Owner[t], out bool home, out int slot)) continue;

                owned++;
                PitchPoint carrier = home ? stream.HomeAt(t, slot) : stream.AwayAt(t, slot);
                PitchPoint ball = stream.BallAt(t);
                Assert.That(carrier.X, Is.EqualTo(ball.X), $"tick {t}: the ball is not on its carrier");
                Assert.That(carrier.Y, Is.EqualTo(ball.Y), $"tick {t}: the ball is not on its carrier");
            }

            int percent = owned * 100 / checkedTicks;
            TestContext.Out.WriteLine($"[movement] ball at a player's feet for {percent}% of the match");
            Assert.That(percent, Is.GreaterThan(25), "Most of a match is somebody carrying the ball");
        }

        [Test]
        public void ThePlayIsBusy_AndBothEndsAreVisited()
        {
            PositionStream stream = Play(23).Positions!;

            long travelled = 0;
            int minX = Pitch.LengthDm, maxX = 0;
            for (int t = 1; t <= stream.LastTick; t++)
            {
                travelled += Step(stream.BallAt(t - 1), stream.BallAt(t));
                minX = System.Math.Min(minX, stream.BallAt(t).X);
                maxX = System.Math.Max(maxX, stream.BallAt(t).X);
            }

            TestContext.Out.WriteLine(
                $"[movement] {stream.Actions.Count} ball actions · ball travelled {travelled / 10}m · X range {minX}..{maxX}dm");

            Assert.That(stream.Actions.Count, Is.GreaterThan(120), "A match is made of many touches, not a handful");
            Assert.That(minX, Is.LessThan(250), "Play reaches the home team's box");
            Assert.That(maxX, Is.GreaterThan(Pitch.LengthDm - 250), "Play reaches the away team's box");
            Assert.That(travelled, Is.GreaterThan(20_000), "The ball actually moves around the pitch");
        }

        [Test]
        public void Shirts_AreUnique_WithinASide()
        {
            PositionStream stream = Play(29).Positions!;

            Assert.That(stream.HomeShirts.Distinct().Count(), Is.EqualTo(stream.HomeShirts.Length));
            Assert.That(stream.AwayShirts.Distinct().Count(), Is.EqualTo(stream.AwayShirts.Length));
            Assert.That(stream.HomeShirts, Has.All.InRange(1, 30));
            Assert.That(stream.HomePlayerIds.Distinct().Count(), Is.EqualTo(Lineup.Size));
        }

        // ------------------------------------------------------------ measurement

        [Test]
        public void StreamSize_IsPrinted_ForTheRecord()
        {
            MatchReport r = Play(31);
            string full = JsonSerializer.Serialize(r);
            string positions = JsonSerializer.Serialize(r.Positions);

            TestContext.Out.WriteLine(
                $"[movement-size] ticksPerMinute={r.Positions!.TicksPerMinute} frames={r.Positions.TickCount} " +
                $"actions={r.Positions.Actions.Count} · stream {positions.Length / 1024}KB · full report {full.Length / 1024}KB");

            Assert.That(positions.Length, Is.GreaterThan(0));
        }
    }
}
