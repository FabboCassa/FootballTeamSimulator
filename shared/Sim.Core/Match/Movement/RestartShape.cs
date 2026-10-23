using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Where the men stand for a goal kick played out from the back and for a throw-in
    /// (real-match spec R11). Pure and draw-free, in decimetres, so it can be scripted in a test.
    /// The goal kick fills <c>placed</c> with the men it moves and their spots, the rest keep
    /// theirs; the throw-in names the men who offer, and <see cref="OfferSpot"/> says where.
    /// </summary>
    public static class RestartShape
    {
        /// <summary>
        /// The goal kick: the two outermost centre-backs split to the corners of the box, a middle
        /// one keeps his spot, and every full-back goes wide on his own flank. One centre-back
        /// alone does not split.
        /// </summary>
        /// <param name="baseYPermille">Each man's place across his formation, for the home side, as <see cref="V11Slot.BaseYPermille"/>.</param>
        /// <param name="eligible">False for the keeper and anybody sent off.</param>
        public static void GoalKick(
            IReadOnlyList<PositionRole> roles, int[] baseYPermille, bool[] eligible, bool home, MatchBalance cfg,
            bool[] placed, int[] xDm, int[] yDm)
        {
            int dir = MovementGeometry.Direction(home);
            int goalX = MovementGeometry.OwnGoalX(home);
            int low = -1, high = -1;

            for (int i = 0; i < placed.Length; i++)
            {
                placed[i] = false;
                if (!eligible[i]) continue;

                if (roles[i] == PositionRole.FullBack)
                {
                    int inset = cfg.GoalKickFullBackInsetDm;
                    Place(i, goalX + dir * cfg.GoalKickFullBackDepthDm,
                        Flank(dir, baseYPermille[i]) < 0 ? inset : Pitch.WidthDm - inset, placed, xDm, yDm);
                }
                else if (roles[i] == PositionRole.CentreBack)
                {
                    int flank = Flank(dir, baseYPermille[i]);
                    if (low < 0 || flank < Flank(dir, baseYPermille[low])) low = i;
                    if (high < 0 || flank >= Flank(dir, baseYPermille[high])) high = i;
                }
            }

            if (low < 0 || low == high) return;
            int cbX = goalX + dir * cfg.GoalKickCentreBackDepthDm;
            Place(low, cbX, Pitch.CenterY - cfg.GoalKickCentreBackAcrossDm, placed, xDm, yDm);
            Place(high, cbX, Pitch.CenterY + cfg.GoalKickCentreBackAcrossDm, placed, xDm, yDm);
        }

        /// <summary>
        /// The throw-in: the two eligible men nearest the ball offer for it, each going to
        /// <see cref="OfferSpot"/>.
        /// </summary>
        /// <param name="eligible">False for the keeper, the taker and anybody sent off.</param>
        /// <param name="offers">Filled with the men who offer.</param>
        /// <returns>How many men offer: two, or fewer when fewer are left.</returns>
        public static int ThrowIn(int ballXDm, int ballYDm, int[] xsDm, int[] ysDm, bool[] eligible, bool[] offers)
        {
            for (int i = 0; i < offers.Length; i++) offers[i] = false;

            int first = Nearest(ballXDm, ballYDm, xsDm, ysDm, eligible, -1);
            if (first < 0) return 0;
            offers[first] = true;
            int second = Nearest(ballXDm, ballYDm, xsDm, ysDm, eligible, first);
            if (second < 0) return 1;
            offers[second] = true;
            return 2;
        }

        /// <summary>
        /// Where a man offering for a throw-in stands: the spot within MatchBalance.ThrowInOfferDm
        /// of the ball nearest his place in the shape — his place itself when it is that close,
        /// otherwise the point that far out on the line from the ball to it. Coming no nearer
        /// than he must keeps him as close to his place as he can be when the ball is back in play.
        /// </summary>
        public static void OfferSpot(int ballXDm, int ballYDm, int placeXDm, int placeYDm, MatchBalance cfg,
            out int xDm, out int yDm)
        {
            int reach = cfg.ThrowInOfferDm;
            int dx = placeXDm - ballXDm, dy = placeYDm - ballYDm;
            int d = MovementGeometry.Sqrt(dx * dx + dy * dy);
            xDm = d <= reach ? placeXDm : ballXDm + dx * reach / d;
            yDm = d <= reach ? placeYDm : ballYDm + dy * reach / d;
        }

        /// <summary>Which side of the pitch a man plays on, on the y axis: negative is the y = 0 side.</summary>
        private static int Flank(int dir, int baseYPermille) => dir * (baseYPermille - 500);

        /// <summary>The eligible man nearest the ball other than <paramref name="skip"/>; ties go to the lower slot.</summary>
        private static int Nearest(int x, int y, int[] xs, int[] ys, bool[] eligible, int skip)
        {
            int best = -1;
            long bestSq = long.MaxValue;
            for (int i = 0; i < xs.Length; i++)
            {
                if (!eligible[i] || i == skip) continue;
                long sq = Sq(xs[i] - x, ys[i] - y);
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }

            return best;
        }

        private static long Sq(long dx, long dy) => dx * dx + dy * dy;

        private static void Place(int i, int x, int y, bool[] placed, int[] xDm, int[] yDm)
        {
            placed[i] = true;
            xDm[i] = x;
            yDm[i] = y;
        }
    }
}
