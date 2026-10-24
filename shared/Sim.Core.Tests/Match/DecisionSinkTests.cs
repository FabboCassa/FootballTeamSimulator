using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Match.Movement.Models;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Believable-match spec R13: the decision sink behind the explained HTML replay. Attached, it
    /// records every carrier decision with its three dearest options and the choice; attached or
    /// not, the match is the same match.
    /// </summary>
    [TestFixture]
    public class DecisionSinkTests
    {
        private const int TopSpeed = 75;
        private const int GoalX = Pitch.LengthDm;
        private const int Mid = Pitch.CenterY;

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20261024));
        }

        private static PitchActor At(int x, int y) => new PitchActor(x, y, 0, 0, TopSpeed);

        // ---------------------------------------------------------------- the options

        [Test]
        public void TopOptions_AreTheDearestThree_DearestFirst_AndTheDearestIsTheChoice()
        {
            var valuation = new V11ActionValuation(new BalanceConfig().Match);
            var s = new V11Scene { BallXDm = 600, BallYDm = Mid, CarryTouchDm = 100, Passing = 60, Technique = 60 };
            s.AddFoe(At(GoalX - 5, Mid), keeper: true);
            s.AddFoe(At(700, Mid + 40), keeper: false);
            s.AddMate(At(680, Mid - 150), keeper: false);
            s.AddMate(At(560, Mid + 180), keeper: false);
            s.AddMate(At(500, Mid), keeper: false);
            s.AddMate(At(760, Mid + 220), keeper: false);

            Span<V11Choice> top = stackalloc V11Choice[V11ActionValuation.ExplainedOptions];
            int count = valuation.TopOptions(s, top);

            Assert.That(count, Is.EqualTo(3), "four mates and a carry are on: three are shown");
            Assert.That(top[0].ValuePer10k, Is.GreaterThanOrEqualTo(top[1].ValuePer10k));
            Assert.That(top[1].ValuePer10k, Is.GreaterThanOrEqualTo(top[2].ValuePer10k));
            Assert.That(top[2].ValuePer10k, Is.GreaterThan(V11ActionValuation.NoOption));

            V11Choice chosen = valuation.Choose(s, holding: false);
            Assert.That(top[0].ValuePer10k, Is.EqualTo(chosen.ValuePer10k), "the dearest option is what an unpressed man takes");
        }

        [Test]
        public void TopOptions_ForAKeeperWithNobodyToFind_IsEmpty()
        {
            var valuation = new V11ActionValuation(new BalanceConfig().Match);
            var s = new V11Scene { BallXDm = 50, BallYDm = Mid, CarrierIsKeeper = true };
            s.AddFoe(At(GoalX - 5, Mid), keeper: true);

            Span<V11Choice> top = stackalloc V11Choice[V11ActionValuation.ExplainedOptions];
            Assert.That(valuation.TopOptions(s, top), Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- the chance zone

        [Test]
        public void ChanceZone_Is16mFromTheAttackedGoal_AndCentral()
        {
            Assert.That(DecisionLog.IsChance(Record(0, GoalX - 150, Mid)), Is.True);
            Assert.That(DecisionLog.IsChance(Record(0, GoalX - 150, Mid + 100)), Is.False, "16 m out and 10 m wide is outside 16 m");
            Assert.That(DecisionLog.IsChance(Record(0, GoalX - 60, Mid + 110)), Is.False, "more than 10 m off the centre line");
            Assert.That(DecisionLog.IsChance(Record(0, 150, Mid)), Is.False, "the home side attacks the high end");
            Assert.That(DecisionLog.IsChance(Record(1, 140, Mid - 60)), Is.True);
        }

        private static DecisionRecord Record(int side, int x, int y) =>
            new DecisionRecord { Side = side, BallXDm = x, BallYDm = y };

        // ---------------------------------------------------------------- the match

        [Test]
        public void AttachingTheSink_DoesNotMoveTheMatch()
        {
            ulong without = MatchReportHasher.Hash(Play(null));
            var log = new DecisionLog();
            ulong with = MatchReportHasher.Hash(Play(log));

            Assert.That(with, Is.EqualTo(without), "the sink only reads: the match hash cannot move");
            Assert.That(log.Records.Count, Is.GreaterThan(200), "and it heard every decision on the ball");
        }

        [Test]
        public void Dump_HasTheRecords_OrderedByTick()
        {
            var log = new DecisionLog();
            Play(log);
            int[] flat = log.Flat();
            int lastTick = 90 * new BalanceConfig().Match.TicksPerMinute;

            Assert.That(flat.Length, Is.EqualTo(log.Records.Count * DecisionLog.Stride));
            Assert.That(log.Records.Count, Is.GreaterThan(200));

            int previous = 0, chances = 0;
            for (int r = 0; r < flat.Length; r += DecisionLog.Stride)
            {
                int tick = flat[r];
                Assert.That(tick, Is.InRange(previous, lastTick), "records come in tick order, inside the match");
                previous = tick;
                Assert.That(flat[r + 1], Is.InRange(0, 1), "side");
                Assert.That(flat[r + 2], Is.InRange(0, 10), "carrier slot");
                Assert.That(flat[r + 5], Is.Not.EqualTo((int)V11ActionKind.Hold), "a hold is not a decision on the ball");

                int count = flat[r + 11];
                Assert.That(count, Is.InRange(0, 3));
                for (int o = 1; o < count; o++)
                    Assert.That(flat[r + 12 + o * 6 + 5], Is.LessThanOrEqualTo(flat[r + 12 + (o - 1) * 6 + 5]), "options dearest first");

                if (DecisionLog.IsChance(log.Records[r / DecisionLog.Stride])) chances++;
            }

            Assert.That(chances, Is.GreaterThan(0), "a match has carriers in the chance zone to jump between");
        }

        private static MatchReport Play(IDecisionSink? sink)
        {
            var engine = new MatchEngine(new BalanceConfig()) { DecisionSink = sink };
            return engine.Simulate(
                LineupSelector.BestEleven(_league.Clubs[2]), LineupSelector.BestEleven(_league.Clubs[7]), new Pcg32(4141));
        }
    }
}
