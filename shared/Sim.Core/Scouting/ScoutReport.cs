namespace Sim.Core.Scouting
{
    /// <summary>
    /// A scouted value with uncertainty (task 5.4): the [<see cref="Min"/>, <see cref="Max"/>]
    /// range a club currently believes a true value lies in, plus its best single
    /// <see cref="Estimate"/> (the band centre, possibly off the truth at low knowledge).
    ///
    /// Invariant guaranteed by <see cref="ScoutingModel"/>: the player's TRUE value is
    /// always inside [Min, Max] at every knowledge level, and the band collapses onto the
    /// truth as knowledge fills. Pure value type — no behaviour.
    /// </summary>
    public readonly struct ScoutedRange
    {
        public int Min { get; }
        public int Max { get; }
        public int Estimate { get; }

        public ScoutedRange(int min, int max, int estimate)
        {
            Min = min;
            Max = max;
            Estimate = estimate;
        }

        /// <summary>Width of the range (Max − Min); 0 means the value is known exactly.</summary>
        public int Width => Max - Min;
    }

    /// <summary>
    /// A full scouting read of one player at a given knowledge level: a range for each of
    /// the 10 attributes (canonical <see cref="Domain.PlayerAttributes"/> order), plus the
    /// overall and the hidden potential as bands. <see cref="Knowledge"/> is the level the
    /// report was built at (0 = unscouted, wide ranges). Pure data the host renders; the
    /// host decides when to show ranges (other clubs) vs exact values (own players).
    /// </summary>
    public sealed class PlayerScoutReport
    {
        public int PlayerId { get; }
        public int Knowledge { get; }
        public ScoutedRange Overall { get; }
        public ScoutedRange Potential { get; }
        public ScoutedRange[] Attributes { get; }

        public PlayerScoutReport(int playerId, int knowledge, ScoutedRange overall,
                                 ScoutedRange potential, ScoutedRange[] attributes)
        {
            PlayerId = playerId;
            Knowledge = knowledge;
            Overall = overall;
            Potential = potential;
            Attributes = attributes;
        }
    }
}
