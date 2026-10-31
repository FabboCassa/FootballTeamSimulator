using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Role jobs out of possession (free-ball spec R7, believable-match R4). Pure and draw-free, in
    /// decimetres on the pitch, so each job can be scripted in a test:
    ///
    /// - <see cref="Step"/>: the near-side full-back steps out to a wide man on the ball;
    /// - <see cref="Tuck"/>: with the ball on the other flank, the far-side full-back tucks in;
    /// - <see cref="Screen"/>: the holding midfielder stands on the passing lane to the most
    ///   advanced opponent.
    ///
    /// Each job is a target that moves with the ball and the men it reads, so it changes
    /// continuously rather than jumping between grid spots.
    /// </summary>
    public sealed class V11RoleJobs
    {
        private readonly MatchBalance _cfg;

        public V11RoleJobs(MatchBalance cfg)
        {
            _cfg = cfg;
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
