using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Real-match spec R9: the short option. In the build-up and the progression the men nearest
    /// the ball do not wait in their places for a ball they cannot be given; each offers himself
    /// on his own side of the man on the ball, a short pass away, at the angle whose lane no
    /// opponent stands on and where nobody is on top of him. A pressed carrier with somebody to
    /// give it to keeps the ball, and the chain goes on.
    ///
    /// The candidates sit on a ring of <see cref="MatchBalance.V11SupportOfferDm"/> round the
    /// ball, from just ahead of square to just behind it on the man's side. Each is scored on the
    /// lane from the ball (the nearest opponent to it, up to <see cref="MatchBalance.V11SupportOfferLaneDm"/>),
    /// the room at the spot (up to twice that), a bonus for the ground it gains and a cost for the
    /// ground he has to cover. Pure, integer and draw-free, in decimetres; home attacks toward
    /// x = <see cref="Pitch.LengthDm"/>.
    /// </summary>
    public sealed class V11SupportAngles
    {
        /// <summary>The ring's directions from the attacking direction: 30, 60, 90, 120 and 150 degrees, cosine and sine in permille.</summary>
        private static readonly int[] Cos = { 866, 500, 0, -500, -866 };
        private static readonly int[] Sin = { 500, 866, 1000, 866, 500 };

        /// <summary>No offer within this of the touchline: a spot on it is half a lane.</summary>
        private const int TouchlineMarginDm = 20;

        public const int NoAngle = -1;

        private readonly MatchBalance _cfg;

        public V11SupportAngles(MatchBalance cfg)
        {
            _cfg = cfg;
        }

        /// <summary>The side of the ball a man offers on: +1 toward higher y, -1 toward lower, his own when he is level with it.</summary>
        public static int SideOf(int ballYDm, int manYDm) =>
            manYDm > ballYDm || (manYDm == ballYDm && ballYDm < Pitch.CenterY) ? 1 : -1;

        /// <summary>
        /// The best angle for a man at (<paramref name="manXDm"/>, <paramref name="manYDm"/>) offering
        /// on <paramref name="across"/> (+1/-1) of the ball, or <see cref="NoAngle"/> when the ring
        /// has no spot on that side inside the pitch.
        /// </summary>
        public int BestAngle(
            bool home, int ballXDm, int ballYDm, int across, int manXDm, int manYDm,
            int[] foeXDm, int[] foeYDm, int foes)
        {
            int best = NoAngle, bestScore = int.MinValue;
            for (int a = 0; a < Cos.Length; a++)
            {
                if (!Spot(home, ballXDm, ballYDm, across, a, out int x, out int y)) continue;
                int score = Score(home, ballXDm, ballYDm, x, y, manXDm, manYDm, foeXDm, foeYDm, foes);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = a;
                }
            }

            return best;
        }

        /// <summary>The spot at <paramref name="angle"/> on <paramref name="across"/> of the ball; false when it falls off the pitch.</summary>
        public bool Spot(bool home, int ballXDm, int ballYDm, int across, int angle, out int xDm, out int yDm)
        {
            int reach = _cfg.V11SupportOfferDm;
            xDm = ballXDm + MovementGeometry.Direction(home) * Cos[angle] * reach / 1000;
            yDm = ballYDm + across * Sin[angle] * reach / 1000;
            return xDm >= 0 && xDm <= Pitch.LengthDm
                   && yDm >= TouchlineMarginDm && yDm <= Pitch.WidthDm - TouchlineMarginDm;
        }

        private int Score(
            bool home, int ballXDm, int ballYDm, int x, int y, int manXDm, int manYDm,
            int[] foeXDm, int[] foeYDm, int foes)
        {
            int laneCap = _cfg.V11SupportOfferLaneDm;
            long lane = (long)laneCap * laneCap, room = 4L * laneCap * laneCap;
            long onTheBall = (long)_cfg.PressureRadiusDm * _cfg.PressureRadiusDm;
            for (int j = 0; j < foes; j++)
            {
                // The man closing the ball down is pressure, not a body on the lane: it is played past him.
                long toLane = U.DistanceSq(foeXDm[j], foeYDm[j], ballXDm, ballYDm) < onTheBall
                    ? lane
                    : U.DistanceSqToSegment(foeXDm[j], foeYDm[j], ballXDm, ballYDm, x, y);
                if (toLane < lane) lane = toLane;
                long toSpot = U.DistanceSq(foeXDm[j], foeYDm[j], x, y);
                if (toSpot < room) room = toSpot;
            }

            int gain = MovementGeometry.Direction(home) * (x - ballXDm);
            int travel = MovementGeometry.Distance(manXDm, manYDm, x, y);
            return 2 * MovementGeometry.Sqrt((int)lane) + MovementGeometry.Sqrt((int)room)
                   + gain * _cfg.V11SupportOfferGainPercent / 100
                   - travel * _cfg.V11SupportOfferTravelPercent / 100;
        }
    }
}
