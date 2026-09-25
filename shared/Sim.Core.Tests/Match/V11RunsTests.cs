using System;
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
    /// R2, off-ball movement (watchable-match spec, task 6): a forward runs in behind, timed against
    /// the offside line, when a passing lane into the space behind it exists; a full-back overlaps
    /// the man on the ball under wide/attacking instructions and not under narrow/defensive ones.
    /// The decisions are scripted first; the last two tests play whole V11 matches and count them.
    /// Home (side 0) attacks toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11RunsTests
    {
        // Formations.Roles(F433): GK, CB, CB, FB, FB, DM, CM, CM, W, W, ST.
        private const int CentreBack = 1;
        private const int FullBack = 3;

        private static readonly PositionRole[] Shape = Formations.Roles(Formation.F433);

        private MatchBalance _cfg = null!;
        private V11Positioning _positioning = null!;

        // The run: the defending back four holds a line at x = 700 and the keeper is on his line.
        private const int Line = 700;
        private readonly int[] _defendersX = { Line, Line, Line, Line, 1030 };
        private readonly int[] _defendersY = { 120, 230, 450, 560, 340 };
        private const int CarrierX = 500, CarrierY = 340;
        private const int ForwardY = 300;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _positioning = new V11Positioning(_cfg);
        }

        private bool Run(int forwardX, int line, int[] xs, int[] ys, out int x, out int y) =>
            _positioning.RunInBehind(true, forwardX, ForwardY, CarrierX, CarrierY, line, xs, ys, xs.Length, out x, out y);

        [Test]
        public void Forward_Onside_WithALaneIntoTheSpaceBehind_StartsARunInBehind()
        {
            const int forwardX = Line - 20;
            Assert.That(forwardX, Is.LessThanOrEqualTo(Line), "he sets off from an onside position");

            bool runs = Run(forwardX, Line, _defendersX, _defendersY, out int x, out int y);

            Assert.That(runs, Is.True, "an open lane into open space behind the line is a run");
            Assert.That(x, Is.GreaterThan(Line), "the run goes in behind the line");
            Assert.That(x, Is.LessThan(Pitch.LengthDm), "and stops short of the goal line");
            Assert.That(Math.Abs(y - Pitch.CenterY), Is.LessThanOrEqualTo(Math.Abs(ForwardY - Pitch.CenterY)),
                "he runs at goal, not at the corner flag");
        }

        [Test]
        public void Forward_AlreadyOffside_DoesNotRun()
        {
            Assert.That(Run(Line + 20, Line, _defendersX, _defendersY, out _, out _), Is.False,
                "a run in behind starts onside, or it is just standing offside");
        }

        [Test]
        public void Forward_WithTheLaneBlocked_DoesNotRun()
        {
            // A holding midfielder standing on the line from the ball to the space.
            int[] xs = { Line, Line, Line, Line, 1030, 620 };
            int[] ys = { 120, 230, 450, 560, 340, 330 };

            Assert.That(Run(Line - 20, Line, xs, ys, out _, out _), Is.False, "no lane, no run");
        }

        [Test]
        public void Forward_WithNoSpaceBehind_DoesNotRun()
        {
            int deep = Pitch.LengthDm - _cfg.V11RunGoalGapDm - _cfg.V11RunMinSpaceDm + 10;
            int[] xs = { deep, deep, deep, deep, 1040 };

            Assert.That(Run(deep - 20, deep, xs, _defendersY, out _, out _), Is.False,
                "a line on the edge of its six-yard box leaves nothing to run into");
        }

        [Test]
        public void Forward_WithADefenderAlreadyInTheSpace_DoesNotRun()
        {
            int[] xs = { Line, Line, Line, Line, 1030, Line + _cfg.V11RunDepthDm };
            int[] ys = { 120, 230, 450, 560, 340, 300 };

            Assert.That(Run(Line - 20, Line, xs, ys, out _, out _), Is.False, "the space is covered");
        }

        [Test]
        public void Forward_AwaySide_RunsTowardXZero()
        {
            int[] xs = Array.ConvertAll(_defendersX, x => Pitch.LengthDm - x);
            bool runs = _positioning.RunInBehind(false, Pitch.LengthDm - (Line - 20), ForwardY,
                Pitch.LengthDm - CarrierX, CarrierY, Pitch.LengthDm - Line, xs, _defendersY, xs.Length,
                out int x, out _);

            Assert.That(runs, Is.True);
            Assert.That(x, Is.LessThan(Pitch.LengthDm - Line), "away's space behind is nearer x = 0");
        }

        // ---------------------------------------------------------------- overlaps

        private bool Overlap(int slot, TacticInstructions instructions, int carrierFlankSign, out int x, out int y)
        {
            V11Slot man = V11Slot.InFormation(Shape, slot, _cfg);
            int flank = Math.Sign(man.BaseYPermille - 500);
            if (flank == 0) flank = 1;
            int carrierY = Pitch.CenterY + carrierFlankSign * flank * 250;
            return _positioning.Overlap(man, true, TeamPhase.Progression, true, instructions,
                550, 650, carrierY, out x, out y);
        }

        private static TacticInstructions With(Mentality mentality, Width width) =>
            new TacticInstructions(mentality, Pressing.Medium, Tempo.Normal, width);

        [Test]
        public void FullBack_Overlaps_UnderWideAttacking()
        {
            bool overlaps = Overlap(FullBack, With(Mentality.Attacking, Width.Wide), 1, out int x, out int y);

            Assert.That(overlaps, Is.True);
            Assert.That(x, Is.GreaterThan(650), "he goes past the man on the ball");
            Assert.That(Math.Abs(y - Pitch.CenterY), Is.GreaterThan(250), "on the outside of him");
        }

        [Test]
        public void FullBack_DoesNotOverlap_UnderNarrowDefensive()
        {
            Assert.That(Overlap(FullBack, With(Mentality.Defensive, Width.Narrow), 1, out _, out _), Is.False);
        }

        [Test]
        public void Overlap_IsOnlyOnHisOwnFlank_AndOnlyForAFullBack()
        {
            TacticInstructions go = With(Mentality.Attacking, Width.Wide);
            Assert.That(Overlap(FullBack, go, -1, out _, out _), Is.False, "the ball is on the other flank");
            Assert.That(Overlap(CentreBack, go, 1, out _, out _), Is.False, "a centre-back does not overlap");
        }

        // ---------------------------------------------------------------- in a match

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        private static MatchSimulator PlayV11(TacticInstructions instructions, ulong seed)
        {
            var cfg = new BalanceConfig();
            cfg.Match.Brain = MatchBrainVersion.V11;
            var side = new TacticContext(new Tactic(Formation.F433, instructions), cfg.Tactics.FamiliarityMax);
            var sim = new MatchSimulator(cfg.Match);
            sim.Generate(LineupSelector.BestEleven(_league.Clubs[9]), LineupSelector.BestEleven(_league.Clubs[10]),
                new MatchReport(), new Pcg32(seed), new MatchTactics(side, side));
            return sim;
        }

        [Test]
        public void InAMatch_FullBacksOverlapUnderWideAttacking_AndNeverUnderNarrowDefensive()
        {
            MatchSimulator go = PlayV11(With(Mentality.Attacking, Width.Wide), 3301);
            MatchSimulator stay = PlayV11(With(Mentality.Defensive, Width.Narrow), 3301);

            Assert.That(go.OverlapTicks(0) + go.OverlapTicks(1), Is.GreaterThan(0));
            Assert.That(stay.OverlapTicks(0) + stay.OverlapTicks(1), Is.Zero);
        }

        [Test]
        public void InAMatch_ForwardsRunInBehind()
        {
            MatchSimulator sim = PlayV11(TacticInstructions.Neutral, 3302);

            Assert.That(sim.RunsInBehind(0) + sim.RunsInBehind(1), Is.GreaterThan(0));
        }
    }
}
