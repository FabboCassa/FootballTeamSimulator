using System;
using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// The commentary panel's cursor (spec R15): which lines of a <see cref="CommentaryBuilder"/>
    /// commentary playback has reached, and which of a fixed pool of rows each shown line sits in.
    /// The panel keeps only the newest <see cref="Capacity"/> lines; a new line takes the row of the
    /// oldest one, so rows are recycled and never created during playback.
    ///
    /// Pure and allocation-free per <see cref="AdvanceTo"/>, so a renderer can drive it every pump.
    /// </summary>
    public sealed class CommentaryFeed
    {
        private readonly IReadOnlyList<CommentaryLine> _lines;

        /// <param name="lines">The commentary, in match order (as <see cref="CommentaryBuilder.Build"/> returns it).</param>
        /// <param name="capacity">How many rows the panel pools.</param>
        public CommentaryFeed(IReadOnlyList<CommentaryLine> lines, int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "the panel needs at least one row");
            _lines = lines ?? throw new ArgumentNullException(nameof(lines));
            Capacity = capacity;
        }

        public int Capacity { get; }

        /// <summary>Every line of the commentary, shown or not.</summary>
        public int Count => _lines.Count;

        /// <summary>How many lines playback has reached: lines 0 to Shown-1.</summary>
        public int Shown { get; private set; }

        /// <summary>The oldest line that still holds a row.</summary>
        public int FirstKept => Math.Max(0, Shown - Capacity);

        /// <summary>
        /// Shows every line whose frame playback has reached. Returns the first line the panel has to
        /// draw now; the range runs to <see cref="Shown"/> and is empty when nothing came in. Lines
        /// that arrived but are already pushed out of the pool by newer ones are not drawn at all.
        /// </summary>
        public int AdvanceTo(int frame)
        {
            int before = Shown;
            while (Shown < _lines.Count && _lines[Shown].Frame <= frame)
                Shown++;
            return Math.Max(before, FirstKept);
        }

        /// <summary>The pooled row a line is drawn in.</summary>
        public int RowOf(int line) => line % Capacity;
    }
}
