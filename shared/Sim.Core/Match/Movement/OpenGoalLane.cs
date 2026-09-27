namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// R4's open goal as one piece of geometry, so the V11 brain that acts on it and the realism
    /// analyzer that measures it cannot drift apart. In decimetres; the attacked goal line is at
    /// <c>goalX</c>, the posts at the centre line plus and minus the goal's half-width.
    /// </summary>
    internal static class OpenGoalLane
    {
        /// <summary>
        /// Close enough, and with enough of the goal mouth in view, for an empty lane to be an
        /// open goal: within <paramref name="rangeDm"/> of the goal centre, seeing the mouth at an
        /// angle whose sine is at least <paramref name="minMouthSinePermille"/>.
        /// </summary>
        public static bool InRange(int bx, int by, int goalX, int rangeDm, int minMouthSinePermille)
        {
            long dx = goalX - bx, dy = Pitch.CenterY - by;
            if (dx * dx + dy * dy > (long)rangeDm * rangeDm) return false;
            return MovementGeometry.GoalMouthSinePermille(bx, by, goalX) >= minMouthSinePermille;
        }

        /// <summary>
        /// Whether an outfield defender at (<paramref name="px"/>, <paramref name="py"/>) closes the
        /// lane from the ball to the posts: inside that triangle (edges included), within
        /// <paramref name="marginDm"/> of either of its sides — a stride from the line of the shot,
        /// where he blocks it — or within <paramref name="freeRadiusDm"/> of the ball, on him.
        /// </summary>
        public static bool Closes(int px, int py, int bx, int by, int goalX, int marginDm, int freeRadiusDm)
        {
            int low = Pitch.CenterY - MovementGeometry.GoalHalfWidthDm;
            int high = Pitch.CenterY + MovementGeometry.GoalHalfWidthDm;
            if (InTriangle(px, py, bx, by, goalX, low, goalX, high)) return true;

            long free = (long)freeRadiusDm * freeRadiusDm;
            if (U.DistanceSq(px, py, bx, by) < free) return true;

            long margin = (long)marginDm * marginDm;
            return U.DistanceSqToSegment(px, py, bx, by, goalX, low) <= margin
                   || U.DistanceSqToSegment(px, py, bx, by, goalX, high) <= margin;
        }

        /// <summary>Inclusive of the edges: a man standing on the line to a post is in it.</summary>
        public static bool InTriangle(long px, long py, long ax, long ay, long bx, long by, long cx, long cy)
        {
            long d1 = Cross(px, py, ax, ay, bx, by);
            long d2 = Cross(px, py, bx, by, cx, cy);
            long d3 = Cross(px, py, cx, cy, ax, ay);
            bool negative = d1 < 0 || d2 < 0 || d3 < 0;
            bool positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive);
        }

        private static long Cross(long px, long py, long ax, long ay, long bx, long by) =>
            (bx - ax) * (py - ay) - (by - ay) * (px - ax);
    }
}
