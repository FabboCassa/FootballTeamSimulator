namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Curved runs (free-ball spec R7): a man running to a spot does not go through an opponent
    /// standing on his straight line, he bends round him. Pure and draw-free, in decimetres.
    /// </summary>
    public static class RunCurve
    {
        /// <summary>
        /// The waypoint of a run from (<paramref name="fromX"/>, <paramref name="fromY"/>) to
        /// (<paramref name="toX"/>, <paramref name="toY"/>): beside the first opponent ahead of him
        /// within <paramref name="triggerDm"/> of the straight line, <paramref name="clearanceDm"/>
        /// off him on the other side of the line from him (the left, for an opponent dead on it).
        /// False when no opponent is in the way, the run is too short to bend, or the opponent
        /// stands within the clearance of the target itself (the run ends at him, it does not pass him)
        /// or of the man (already alongside him: bending then would turn the run more than 45 degrees).
        /// </summary>
        public static bool Bend(
            int fromX, int fromY, int toX, int toY, int[] foeX, int[] foeY, int foes,
            int triggerDm, int clearanceDm, out int viaX, out int viaY)
        {
            viaX = toX;
            viaY = toY;
            long dx = toX - fromX, dy = toY - fromY;
            long lengthSq = dx * dx + dy * dy;
            if (lengthSq <= 4L * clearanceDm * clearanceDm) return false;

            int length = MovementGeometry.Sqrt(lengthSq > int.MaxValue ? int.MaxValue : (int)lengthSq);
            long trigger = (long)triggerDm * length;
            long firstAlongAllowed = (long)clearanceDm * length;
            long lastAlong = lengthSq - firstAlongAllowed;

            int first = -1;
            long firstAlong = long.MaxValue, firstSide = 0;
            for (int j = 0; j < foes; j++)
            {
                long ox = foeX[j] - fromX, oy = foeY[j] - fromY;
                long along = ox * dx + oy * dy;          // his distance along the run, times its length
                if (along < firstAlongAllowed || along >= lastAlong) continue;

                long side = oy * dx - ox * dy;           // his distance off it, times its length; left > 0
                if (side >= trigger || -side >= trigger) continue;

                if (along < firstAlong)
                {
                    firstAlong = along;
                    firstSide = side;
                    first = j;
                }
            }

            if (first < 0) return false;

            // The left normal of the run is (-dy, dx); the man passes on the other side from him.
            int sign = firstSide > 0 ? -1 : 1;
            viaX = foeX[first] + (int)(sign * -dy * clearanceDm / length);
            viaY = foeY[first] + (int)(sign * dx * clearanceDm / length);
            return true;
        }
    }
}
