using Sim.Core.Config;

namespace Sim.Core.Match.Movement.Models
{
    /// <summary>
    /// Expected threat (Karun Singh): the chance that having the ball at a spot ends in a goal
    /// within the next few actions, in ten-thousandths of a goal. Moving the ball is worth the
    /// difference between where it ends and where it started, which is what lets a pass, a carry
    /// and a cross be priced in one currency.
    ///
    /// The grid lives in <see cref="ActionModelBalance"/> and is read bilinearly between cell
    /// centres, so a carry of a few metres is worth something instead of nothing until it crosses
    /// a cell edge. Pure, static, integer, allocation-free.
    /// </summary>
    public static class ExpectedThreat
    {
        private const int FracBits = 10;
        private const int One = 1 << FracBits;

        /// <summary>
        /// xT of the ball at (<paramref name="xDm"/>, <paramref name="yDm"/>) for the side that
        /// attacks toward X = <see cref="Pitch.LengthDm"/> when <paramref name="attacksHighX"/>,
        /// toward X = 0 otherwise. A malformed grid reads as no threat anywhere.
        /// </summary>
        public static int ValuePer10k(int xDm, int yDm, bool attacksHighX, ActionModelBalance cfg)
        {
            int cols = cfg.XtColumns;
            int rows = cfg.XtRows;
            int[] grid = cfg.XtGridPer10k;
            if (cols < 1 || rows < 1 || grid == null || grid.Length < cols * rows) return 0;

            int x = Pitch.ClampX(xDm);
            int y = Pitch.ClampY(yDm);
            if (!attacksHighX)
            {
                // A half-turn, not a reflection, so each side's left wing reads the same row.
                x = Pitch.LengthDm - x;
                y = Pitch.WidthDm - y;
            }

            CellCoordinate(x, Pitch.LengthDm, cols, out int c0, out int c1, out int fx);
            CellCoordinate(y, Pitch.WidthDm, rows, out int r0, out int r1, out int fy);

            long top = (long)grid[r0 * cols + c0] * (One - fx) + (long)grid[r0 * cols + c1] * fx;
            long bottom = (long)grid[r1 * cols + c0] * (One - fx) + (long)grid[r1 * cols + c1] * fx;
            long blended = top * (One - fy) + bottom * fy;
            return (int)((blended + (1L << (2 * FracBits - 1))) >> (2 * FracBits));
        }

        /// <summary>What moving the ball from one spot to another is worth to the side on it.</summary>
        public static int GainPer10k(
            int fromXDm, int fromYDm, int toXDm, int toYDm, bool attacksHighX, ActionModelBalance cfg)
            => ValuePer10k(toXDm, toYDm, attacksHighX, cfg) - ValuePer10k(fromXDm, fromYDm, attacksHighX, cfg);

        /// <summary>
        /// The two cells a coordinate falls between, measured from cell centres, and how far it is
        /// from the first toward the second in 1/1024ths. Beyond the outer centres it holds the
        /// edge cell.
        /// </summary>
        private static void CellCoordinate(int at, int span, int cells, out int first, out int second, out int frac)
        {
            int u = at * cells * One / span - One / 2;
            int max = (cells - 1) * One;
            u = u < 0 ? 0 : (u > max ? max : u);

            first = u >> FracBits;
            frac = u & (One - 1);
            second = first + 1 < cells ? first + 1 : first;
        }
    }
}
