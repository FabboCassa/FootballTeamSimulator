using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Role jobs (free-ball spec R7, believable-match R4). Pure and draw-free, in decimetres on the
    /// pitch, so each job can be scripted in a test.
    ///
    /// Out of possession:
    /// - <see cref="Step"/>: the near-side full-back steps out to a wide man on the ball;
    /// - <see cref="Tuck"/>: with the ball on the other flank, the far-side full-back tucks in;
    /// - <see cref="Screen"/>: the holding midfielder stands on the passing lane to the most
    ///   advanced opponent.
    ///
    /// In possession:
    /// - <see cref="HoldWidth"/>: a wide man holds his lane, in off the line with the ball on his side;
    /// - <see cref="HoldBehind"/>: the holding midfielder stays behind the ball;
    /// - <see cref="CrossOn"/> and <see cref="BoxSpot"/>: a central midfielder arrives in the box
    ///   when a cross or cut-back is on, the striker attacks the <see cref="NearPost"/> and the
    ///   far-side wide man the <see cref="FarPost"/> (free-ball spec R5).
    /// (The full-back's overlap is <see cref="V11Positioning.Overlap"/>.)
    ///
    /// Each job is a target that moves with the ball and the men it reads, so it changes
    /// continuously rather than jumping between grid spots.
    /// </summary>
    public sealed class V11RoleJobs
    {
        private readonly MatchBalance _cfg;
        private readonly V11Positioning _positioning;

        public V11RoleJobs(MatchBalance cfg)
        {
            _cfg = cfg;
            _positioning = new V11Positioning(cfg);
        }

        /// <summary>
        /// A full-back whose flank the opponent on the ball is out on (at least V11StepFlankMinDm
        /// off the centre) and within V11StepReachDm of his place steps to him: V11StepStandOffDm
        /// off him on the line to the centre of his own goal.
        /// </summary>
        public bool Step(
            V11Slot man, bool home, int spotXDm, int spotYDm, int carrierXDm, int carrierYDm,
            out int xDm, out int yDm)
        {
            xDm = spotXDm;
            yDm = spotYDm;
            int flank = Flank(man, home);
            if (man.Role != PositionRole.FullBack || flank == 0) return false;
            if ((carrierYDm - Pitch.CenterY) * flank < _cfg.V11StepFlankMinDm) return false;
            if (U.DistanceSq(spotXDm, spotYDm, carrierXDm, carrierYDm) > (long)_cfg.V11StepReachDm * _cfg.V11StepReachDm)
                return false;

            U.Scaled(MovementGeometry.OwnGoalX(home) - carrierXDm, Pitch.CenterY - carrierYDm, _cfg.V11StepStandOffDm,
                out int dx, out int dy);
            xDm = Pitch.ClampX(carrierXDm + dx);
            yDm = Pitch.ClampY(carrierYDm + dy);
            return true;
        }

        /// <summary>
        /// A full-back with the ball at least V11TuckBallOffDm off the centre on the OTHER flank
        /// tucks V11TuckDm in from his place toward the centre-backs, never past the centre.
        /// </summary>
        public bool Tuck(V11Slot man, bool home, int spotYDm, int ballYDm, out int yDm)
        {
            yDm = spotYDm;
            int flank = Flank(man, home);
            if (man.Role != PositionRole.FullBack || flank == 0) return false;
            if ((ballYDm - Pitch.CenterY) * flank > -_cfg.V11TuckBallOffDm) return false;

            int inside = spotYDm - flank * _cfg.V11TuckDm;
            yDm = flank > 0 ? (inside > Pitch.CenterY ? inside : Pitch.CenterY) : (inside < Pitch.CenterY ? inside : Pitch.CenterY);
            return yDm != spotYDm;
        }

        /// <summary>
        /// The holding midfielder, with the ball further from his goal than the most advanced
        /// opponent and the lane between them at least V11ScreenMinLaneDm long, stands on that lane
        /// V11ScreenFromForwardDm in front of the forward, no further than V11ScreenLeashDm from
        /// his place.
        /// </summary>
        public bool Screen(
            V11Slot man, bool home, int spotXDm, int spotYDm, int ballXDm, int ballYDm, int forwardXDm, int forwardYDm,
            out int xDm, out int yDm)
        {
            xDm = spotXDm;
            yDm = spotYDm;
            if (man.Role != PositionRole.DefensiveMidfielder) return false;
            if (Depth(home, ballXDm) <= Depth(home, forwardXDm)) return false;
            if (U.DistanceSq(ballXDm, ballYDm, forwardXDm, forwardYDm) < (long)_cfg.V11ScreenMinLaneDm * _cfg.V11ScreenMinLaneDm)
                return false;

            U.Scaled(ballXDm - forwardXDm, ballYDm - forwardYDm, _cfg.V11ScreenFromForwardDm, out int dx, out int dy);
            int x = forwardXDm + dx, y = forwardYDm + dy;
            int lx = x - spotXDm, ly = y - spotYDm;
            int leash = _cfg.V11ScreenLeashDm;
            if ((long)lx * lx + (long)ly * ly > (long)leash * leash)
            {
                U.Scaled(lx, ly, leash, out lx, out ly);
                x = spotXDm + lx;
                y = spotYDm + ly;
            }

            xDm = Pitch.ClampX(x);
            yDm = Pitch.ClampY(y);
            return true;
        }

        /// <summary>A winger, or a midfielder on the outside of his line: the man who gives the side its width.</summary>
        public bool IsWideMan(V11Slot man)
        {
            bool mayBe = man.Role == PositionRole.Winger || man.Role == PositionRole.CentralMidfielder
                         || man.Role == PositionRole.AttackingMidfielder;
            return mayBe && System.Math.Abs(man.BaseYPermille - 500) >= _cfg.V11WideManPermille;
        }

        /// <summary>
        /// With the ball, a wide man stands on his lane (<see cref="V11Positioning.LaneY"/>), not
        /// wherever the ball slides the block: the far-side one is the switch, the near-side one
        /// comes in off the line and leaves the outside to the man on the ball and the overlap.
        /// </summary>
        public bool HoldWidth(V11Slot man, bool home, int expansionPermille, TacticInstructions instructions, int ballYDm, out int yDm)
        {
            yDm = Pitch.CenterY;
            if (!IsWideMan(man)) return false;
            yDm = _positioning.LaneY(man, home, expansionPermille, instructions, ballYDm);
            return true;
        }

        /// <summary>
        /// How far up a wide man holds: his place, held onside of the line, but a very high line
        /// pulls him back only as far as V11WideManFloorDm.
        /// </summary>
        public int WideManX(bool home, int heldXDm, int rawXDm)
        {
            int depth = System.Math.Min(Depth(home, rawXDm), System.Math.Max(Depth(home, heldXDm), _cfg.V11WideManFloorDm));
            return home ? depth : Pitch.LengthDm - depth;
        }

        /// <summary>
        /// The holding midfielder going further upfield than V11HoldingBehindBallDm behind the ball
        /// (a target of any job) is held there. The limit never comes back past V11HoldingFloorDm
        /// and it moves with the ball, so it pulls his target continuously.
        /// </summary>
        public bool HoldBehind(V11Slot man, bool home, int xDm, int ballXDm, out int heldXDm)
        {
            heldXDm = xDm;
            if (man.Role != PositionRole.DefensiveMidfielder) return false;
            int limit = Depth(home, ballXDm) - _cfg.V11HoldingBehindBallDm;
            if (limit < _cfg.V11HoldingFloorDm) limit = _cfg.V11HoldingFloorDm;
            if (Depth(home, xDm) <= limit) return false;
            heldXDm = home ? limit : Pitch.LengthDm - limit;
            return true;
        }

        /// <summary>
        /// Whether a man on the ball here can cross or cut back: up the wide channel, as far up as
        /// the side's Final-third approach asks, or near the goal line off the centre.
        /// </summary>
        public bool CrossOn(bool home, int carrierXDm, int carrierYDm, FinalThirdApproach approach)
        {
            int depth = Depth(home, carrierXDm);
            int off = System.Math.Abs(carrierYDm - Pitch.CenterY);
            if (depth >= Pitch.LengthDm - _cfg.V11CutBackGoalLineDm && off >= _cfg.V11CutBackOffCentreDm) return true;
            return depth >= MovementTactics.Pick(_cfg.V11CrossOnDepthDm, (int)approach)
                   && off >= Pitch.CenterY - _cfg.V11CrossChannelDm;
        }

        /// <summary>A central midfielder, on or in front of the middle of his line: who may arrive in the box.</summary>
        public bool ArrivesInTheBox(V11Slot man) =>
            (man.Role == PositionRole.CentralMidfielder || man.Role == PositionRole.AttackingMidfielder) && !IsWideMan(man);

        /// <summary>Where the man arriving in the box is going: the penalty spot.</summary>
        public void BoxSpot(bool home, out int xDm, out int yDm)
        {
            int depth = Pitch.LengthDm - _cfg.V11BoxArrivalGoalLineDm;
            xDm = home ? depth : Pitch.LengthDm - depth;
            yDm = Pitch.CenterY;
        }

        /// <summary>The near post the striker attacks for a cross from <paramref name="ballYDm"/>: on the ball's side of the goal.</summary>
        public void NearPost(bool home, int ballYDm, out int xDm, out int yDm)
        {
            int depth = Pitch.LengthDm - _cfg.V11NearPostGoalLineDm;
            xDm = home ? depth : Pitch.LengthDm - depth;
            yDm = Pitch.CenterY + BallFlank(ballYDm) * _cfg.V11NearPostOffCentreDm;
        }

        /// <summary>The far post the far-side wide man attacks for a cross from <paramref name="ballYDm"/>: across the goal from the ball.</summary>
        public void FarPost(bool home, int ballYDm, out int xDm, out int yDm)
        {
            int depth = Pitch.LengthDm - _cfg.V11FarPostGoalLineDm;
            xDm = home ? depth : Pitch.LengthDm - depth;
            yDm = Pitch.CenterY - BallFlank(ballYDm) * _cfg.V11FarPostOffCentreDm;
        }

        /// <summary>A wide man whose flank is across the pitch from the ball: who attacks the far post.</summary>
        public bool OnTheFarSide(V11Slot man, bool home, int ballYDm) => IsWideMan(man) && Flank(man, home) == -BallFlank(ballYDm);

        private static int BallFlank(int ballYDm) => ballYDm < Pitch.CenterY ? -1 : 1;

        /// <summary>
        /// Issue #206: whether a point (where a pass to an opponent arrives) is in the central zone
        /// this side guards, within V11ZoneGuardRangeDm of its own goal's centre.
        /// </summary>
        public bool InGuardedZone(bool home, int xDm, int yDm)
        {
            long range = _cfg.V11ZoneGuardRangeDm;
            return U.DistanceSq(xDm, yDm, MovementGeometry.OwnGoalX(home), Pitch.CenterY) <= range * range;
        }

        /// <summary>
        /// Issue #206: where the man closing that point stands, V11ZoneCloseStandOffDm off it on the
        /// line to the centre of his own goal, so the receiver is pressed the moment he arrives and
        /// has the defender between him and the goal.
        /// </summary>
        public void ZoneClose(bool home, int threatXDm, int threatYDm, out int xDm, out int yDm)
        {
            U.Scaled(MovementGeometry.OwnGoalX(home) - threatXDm, Pitch.CenterY - threatYDm, _cfg.V11ZoneCloseStandOffDm,
                out int dx, out int dy);
            xDm = Pitch.ClampX(threatXDm + dx);
            yDm = Pitch.ClampY(threatYDm + dy);
        }

        /// <summary>The opponent nearest this side's own goal line: the one the screen guards.</summary>
        public static int MostAdvanced(bool home, int[] foeXDm, int foes)
        {
            int best = -1, bestDepth = int.MaxValue;
            for (int j = 0; j < foes; j++)
            {
                int depth = Depth(home, foeXDm[j]);
                if (depth < bestDepth)
                {
                    bestDepth = depth;
                    best = j;
                }
            }

            return best;
        }

        /// <summary>Which side of the centre his flank is, in pitch y: +1, -1, or 0 for a central man.</summary>
        private static int Flank(V11Slot man, bool home)
        {
            int f = MovementGeometry.Direction(home) * (man.BaseYPermille - 500);
            return f > 0 ? 1 : (f < 0 ? -1 : 0);
        }

        private static int Depth(bool home, int xDm) => home ? xDm : Pitch.LengthDm - xDm;
    }
}
