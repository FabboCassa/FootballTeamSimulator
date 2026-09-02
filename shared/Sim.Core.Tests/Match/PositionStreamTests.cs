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

        /// <summary>How long a strike may take to become a goal, a save or a ball out of play.</summary>
        private const int ShotResolveTicks = 40;

        // A chance belongs to a MINUTE, not to a tick: the strike waits a few ticks for the
        // move to arrive, and a chance that lands on top of the previous one waits for that
        // one to be settled. Both are bounded and deterministic (see MatchDirector).
        private const int StrikeSlackTicks = 27;

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
        public void EveryEvent_IsStruck_AndCreditedToItsPlayer()
        {
            Lineup home = LineupSelector.BestEleven(_midA);
            Lineup away = LineupSelector.BestEleven(_midB);
            int checkedEvents = 0;

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

                    // Since 13.2 the ball is not teleported onto the named player: the strike
                    // leaves from where the ball actually is, and the ACTION carries his name.
                    // A ball that jumps thirty metres onto a foot is a far more visible lie
                    // than a shot credited to the man the timeline says it belongs to.
                    Assert.That(
                        stream.Actions.Any(a => a.Tick >= tick && a.Tick <= tick + StrikeSlackTicks
                                                && a.Kind == BallActionKind.Shot
                                                && a.Home == homeShoots && a.Slot == slot),
                        Is.True, $"seed {seed} minute {e.Minute}: no shot credited to the scorer");
                    checkedEvents++;
                }
            }

            Assert.That(checkedEvents, Is.GreaterThan(200), "Sanity: enough chances sampled");
        }

        [Test]
        public void EveryGoal_IsShown_AndAlmostEveryOtherOutcomeToo()
        {
            int goals = 0, goalsShown = 0, others = 0, othersShown = 0;

            for (ulong seed = 200; seed < 215; seed++)
            {
                MatchReport r = Play(seed);
                PositionStream stream = r.Positions!;

                foreach (MatchEvent e in r.Events)
                {
                    int tick = stream.TickOfMinute(e.Minute);
                    BallActionKind expected = e.Type == MatchEventType.Goal ? BallActionKind.Goal
                        : e.Type == MatchEventType.ChanceSaved ? BallActionKind.Save
                        : BallActionKind.Miss;

                    bool shown = stream.Actions.Any(a =>
                        a.Kind == expected && a.Tick >= tick && a.Tick <= tick + ShotResolveTicks);

                    if (e.Type == MatchEventType.Goal)
                    {
                        goals++;
                        if (shown) goalsShown++;
                    }
                    else
                    {
                        others++;
                        if (shown) othersShown++;
                    }
                }
            }

            TestContext.Out.WriteLine($"[outcomes] goals {goalsShown}/{goals} · saves and misses {othersShown}/{others}");

            // A GOAL is not allowed to go unshown: the score says one was scored and the
            // viewer has to see it. A save or a miss can occasionally be swallowed by the next
            // chance arriving; that is a shrug, not a lie about the scoreline.
            Assert.That(goalsShown, Is.EqualTo(goals), "every goal on the timeline must be played out");
            Assert.That(othersShown * 100 / others, Is.GreaterThan(85), "saves and misses should nearly always show");
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

        // ------------------------------------------------------------ realism (13.1, run 1 in Play mode)

        [Test]
        public void SavedShots_StopAtTheKeeper_AndMissesGoWide()
        {
            int goals = 0, saves = 0, misses = 0;

            for (ulong seed = 400; seed < 425; seed++)
            {
                PositionStream stream = Play(seed).Positions!;
                foreach (BallAction a in stream.Actions)
                {
                    if (a.Kind != BallActionKind.Goal && a.Kind != BallActionKind.Save
                        && a.Kind != BallActionKind.Miss) continue;

                    PitchPoint ball = stream.BallAt(a.Tick);
                    int goalX = a.Home ? Pitch.LengthDm : 0;
                    int depth = System.Math.Abs(ball.X - goalX);
                    int offCentre = System.Math.Abs(ball.Y - Pitch.CenterY);

                    if (a.Kind == BallActionKind.Goal)
                    {
                        goals++;
                        Assert.That(depth, Is.LessThanOrEqualTo(6), "a goal has to cross the line");
                        Assert.That(offCentre, Is.LessThanOrEqualTo(40), "a goal has to go between the posts");
                    }
                    else if (a.Kind == BallActionKind.Save)
                    {
                        // THE bug the first Play-mode run showed: a saved shot that ends in the
                        // goal mouth is watched as a goal that was not given.
                        saves++;
                        Assert.That(depth >= 20 || offCentre > 40, Is.True,
                            "a SAVE must not end in the net");
                    }
                    else
                    {
                        misses++;
                        Assert.That(offCentre > 40 || depth > 20, Is.True,
                            "a miss must not end up in the net either");
                    }
                }
            }

            TestContext.Out.WriteLine($"[shots] {goals} goals · {saves} saves · {misses} misses");
            Assert.That(goals, Is.GreaterThan(10), "sanity: enough goals sampled");
            Assert.That(saves, Is.GreaterThan(10), "sanity: enough saves sampled");
        }

        [Test]
        public void Passes_AreFootballLength()
        {
            var lengths = new List<int>();

            for (ulong seed = 200; seed < 215; seed++)
            {
                PositionStream stream = Play(seed).Positions!;
                foreach (BallAction a in stream.Actions)
                {
                    if (a.TargetSlot < 0) continue;
                    if (a.Kind != BallActionKind.Pass && a.Kind != BallActionKind.Cross
                        && a.Kind != BallActionKind.LongBall) continue;

                    int[] side = a.Home ? stream.HomeXY : stream.AwayXY;
                    PitchPoint from = stream.PlayerAt(side, a.Tick, a.Slot);

                    // Follow the straight flight: a pass travels at a constant speed, so the
                    // step stops matching the moment it lands.
                    int first = Step(stream.BallAt(a.Tick), stream.BallAt(System.Math.Min(a.Tick + 1, stream.LastTick)));
                    int land = System.Math.Min(a.Tick + 1, stream.LastTick);
                    while (land + 1 <= stream.LastTick && land <= a.Tick + 8 && first > 4
                           && System.Math.Abs(Step(stream.BallAt(land), stream.BallAt(land + 1)) - first) <= 2)
                        land++;

                    lengths.Add(Step(stream.BallAt(a.Tick), stream.BallAt(land)));
                    Assert.That(from.X, Is.InRange(0, Pitch.LengthDm));
                }
            }

            lengths.Sort();
            int median = lengths[lengths.Count / 2];
            int p99 = lengths[(int)(lengths.Count * 0.99)];

            TestContext.Out.WriteLine(
                $"[passes] {lengths.Count} · median {median / 10}m · p99 {p99 / 10}m · longest {lengths[lengths.Count - 1] / 10}m");

            // Football is played in short passes. The first version scored forward progress
            // without bounding it and duly played 75-metre balls to the most advanced man.
            Assert.That(median, Is.LessThan(320), "the median pass must be a football pass, not a hoof");
            Assert.That(p99, Is.LessThan(620), "even the long balls have to be strikeable");
        }

        [Test]
        public void TheBall_NeverChangesDirectionUntouched()
        {
            int swerves = 0, moving = 0;

            for (ulong seed = 300; seed < 310; seed++)
            {
                PositionStream stream = Play(seed).Positions!;
                for (int t = 2; t <= stream.LastTick; t++)
                {
                    PitchPoint p0 = stream.BallAt(t - 2), p1 = stream.BallAt(t - 1), p2 = stream.BallAt(t);
                    int a = Step(p0, p1), b = Step(p1, p2);
                    if (a < 8 || b < 8) continue; // barely moving: it has no direction to change
                    moving++;

                    // cos of the turn, without trigonometry: a straight flight scores 1.
                    long dot = (long)(p1.X - p0.X) * (p2.X - p1.X) + (long)(p1.Y - p0.Y) * (p2.Y - p1.Y);
                    if (dot * 10 >= 9L * a * b) continue; // under ~25 degrees

                    // Something REPOSITIONS the ball on purpose: a restart, or a challenge.
                    bool deliberate = stream.Actions.Any(x => x.Tick >= t - 2 && x.Tick <= t + 1
                        && x.Kind != BallActionKind.Pass && x.Kind != BallActionKind.Cross
                        && x.Kind != BallActionKind.LongBall && x.Kind != BallActionKind.Dribble);
                    if (deliberate) continue;

                    // So is somebody GAINING the ball, action or no action. When a loose ball is
                    // collected by a team-mate of the man who played it the engine records nothing
                    // (it only logs an interception when the side changes), yet the ball is snapped
                    // to the collector's feet — a turn judged here against where the ball WAS, which
                    // is the wrong point: the collector is standing where it ENDED. He can be a whole
                    // ControlRadius plus the director's chance bonus away, far outside the 45dm below.
                    if (stream.Owner[t] != PositionStream.NoOwner && stream.Owner[t] != stream.Owner[t - 1])
                        continue;

                    int nearest = int.MaxValue;
                    for (int i = 0; i < stream.PlayerCount; i++)
                    {
                        nearest = System.Math.Min(nearest, Step(stream.HomeAt(t, i), p1));
                        nearest = System.Math.Min(nearest, Step(stream.AwayAt(t, i), p1));
                    }

                    if (nearest > 45) swerves++;
                }
            }

            TestContext.Out.WriteLine($"[swerve] {swerves} over {moving} moving ticks");
            Assert.That(swerves, Is.Zero,
                "the ball may only change direction where somebody is standing — it is an object, not a guided missile");
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
