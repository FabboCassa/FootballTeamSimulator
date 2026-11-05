namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The central-zone counts of one match per side (see <see cref="CentralZoneAnalyzer"/>): on-ball
    /// spells touching the opponent's penalty area, spells reaching the central zone, and those of
    /// them with no defender within 3 m. Raw counts, so matches pool by adding them.
    /// </summary>
    public sealed class CentralZoneMetrics
    {
        private readonly int[] _box = new int[2];
        private readonly int[] _zone = new int[2];
        private readonly int[] _unpressed = new int[2];

        /// <summary>On-ball spells of this side with a frame inside the opponent's penalty area.</summary>
        public int BoxSpells(bool home) => _box[home ? 0 : 1];

        /// <summary>On-ball spells of this side that reached the central zone.</summary>
        public int ZoneSpells(bool home) => _zone[home ? 0 : 1];

        /// <summary>Of <see cref="ZoneSpells"/>, those with no outfield defender within 3 m on arrival.</summary>
        public int ZoneUnpressed(bool home) => _unpressed[home ? 0 : 1];

        public void Add(bool home, bool box, bool zone, bool unpressed)
        {
            int side = home ? 0 : 1;
            if (box) _box[side]++;
            if (!zone) return;
            _zone[side]++;
            if (unpressed) _unpressed[side]++;
        }
    }
}
