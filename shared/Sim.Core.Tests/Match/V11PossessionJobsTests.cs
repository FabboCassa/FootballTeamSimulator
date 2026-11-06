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
    /// Free-ball spec R7 (believable-match R4), issue #185: the role jobs in possession. Each job on
    /// a scripted scene, then the wiring read off the movement trace of real matches. Home (side 0)
    /// attacks toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11PossessionJobsTests
    {
        // Formations.Roles(F433): GK, CB, CB, FB, FB, DM, CM, CM, W, W, ST.
        private const int CentreBack = 1;
        private const int FullBack = 3;
        private const int Holding = 5;
        private const int Midfielder = 6;
        private const int WingerA = 8;
        private const int WingerB = 9;
        private const int Striker = 10;

        private static readonly PositionRole[] Shape = Formations.Roles(Formation.F433);
        private static readonly TacticInstructions Neutral =
            new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Normal);

        private MatchBalance _cfg = null!;
        private V11RoleJobs _jobs = null!;
        private V11Positioning _positioning = null!;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _jobs = new V11RoleJobs(_cfg);
            _positioning = new V11Positioning(_cfg);
        }

        private V11Slot Man(int slot) => V11Slot.InFormation(Shape, slot, _cfg);

        private V11Slot Man(PositionRole[] shape, int slot) => V11Slot.InFormation(shape, slot, _cfg);

        // ---------------------------------------------------------------- wingers hold the width

        [Test]
        public void Winger_HoldsHisLane_InTheWideChannel()
        {
            Assert.That(_jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, Pitch.CenterY, out int lane), Is.True);
            Assert.That(Math.Abs(lane - Pitch.CenterY), Is.GreaterThanOrEqualTo(Pitch.CenterY - _cfg.V11CrossChannelDm), "out in the wide channel");
            Assert.That(lane, Is.InRange(_cfg.V11WideLaneTouchlineGapDm, Pitch.WidthDm - _cfg.V11WideLaneTouchlineGapDm), "and on the pitch");
        }

        [Test]
        public void Winger_ComesInOffTheLine_WithTheBallOnHisSide_WhileTheBlockSlidesOut()
        {
            _jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, Pitch.CenterY, out int lane);
            int flank = Math.Sign(lane - Pitch.CenterY);
            int onHisSide = Pitch.CenterY + flank * 300;

            _positioning.PhaseSpot(Man(WingerA), true, TeamPhase.Progression, 1000, Neutral, 600, onHisSide, out _, out int spotNear);
            _positioning.PhaseSpot(Man(WingerA), true, TeamPhase.Progression, 1000, Neutral, 600, Pitch.CenterY, out _, out int spotMiddle);
            Assert.That(Math.Sign(spotNear - spotMiddle), Is.EqualTo(flank), "the block slides toward the ball");

            _jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, onHisSide, out int near);
            Assert.That(near, Is.EqualTo(lane - flank * (300 * _cfg.V11WideLaneLeanPercent / 100)), "he steps against the slide");
            Assert.That(Math.Sign(near - Pitch.CenterY), Is.EqualTo(flank), "and stays on his flank");
        }

        [Test]
        public void FarSideWinger_HoldsHisLane_WhereverTheBallIs()
        {
            _jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, Pitch.CenterY, out int lane);
            int flank = Math.Sign(lane - Pitch.CenterY);

            _jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, Pitch.CenterY - flank * 100, out int near);
            _jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, Pitch.CenterY - flank * 300, out int far);
            Assert.That(near, Is.EqualTo(lane));
            Assert.That(far, Is.EqualTo(lane));
        }

        [Test]
        public void AwayWinger_ComesInOffTheMirroredLine()
        {
            _jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, Pitch.CenterY + 300, out int home);
            _jobs.HoldWidth(Man(WingerA), false, 1000, Neutral, Pitch.CenterY - 300, out int away);
            Assert.That(away - Pitch.CenterY, Is.EqualTo(Pitch.CenterY - home));
        }

        [Test]
        public void TheTwoWingers_StandOnOppositeFlanks_AndTheWidthInstructionSpreadsThem()
        {
            _jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, Pitch.CenterY, out int a);
            _jobs.HoldWidth(Man(WingerB), true, 1000, Neutral, Pitch.CenterY, out int b);
            Assert.That(Math.Sign(a - Pitch.CenterY) * Math.Sign(b - Pitch.CenterY), Is.EqualTo(-1));

            var narrow = new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Narrow);
            _jobs.HoldWidth(Man(WingerA), true, 1000, narrow, Pitch.CenterY, out int tucked);
            Assert.That(Math.Abs(tucked - Pitch.CenterY), Is.LessThan(Math.Abs(a - Pitch.CenterY)), "a narrow side's wingers play narrower");
        }

        [Test]
        public void AwayWinger_HoldsTheMirroredLane()
        {
            _jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, Pitch.CenterY, out int home);
            _jobs.HoldWidth(Man(WingerA), false, 1000, Neutral, Pitch.CenterY, out int away);
            Assert.That(away - Pitch.CenterY, Is.EqualTo(Pitch.CenterY - home));
        }

        [Test]
        public void TheLane_GrowsWithTheExpansion_SoItDoesNotJumpWhenTheBallIsWon()
        {
            _jobs.HoldWidth(Man(WingerA), true, 0, Neutral, Pitch.CenterY, out int shut);
            _jobs.HoldWidth(Man(WingerA), true, 500, Neutral, Pitch.CenterY, out int half);
            _jobs.HoldWidth(Man(WingerA), true, 1000, Neutral, Pitch.CenterY, out int open);

            Assert.That(Math.Abs(half - Pitch.CenterY), Is.GreaterThan(Math.Abs(shut - Pitch.CenterY)));
            Assert.That(Math.Abs(open - Pitch.CenterY), Is.GreaterThan(Math.Abs(half - Pitch.CenterY)));
        }

        [Test]
        public void OnlyWideMenHoldTheWidth_AFlatFoursOutsideMidfielderIsOne()
        {
            Assert.That(_jobs.HoldWidth(Man(Midfielder), true, 1000, Neutral, Pitch.CenterY, out _), Is.False, "a 4-3-3 central midfielder");
            Assert.That(_jobs.HoldWidth(Man(FullBack), true, 1000, Neutral, Pitch.CenterY, out _), Is.False, "a full-back has the overlap");
            Assert.That(_jobs.HoldWidth(Man(Striker), true, 1000, Neutral, Pitch.CenterY, out _), Is.False);

            // A 4-4-2's wide midfielders are encoded CM: they belong on the touchline all the same.
            PositionRole[] flat = Formations.Roles(Formation.F442);
            int[] midfield = Enumerable.Range(0, flat.Length).Where(i => flat[i] == PositionRole.CentralMidfielder).ToArray();
            Assert.That(midfield.Length, Is.EqualTo(4));
            Assert.That(midfield.Count(i => _jobs.IsWideMan(Man(flat, i))), Is.EqualTo(2));
        }

        [Test]
        public void AWideMan_IsHeldOnsideOnlyDownToHisFloor_AndNeverSentPastHisPlace()
        {
            int floor = _cfg.V11WideManFloorDm;
            Assert.That(_jobs.WideManX(true, floor + 50, floor + 90), Is.EqualTo(floor + 50), "a line past the floor holds him on it");
            Assert.That(_jobs.WideManX(true, floor - 60, floor + 90), Is.EqualTo(floor), "a higher line pulls him back to the floor only");
            Assert.That(_jobs.WideManX(true, floor - 60, floor - 20), Is.EqualTo(floor - 20), "his place in the shape is short of it: he stands there");
            Assert.That(_jobs.WideManX(true, floor - 20, floor - 20), Is.EqualTo(floor - 20));
        }

        [Test]
        public void AwayWideMan_HasTheMirroredFloor()
        {
            int floor = _cfg.V11WideManFloorDm;
            Assert.That(_jobs.WideManX(false, Pitch.LengthDm - floor + 60, Pitch.LengthDm - floor - 90), Is.EqualTo(Pitch.LengthDm - floor));
        }

        // ---------------------------------------------------------------- one full-back overlaps

        private bool Overlap(TeamPhase phase, int carrierFlank, int carrierX, out int x, out int y)
        {
            V11Slot man = Man(FullBack);
            int flank = Math.Sign(man.BaseYPermille - 500);
            return _positioning.Overlap(man, true, phase, true, Neutral, carrierX - 100, carrierX,
                Pitch.CenterY + carrierFlank * flank * 250, out x, out y);
        }

        [Test]
        public void FullBack_OverlapsPastTheBall_OnHisSide_InTheFinalTwoThirds()
        {
            foreach (TeamPhase phase in new[] { TeamPhase.Progression, TeamPhase.FinalThird })
            {
                Assert.That(Overlap(phase, 1, 700, out int x, out int y), Is.True, phase.ToString());
                Assert.That(x, Is.GreaterThan(700), "past the man on the ball");
                Assert.That(Math.Abs(y - Pitch.CenterY), Is.GreaterThan(250), "on the outside of him");
            }
        }

        [Test]
        public void TheOtherFullBack_DoesNotGo_NorDoesHeInTheFirstThird()
        {
            Assert.That(Overlap(TeamPhase.Progression, -1, 700, out _, out _), Is.False, "the ball is on the other flank");
            Assert.That(Overlap(TeamPhase.BuildUp, 1, 300, out _, out _), Is.False, "the first third is not an overlap");
        }

        // ---------------------------------------------------------------- holding midfielder

        [Test]
        public void HoldingMidfielder_ATargetBeyondTheBall_IsHeldBehindIt()
        {
            Assert.That(_jobs.HoldBehind(Man(Holding), true, 800, 700, out int x), Is.True);
            Assert.That(x, Is.EqualTo(700 - _cfg.V11HoldingBehindBallDm));

            Assert.That(_jobs.HoldBehind(Man(Holding), false, Pitch.LengthDm - 800, Pitch.LengthDm - 700, out int away), Is.True);
            Assert.That(away, Is.EqualTo(Pitch.LengthDm - 700 + _cfg.V11HoldingBehindBallDm), "mirrored for the away side");
        }

        [Test]
        public void HoldingMidfielder_AlreadyBehindTheBall_IsLeftAlone()
        {
            Assert.That(_jobs.HoldBehind(Man(Holding), true, 500, 700, out int x), Is.False);
            Assert.That(x, Is.EqualTo(500));
        }

        [Test]
        public void HoldingMidfielder_WithTheBallInHisOwnThird_DoesNotDropBehindHisCentreBacks()
        {
            int floor = _cfg.V11HoldingFloorDm;
            Assert.That(_jobs.HoldBehind(Man(Holding), true, floor - 10, 150, out _), Is.False, "the ball is deep: he stands in his place");
            Assert.That(_jobs.HoldBehind(Man(Holding), true, floor + 40, 150, out int x), Is.True);
            Assert.That(x, Is.EqualTo(floor));
        }

        [Test]
        public void TheLimit_MovesWithTheBall_NotInJumps()
        {
            int last = -1;
            for (int ball = 100; ball < 900; ball += 5)
            {
                _jobs.HoldBehind(Man(Holding), true, 1000, ball, out int x);
                if (last >= 0) Assert.That(x - last, Is.InRange(0, 5), $"ball at {ball}");
                last = x;
            }
        }

        [Test]
        public void OnlyTheHoldingMidfielder_IsHeld()
        {
            foreach (int slot in new[] { CentreBack, FullBack, Midfielder, WingerA, Striker })
                Assert.That(_jobs.HoldBehind(Man(slot), true, 800, 700, out _), Is.False, Shape[slot].ToString());
        }

        // ---------------------------------------------------------------- box arrival

        private bool CrossOn(bool home, int x, int y) => _jobs.CrossOn(home, x, y, FinalThirdApproach.Mixed);

        [Test]
        public void ACross_IsOn_FromUpTheWideChannel()
        {
            int from = _cfg.V11CrossOnDepthDm[(int)FinalThirdApproach.Mixed];
            Assert.That(CrossOn(true, from + 50, 80), Is.True);
            Assert.That(CrossOn(true, from + 50, Pitch.WidthDm - 80), Is.True);
            Assert.That(CrossOn(true, from - 50, 80), Is.False, "too far out");
            Assert.That(CrossOn(true, from + 50, Pitch.CenterY), Is.False, "from the middle it is a through ball or a shot");
            Assert.That(CrossOn(false, Pitch.LengthDm - from - 50, 80), Is.True, "mirrored for the away side");
            Assert.That(CrossOn(false, from + 50, 80), Is.False, "that is the away side's own third");
        }

        [Test]
        public void ACrossIsOnSooner_TheEarlierTheSideCrosses()
        {
            int[] from = _cfg.V11CrossOnDepthDm;
            Assert.That(from[(int)FinalThirdApproach.CrossEarly], Is.LessThan(from[(int)FinalThirdApproach.Mixed]));
            Assert.That(from[(int)FinalThirdApproach.Mixed], Is.LessThan(from[(int)FinalThirdApproach.WorkIntoBox]));

            int x = from[(int)FinalThirdApproach.CrossEarly] + 10;
            Assert.That(_jobs.CrossOn(true, x, 80, FinalThirdApproach.CrossEarly), Is.True);
            Assert.That(_jobs.CrossOn(true, x, 80, FinalThirdApproach.Mixed), Is.False);
            Assert.That(_jobs.CrossOn(true, x, 80, FinalThirdApproach.WorkIntoBox), Is.False);
        }

        [Test]
        public void ACutBack_IsOn_FromTheGoalLine_OffTheCentre()
        {
            Assert.That(CrossOn(true, Pitch.LengthDm - 60, Pitch.CenterY + 130), Is.True, "inside the box, by the goal line");
            Assert.That(CrossOn(true, Pitch.LengthDm - 60, Pitch.CenterY + 20), Is.False, "in front of goal that is a shot");
            Assert.That(CrossOn(true, Pitch.LengthDm - 200, Pitch.CenterY + 130), Is.False, "at the top of the box too");
        }

        [Test]
        public void TheCentralMidfielder_ArrivesAtThePenaltySpot()
        {
            Assert.That(_jobs.ArrivesInTheBox(Man(Midfielder)), Is.True);
            _jobs.BoxSpot(true, out int x, out int y);
            Assert.That(Pitch.LengthDm - x, Is.EqualTo(_cfg.V11BoxArrivalGoalLineDm));
            Assert.That(y, Is.EqualTo(Pitch.CenterY));

            _jobs.BoxSpot(false, out int ax, out _);
            Assert.That(ax, Is.EqualTo(_cfg.V11BoxArrivalGoalLineDm));
        }

        [Test]
        public void OnlyACentralMidfielder_ArrivesInTheBox()
        {
            foreach (int slot in new[] { CentreBack, FullBack, Holding, WingerA, Striker })
                Assert.That(_jobs.ArrivesInTheBox(Man(slot)), Is.False, Shape[slot].ToString());

            PositionRole[] flat = Formations.Roles(Formation.F442);
            int[] wide = Enumerable.Range(0, flat.Length).Where(i => flat[i] == PositionRole.CentralMidfielder && _jobs.IsWideMan(Man(flat, i))).ToArray();
            Assert.That(wide, Is.Not.Empty);
            foreach (int slot in wide) Assert.That(_jobs.ArrivesInTheBox(Man(flat, slot)), Is.False, "a wide midfielder holds the width");
        }

        // ---------------------------------------------------------------- in a match, off the trace

        private sealed class Played
        {
            public List<MovementRecord> Records = new();
            public Lineup Home = null!;
            public Lineup Away = null!;
            public PositionRole Role(MovementRecord r) => (r.Side == 0 ? Home : Away).Slots[r.Slot].Role;
        }

        private static readonly Lazy<Played> Match433 = new(() =>
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
            var engine = new MatchEngine(cfg) { MovementSink = log };
            engine.Simulate(home, away, new Pcg32(4242), new MatchTactics(tactic, tactic));
            return new Played { Records = log.Records.ToList(), Home = home, Away = away };
        });

        private static bool OpenPlay(MovementRecord r) => !r.BallDead && r.InPossession;

        [Test]
        public void InAMatch_WingersAreOnTheWidthJob_AndItsTargetStepsAwayFromTheBall()
        {
            Played played = Match433.Value;
            MatchBalance cfg = new BalanceConfig().Match;
            var wide = played.Records.Where(r => OpenPlay(r) && r.Job == MovementJob.Width).ToList();

            Assert.That(wide.Count, Is.GreaterThan(1000), "wingers spend the possession on it");
            Assert.That(wide.Select(r => played.Role(r)).Distinct(), Is.EqualTo(new[] { PositionRole.Winger }));

            // At full expansion a winger's target stays within the step of his lane, and whichever
            // flank he is on it sits lower across the pitch with the ball high than with it low.
            int reach = Pitch.CenterY * cfg.V11WideLaneLeanPercent / 100;
            foreach (var man in wide.Where(r => r.ExpansionPermille == 1000).GroupBy(r => (r.Side, r.Slot)))
            {
                string who = $"side {man.Key.Side} slot {man.Key.Slot}";
                Assert.That(man.Max(r => r.TargetYDm) - man.Min(r => r.TargetYDm), Is.LessThanOrEqualTo(reach + 2), who);

                var ballHigh = man.Where(r => r.BallYDm > Pitch.CenterY + 100).ToList();
                var ballLow = man.Where(r => r.BallYDm < Pitch.CenterY - 100).ToList();
                Assert.That(ballHigh, Is.Not.Empty, who);
                Assert.That(ballLow, Is.Not.Empty, who);
                Assert.That(ballHigh.Average(r => r.TargetYDm), Is.LessThan(ballLow.Average(r => r.TargetYDm)), who);
            }
        }

        [Test]
        public void InAMatch_NoWingerGoesOnASupportingRun()
        {
            Played played = Match433.Value;
            Assert.That(played.Records.Count(r => OpenPlay(r) && r.Job == MovementJob.Support && played.Role(r) == PositionRole.Winger), Is.Zero);
        }

        [Test]
        public void InAMatch_OneFullBackOverlaps_OnlyHisRoleAndOnlyOneAtATime()
        {
            Played played = Match433.Value;
            var overlaps = played.Records.Where(r => r.Job == MovementJob.Overlap).ToList();

            Assert.That(overlaps.Count, Is.GreaterThan(50));
            Assert.That(overlaps.Select(r => played.Role(r)).Distinct(), Is.EqualTo(new[] { PositionRole.FullBack }));
            Assert.That(overlaps.GroupBy(r => (r.Tick, r.Side)).Max(g => g.Count()), Is.EqualTo(1));
        }

        [Test]
        public void InAMatch_TheHoldingMidfielder_StaysBehindTheBall()
        {
            Played played = Match433.Value;
            MatchBalance cfg = new BalanceConfig().Match;
            var held = played.Records.Where(r => r.Job == MovementJob.HoldBehind).ToList();

            Assert.That(held.Select(r => played.Role(r)).Distinct(), Is.EqualTo(new[] { PositionRole.DefensiveMidfielder }));
            Assert.That(held.Count, Is.GreaterThan(0));

            // Every target of his with his side on the ball, the ball past his floor (as he last read it
            // the target can trail a pass by a beat, hence the slack of a ball's flight).
            var targets = played.Records.Where(r => OpenPlay(r) && r.Steered && r.BallOwnerSide == r.Side
                                                    && played.Role(r) == PositionRole.DefensiveMidfielder
                                                    && r.Job != MovementJob.Ball && r.Job != MovementJob.Chase
                                                    && Depth(r, r.BallXDm) > cfg.V11HoldingFloorDm + 100).ToList();
            Assert.That(targets.Count, Is.GreaterThan(500));
            double behind = targets.Count(r => Depth(r, r.TargetXDm) <= Depth(r, r.BallXDm)) * 100.0 / targets.Count;
            Assert.That(behind, Is.GreaterThanOrEqualTo(97.0));
        }

        private static int Depth(MovementRecord r, int x) => r.Side == 0 ? x : Pitch.LengthDm - x;

        [Test]
        public void InAMatch_OneCentralMidfielderArrives_WhenACrossIsOn()
        {
            Played played = Match433.Value;
            var cfg = new BalanceConfig().Match;
            var arrivals = played.Records.Where(r => r.Job == MovementJob.BoxArrival).ToList();

            Assert.That(arrivals.Count, Is.GreaterThan(50));
            Assert.That(arrivals.Select(r => played.Role(r)).Distinct(), Is.EqualTo(new[] { PositionRole.CentralMidfielder }));
            Assert.That(arrivals.GroupBy(r => (r.Tick, r.Side)).Max(g => g.Count()), Is.EqualTo(1));
            Assert.That(arrivals.All(r => r.InPossession), Is.True);

            // He runs at the penalty spot, held onside of the line.
            foreach (MovementRecord r in arrivals.Take(2000))
            {
                Assert.That(r.TargetYDm, Is.EqualTo(Pitch.CenterY).Within(1));
                Assert.That(Depth(r, r.TargetXDm), Is.LessThanOrEqualTo(Pitch.LengthDm - cfg.V11BoxArrivalGoalLineDm + 1));
                Assert.That(Depth(r, r.TargetXDm), Is.LessThanOrEqualTo(Depth(r, r.OffsideLineXDm) - cfg.V11OnsideHoldDm + 1));
            }
        }
    }
}
