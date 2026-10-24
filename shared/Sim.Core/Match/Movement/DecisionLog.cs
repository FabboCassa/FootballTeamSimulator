using System.Collections.Generic;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The decision sink the harness attaches (R13): keeps every record in tick order and writes
    /// them flat for the HTML replay.
    /// </summary>
    public sealed class DecisionLog : IDecisionSink
    {
        /// <summary>Ints per record in <see cref="Flat"/>: tick, side, slot, ball x, ball y, the choice, the count, three options.</summary>
        public const int Stride = 5 + OptionInts + 1 + 3 * OptionInts;

        private const int OptionInts = 6;

        /// <summary>The R2 chance zone: within 16 m of the attacked goal centre and 10 m of the centre line.</summary>
        public const int ChanceRangeDm = 160;
        public const int ChanceHalfWidthDm = 100;

        private readonly List<DecisionRecord> _records = new List<DecisionRecord>();

        public IReadOnlyList<DecisionRecord> Records => _records;

        public void Record(in DecisionRecord decision) => _records.Add(decision);

        public static bool IsChance(in DecisionRecord r)
        {
            int dx = MovementGeometry.AttackedGoalX(r.Side == 0) - r.BallXDm;
            int dy = r.BallYDm - Pitch.CenterY;
            if (dy > ChanceHalfWidthDm || -dy > ChanceHalfWidthDm) return false;
            return (long)dx * dx + (long)dy * dy <= (long)ChanceRangeDm * ChanceRangeDm;
        }

        /// <summary>Every record as <see cref="Stride"/> ints, in the order they were heard.</summary>
        public int[] Flat()
        {
            var flat = new int[_records.Count * Stride];
            int at = 0;
            foreach (DecisionRecord r in _records)
            {
                flat[at++] = r.Tick;
                flat[at++] = r.Side;
                flat[at++] = r.Slot;
                flat[at++] = r.BallXDm;
                flat[at++] = r.BallYDm;
                at = Write(flat, at, r.Chosen);
                flat[at++] = r.OptionCount;
                for (int i = 0; i < 3; i++) at = Write(flat, at, r.Option(i));
            }

            return flat;
        }

        private static int Write(int[] flat, int at, in DecisionOption o)
        {
            flat[at++] = (int)o.Kind;
            flat[at++] = o.MateSlot;
            flat[at++] = o.XDm;
            flat[at++] = o.YDm;
            flat[at++] = o.CompletionPermille;
            flat[at++] = o.ValuePer10k;
            return at;
        }
    }
}
