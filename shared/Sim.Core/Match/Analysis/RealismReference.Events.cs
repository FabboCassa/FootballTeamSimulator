using System.Collections.Generic;

namespace Sim.Core.Match.Analysis
{
    public static partial class RealismReference
    {
        /// <summary>
        /// The R1 event bands of docs/specs/free-ball-real-movement.md (reference v2), per
        /// team-match, from StatsBomb open data (doc §11, printed by tools/event_reference.py).
        /// Sources, definitions (the geometric cross of spec R2 included), splits and the band rule
        /// are in doc §11.1-§11.4. Each band is the min-max across the league x team tier x opponent
        /// tier splits of at least 10 team-matches. Impect was dropped (licence, doc §11.1).
        /// </summary>
        public static class Events
        {
            // doc §11.5 Event bands
            public static readonly RealismBand PassesPerTeamMatch =
                new RealismBand("passes/team-match, event v2", 338.5, 820.5);
            // doc §11.5 Event bands
            public static readonly RealismBand LongPassPercent =
                new RealismBand("long passes > 30 m % of passes", 4.87, 21.23);
            // doc §11.5 Event bands
            public static readonly RealismBand HighPassPercent =
                new RealismBand("high passes % of passes", 4.98, 33.34);
            // doc §11.5 Event bands
            public static readonly RealismBand HighCrossesPerTeamMatch =
                new RealismBand("high crosses/team-match", 1.46, 10.42);
            // doc §11.5 Event bands
            public static readonly RealismBand LowCrossesPerTeamMatch =
                new RealismBand("low crosses and cut-backs/team-match", 1.81, 7.02);
            // doc §11.5 Event bands
            public static readonly RealismBand ThroughBallsPerTeamMatch =
                new RealismBand("through balls/team-match", 0.00, 9.08);
            // doc §11.5 Event bands
            public static readonly RealismBand AerialDuelsPerTeamMatch =
                new RealismBand("aerial duels/team-match", 8.36, 56.30);
            // doc §11.5 Event bands
            public static readonly RealismBand OffsidesPerTeamMatch =
                new RealismBand("offsides/team-match", 0.70, 3.21);

            /// <summary>Every event band above, in doc §11.5 table order.</summary>
            public static readonly IReadOnlyList<RealismBand> All = new[]
            {
                PassesPerTeamMatch, LongPassPercent, HighPassPercent, HighCrossesPerTeamMatch,
                LowCrossesPerTeamMatch, ThroughBallsPerTeamMatch, AerialDuelsPerTeamMatch, OffsidesPerTeamMatch,
            };
        }
    }
}
