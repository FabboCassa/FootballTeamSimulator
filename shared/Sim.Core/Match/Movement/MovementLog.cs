using System.Collections.Generic;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The movement sink the harness attaches (free-ball spec R7): watches the men it is given and
    /// keeps every record they produce, in tick order.
    /// </summary>
    public sealed class MovementLog : IMovementSink
    {
        private readonly HashSet<int> _watched = new HashSet<int>();
        private readonly List<MovementRecord> _records = new List<MovementRecord>();

        /// <summary>Watches man <paramref name="slot"/> of <paramref name="side"/> (0 home, 1 away).</summary>
        public MovementLog Watch(int side, int slot)
        {
            _watched.Add(Key(side, slot));
            return this;
        }

        public IReadOnlyList<MovementRecord> Records => _records;

        public bool Watches(int side, int slot) => _watched.Contains(Key(side, slot));

        public void Record(in MovementRecord move) => _records.Add(move);

        private static int Key(int side, int slot) => side * 64 + slot;
    }
}
