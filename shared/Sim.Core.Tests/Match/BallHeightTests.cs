using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R7 of believable-match (issue #150): the ball's height, one value per frame in decimetres.
    /// Presentation-only — it must leave the hash, and so every golden master, where it was.
    /// </summary>
    [TestFixture]
    public class BallHeightTests
    {
        private const int AboveHeadDm = 15;
        private const ulong Seed = 4242;
        private static readonly MatchBalance Cfg = new BalanceConfig().Match;

        /// <summary>A lofted ball is back on the grass well inside this many frames of its peak.</summary>
        private static int LandingFrames => Cfg.MaxFlightTicks / Cfg.StreamTicksPerFrame;

        /// <summary>
        /// The fewest lofted balls of a kind that must land untouched for the landing to be judged
        /// at all: over 31 seeds on this engine and the one before issue #182 a match had 10 to 32
        /// untouched crosses and 33 to 72 untouched long balls (this seed: 20 and 52).
        /// </summary>
        private const int MinUntouchedLandings = 10;

        private static League _league = null!;
        private static MatchReport _report = null!;

        [OneTimeSetUp]
        public void PlayOnce()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _report = Play();
        }

        private static MatchReport Play() => new MatchEngine().Simulate(
            LineupSelector.BestEleven(_league.Clubs[9]),
            LineupSelector.BestEleven(_league.Clubs[10]), new Pcg32(Seed));

        private static PositionStream Stream => _report.Positions!.Unpack();

        [Test]
        public void EveryFrame_CarriesAHeight_AndNoneIsBelowTheGrass()
        {
            Assert.That(Stream.BallZ.Length, Is.EqualTo(Stream.TickCount), "one height per frame");
            Assert.That(Stream.BallZ.Min(), Is.GreaterThanOrEqualTo(0));
            Assert.That(Stream.BallZ[0], Is.Zero, "the kickoff is on the centre spot");
        }

        [TestCase(BallActionKind.Cross)]
        [TestCase(BallActionKind.LongBall)]
        public void ALoftedBall_RisesAboveHeadHeight_AndLandsAtZero(BallActionKind kind)
        {
            PositionStream s = Stream;
            List<BallAction> actions = s.Actions;
            int played = 0, rose = 0, landedUntouched = 0, metInTheAir = 0;

            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].Kind != kind) continue;
                played++;

                int from = actions[i].Tick;
                int to = i + 1 < actions.Count ? actions[i + 1].Tick : s.LastTick;
                int peakFrame = from;
                for (int f = from; f <= to; f++)
                    if (s.BallZ[f] > s.BallZ[peakFrame]) peakFrame = f;

                if (s.BallZ[peakFrame] <= AboveHeadDm) continue;
                rose++;

                int landed = Array.IndexOf(s.BallZ, 0, peakFrame);

                // The flight ends on the grass or at the first touch, whichever comes first. A ball
                // met in the air never landed: the grass it reaches later is another ball's.
                int touched = FirstTouchAfter(s, i);
                if (touched >= 0 && (landed < 0 || touched < landed))
                {
                    metInTheAir++;
                    continue;
                }

                // Struck in the last seconds: the final whistle ends the stream before it can land.
                if (landed < 0 && peakFrame + LandingFrames > s.LastTick) continue;
                Assert.That(landed, Is.GreaterThan(peakFrame), $"{kind} at frame {from} never comes down");
                Assert.That(landed - peakFrame, Is.LessThanOrEqualTo(LandingFrames),
                    $"{kind} at frame {from} hangs in the air");
                landedUntouched++;
            }

            TestContext.Out.WriteLine($"[ball-height] {kind}: {rose}/{played} rose above {AboveHeadDm} dm, " +
                                      $"{landedUntouched} landed untouched, {metInTheAir} met in the air");
            Assert.That(played, Is.GreaterThan(0), $"the match plays at least one {kind}");
            Assert.That(rose * 2, Is.GreaterThanOrEqualTo(played), $"most of the {kind}es rise above head height");
            Assert.That(landedUntouched, Is.GreaterThanOrEqualTo(MinUntouchedLandings),
                $"enough {kind}es land untouched for their landing to be judged");
        }

        /// <summary>
        /// The frame of the first touch of the ball by anybody after the action at
        /// <paramref name="index"/>: a man on it, or a touch in the feed; -1 for none.
        /// </summary>
        private static int FirstTouchAfter(PositionStream s, int index)
        {
            int from = s.Actions[index].Tick;
            int touch = -1;
            for (int j = index + 1; j < s.Actions.Count; j++)
            {
                if (!IsTouch(s.Actions[j].Kind)) continue;
                touch = s.Actions[j].Tick;
                break;
            }

            int last = touch >= 0 ? touch : s.LastTick;
            for (int f = from + 1; f < last; f++)
                if (s.Owner[f] != PositionStream.NoOwner) return f;
            return touch;
        }

        private static bool IsTouch(BallActionKind kind) => kind switch
        {
            BallActionKind.Pass or BallActionKind.LongBall or BallActionKind.Cross or BallActionKind.Dribble
                or BallActionKind.Recovery or BallActionKind.Interception or BallActionKind.Clearance
                or BallActionKind.Shot or BallActionKind.Save or BallActionKind.Block => true,
            _ => false,
        };

        [Test]
        public void APass_StaysOnTheGrass()
        {
            PositionStream s = Stream;
            BallAction pass = s.Actions.First(a => a.Kind == BallActionKind.Pass && s.BallZ[a.Tick] == 0);
            Assert.That(s.BallZ[pass.Tick + 1], Is.Zero, "a ground pass is not lifted");
        }

        [Test]
        public void TheHeight_SurvivesPacking_AndStaysOutOfTheHash()
        {
            MatchReport report = Play();
            int[] heights = report.Positions!.BallZ.ToArray();
            ulong hash = MatchReportHasher.Hash(report);

            report.Positions.Pack();
            Assert.That(report.Positions.BallZ, Is.Empty, "packing moves the height into its packed form");
            MatchReport back = JsonSerializer.Deserialize<MatchReport>(JsonSerializer.Serialize(report))!;
            back.Positions!.Unpack();

            Assert.That(back.Positions.BallZ, Is.EqualTo(heights));
            Assert.That(MatchReportHasher.Hash(back), Is.EqualTo(hash));

            back.Positions.BallZ = Array.Empty<int>();
            Assert.That(MatchReportHasher.Hash(back), Is.EqualTo(hash),
                "the height is presentation: it must not move a golden master");
        }

        [Test]
        public void AStreamStoredWithoutHeights_StillLoads_WithTheBallOnTheGround()
        {
            MatchReport report = Play();
            int[] ball = report.Positions!.BallXY.ToArray();

            // A v12 replay: packed, and no height at all — neither the array nor its packed form.
            report.Positions.BallZ = Array.Empty<int>();
            report.Positions.Pack();
            JsonNode json = JsonSerializer.SerializeToNode(report)!;
            JsonObject positions = json["Positions"]!.AsObject();
            positions.Remove("BallZ");
            positions.Remove("BallZPacked");
            string stored = json.ToJsonString();
            Assert.That(stored, Does.Not.Contain("BallZ"), "the stored shape predates the height");

            MatchReport back = JsonSerializer.Deserialize<MatchReport>(stored)!;
            back.Positions!.Unpack();

            Assert.That(back.Positions.BallXY, Is.EqualTo(ball));
            Assert.That(back.Positions.BallZ, Is.Empty);
            Assert.That(back.Positions.BallZAt(500), Is.Zero, "no height recorded means on the ground");
        }
    }
}
