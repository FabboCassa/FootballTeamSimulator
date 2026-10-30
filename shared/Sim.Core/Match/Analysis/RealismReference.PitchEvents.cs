namespace Sim.Core.Match.Analysis
{
    public static partial class RealismReference
    {
        /// <summary>
        /// Interim event bands of the `pitch` scenario, per team-match, until the reference v2 event
        /// bands of docs/specs/free-ball-real-movement.md R1 land (issue #173). Replace each with its
        /// #173 constant then.
        /// </summary>
        public static class PitchEvents
        {
            // The pitch scenario's 1.5-5.0 offsides per match, halved to one side; replaced by #173's offsides band.
            public static readonly RealismBand OffsidesPerTeamMatch =
                new RealismBand("offsides/team-match (interim pitch band)", 0.75, 2.5);
        }
    }
}
