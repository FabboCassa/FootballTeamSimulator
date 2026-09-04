using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Pitch geometry the movement model works in (task 13.1): formation anchors in
    /// decimetres, role forwardness, goal mouths and box tests, plus the integer maths
    /// (square root, clamped steps) the steering needs. Integer-only so the whole
    /// position stream stays bit-identical across .NET, Mono and IL2CPP.
    ///
    /// Anchors come from <see cref="FormationGeometry"/> — the single source the tactics
    /// screen and the positional tilt already share — so what the user arranges on the
    /// Tactics pitch is literally the shape that lines up on the match pitch. A slot with
    /// a custom <see cref="LineupSlot.Position"/> (free positioning, task 6.10) uses it;
    /// the rest fall back to their canonical role anchor.
    /// </summary>
    internal static class MovementGeometry
    {
        /// <summary>Half-width of the goal mouth (7.32 m).</summary>
        public const int GoalHalfWidthDm = 37;

        /// <summary>Penalty area depth (16.5 m) and half-width (20.16 m).</summary>
        public const int BoxDepthDm = 165;
        public const int BoxHalfWidthDm = 201;

        /// <summary>Attacking direction of a side: home attacks toward X = LengthDm.</summary>
        public static int Direction(bool home) => home ? 1 : -1;

        /// <summary>X of the goal line a side attacks.</summary>
        public static int AttackedGoalX(bool home) => home ? Pitch.LengthDm : 0;

        /// <summary>X of the goal line a side defends.</summary>
        public static int OwnGoalX(bool home) => home ? 0 : Pitch.LengthDm;

        /// <summary>True when the point sits inside the penalty area a side defends.</summary>
        public static bool InOwnBox(bool home, int x, int y)
        {
            int depth = home ? x : Pitch.LengthDm - x;
            return depth <= BoxDepthDm
                && y >= Pitch.CenterY - BoxHalfWidthDm
                && y <= Pitch.CenterY + BoxHalfWidthDm;
        }

        /// <summary>
        /// Formation anchors in pitch decimetres for one lineup. The away side is rotated
        /// 180 degrees (both axes), so the two shapes face each other.
        /// </summary>
        public static PitchPoint[] Anchors(Lineup lineup, bool home, MatchBalance cfg)
        {
            int n = lineup.Slots.Count;
            var roles = new List<PositionRole>(n);
            for (int i = 0; i < n; i++) roles.Add(lineup.Slots[i].Role);

            var anchors = new PitchPoint[n];
            for (int i = 0; i < n; i++)
            {
                SlotPosition? custom = lineup.Slots[i].Position;
                int px = custom.HasValue ? custom.Value.XPermille : FormationGeometry.AnchorX(roles[i], cfg);
                int py = custom.HasValue ? custom.Value.YPermille : FormationGeometry.AnchorY(roles, i, cfg);

                int x = px * Pitch.LengthDm / 1000;
                int y = py * Pitch.WidthDm / 1000;
                if (!home)
                {
                    x = Pitch.LengthDm - x;
                    y = Pitch.WidthDm - y;
                }

                anchors[i] = new PitchPoint(Pitch.ClampX(x), Pitch.ClampY(y));
            }

            return anchors;
        }

        /// <summary>
        /// How far up the pitch a role belongs, in permille of the pitch length (0 = own
        /// goal line, 1000 = attacked goal line). Drives how much of the block's forward
        /// push in possession — and of its drop when defending — each player takes.
        /// </summary>
        public static int Forwardness(PositionRole role, MatchBalance cfg) =>
            FormationGeometry.AnchorX(role, cfg);

        // ---------------------------------------------------------------- integer maths

        /// <summary>
        /// Integer square root (floor). Deterministic on every runtime — no float, no library
        /// call, so the answer is the same bit for bit on .NET, Mono and IL2CPP.
        ///
        /// This is the single hottest routine in the simulation: at 10 Hz the model asks for the
        /// distance between two of twenty-three moving things some millions of times a match, and
        /// the Newton iteration this replaced spent an integer DIVISION on every step. The
        /// digit-by-digit method below uses nothing but shifts, adds and comparisons, which is
        /// what makes ten times the tick rate affordable rather than ruinous.
        /// </summary>
        public static int Sqrt(int value)
        {
            if (value <= 0) return 0;

            uint n = (uint)value;
            uint result = 0;
            uint bit = 1u << 30;
            while (bit > n) bit >>= 2;

            while (bit != 0)
            {
                uint step = result + bit;
                if (n >= step)
                {
                    n -= step;
                    result = (result >> 1) + bit;
                }
                else
                {
                    result >>= 1;
                }

                bit >>= 2;
            }

            return (int)result;
        }

        /// <summary>Euclidean distance between two points, floored to a whole decimetre.</summary>
        public static int Distance(int ax, int ay, int bx, int by)
        {
            long dx = ax - bx;
            long dy = ay - by;
            long sq = dx * dx + dy * dy;
            return Sqrt(sq > int.MaxValue ? int.MaxValue : (int)sq);
        }

        /// <summary>Linear interpolation on integers: a at step 0, b at step span.</summary>
        public static int Lerp(int a, int b, int step, int span) =>
            span <= 0 ? b : a + (b - a) * step / span;

        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
