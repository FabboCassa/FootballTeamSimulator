using System.Collections.Generic;

namespace Sim.Core.Match.Analysis
{
    public static partial class RealismReference
    {
        /// <summary>
        /// The R1 tracking bands of docs/specs/free-ball-real-movement.md (reference v2), from DFL,
        /// SkillCorner and Metrica open tracking data (doc §10, printed by
        /// tools/tracking_reference.py). Ball in play only; sources, definitions, splits and the
        /// band rule are in doc §10.1-§10.4. Each band is the min-max across the source x league x
        /// tier-pairing splits; the noise-sensitive bands (turns, reversals, speeds above 5.5 m/s,
        /// accelerations) take the optical 25 fps sources (DFL, Metrica) only, doc §10.4.
        /// </summary>
        public static class Tracking
        {
            // doc §10.5 Tracking bands
            public static readonly RealismBand BallFree15Percent =
                new RealismBand("nobody within 1.5 m of the ball % of time", 38.89, 55.92);
            // doc §10.5 Tracking bands
            public static readonly RealismBand BallFree3Percent =
                new RealismBand("nobody within 3 m of the ball % of time", 24.05, 28.82);
            // doc §10.5 Tracking bands
            public static readonly RealismBand PlayersWithin5m =
                new RealismBand("players within 5 m of the ball", 1.47, 1.83);
            // doc §10.5 Tracking bands
            public static readonly RealismBand PlayersWithin10m =
                new RealismBand("players within 10 m of the ball", 3.20, 4.15);
            // doc §10.5 Tracking bands
            public static readonly RealismBand BallAbove1mPercent =
                new RealismBand("ball above 1 m % of time", 17.56, 26.85);
            // doc §10.5 Tracking bands
            public static readonly RealismBand FastFreeBallTurnPercent =
                new RealismBand("fast free ball turning > 20 deg/0.2 s % of steps", 0.06, 0.78);
            // doc §10.5 Tracking bands
            public static readonly RealismBand StrikerOffsidePercent =
                new RealismBand("striker in an offside position % of possession", 6.84, 9.94);
            // doc §10.5 Tracking bands
            public static readonly RealismBand PassFlightP50Seconds =
                new RealismBand("pass flight time p50 s", 0.89, 1.19);
            // doc §10.5 Tracking bands
            public static readonly RealismBand PassFlightP90Seconds =
                new RealismBand("pass flight time p90 s", 1.64, 2.12);
            // doc §10.5 Tracking bands
            public static readonly RealismBand ReceiverToArrivalP50Meters =
                new RealismBand("receiver to arrival point at release p50 m", 2.12, 2.90);
            // doc §10.5 Tracking bands
            public static readonly RealismBand PassIntoSpacePercent =
                new RealismBand("passes into space % of passes", 30.63, 49.56);
            // doc §10.5 Tracking bands
            public static readonly RealismBand LooseBallsPerMatch =
                new RealismBand("loose-ball episodes/match", 75.0, 210.3);
            // doc §10.5 Tracking bands
            public static readonly RealismBand AgreementMedian =
                new RealismBand("direction agreement R (median), tracking v2", 0.811, 0.899);
            // doc §10.5 Tracking bands
            public static readonly RealismBand AgreementAbove09Percent =
                new RealismBand("direction agreement R > 0.9 % of windows, tracking v2", 29.12, 49.08);
            // doc §10.5 Tracking bands
            public static readonly RealismBand ReversalsPerPlayerMinute =
                new RealismBand("heading reversals/player-minute, tracking v2", 0.870, 1.529);
            // doc §10.5 Tracking bands
            public static readonly RealismBand SpeedBelow02Percent =
                new RealismBand("speed < 0.2 m/s % of frames, tracking v2", 0.75, 1.35);
            // doc §10.5 Tracking bands
            public static readonly RealismBand Speed02To2Percent =
                new RealismBand("speed 0.2-2 m/s % of frames, tracking v2", 34.22, 50.51);
            // doc §10.5 Tracking bands
            public static readonly RealismBand Speed2To4Percent =
                new RealismBand("speed 2-4 m/s % of frames, tracking v2", 34.41, 40.97);
            // doc §10.5 Tracking bands
            public static readonly RealismBand Speed4To55Percent =
                new RealismBand("speed 4-5.5 m/s % of frames, tracking v2", 8.89, 16.79);
            // doc §10.5 Tracking bands
            public static readonly RealismBand Speed55To7Percent =
                new RealismBand("speed 5.5-7 m/s % of frames, tracking v2", 2.40, 3.92);
            // doc §10.5 Tracking bands
            public static readonly RealismBand SpeedAbove7Percent =
                new RealismBand("speed > 7 m/s % of frames, tracking v2", 0.54, 0.94);
            // doc §10.5 Tracking bands
            public static readonly RealismBand AccelerationsPerPlayerMinute =
                new RealismBand("accelerations > 2 m/s2/player-minute, tracking v2", 1.249, 2.702);

            /// <summary>Every tracking band above, in doc §10.5 table order.</summary>
            public static readonly IReadOnlyList<RealismBand> All = new[]
            {
                BallFree15Percent, BallFree3Percent, PlayersWithin5m, PlayersWithin10m, BallAbove1mPercent,
                FastFreeBallTurnPercent, StrikerOffsidePercent, PassFlightP50Seconds, PassFlightP90Seconds,
                ReceiverToArrivalP50Meters, PassIntoSpacePercent, LooseBallsPerMatch, AgreementMedian,
                AgreementAbove09Percent, ReversalsPerPlayerMinute, SpeedBelow02Percent, Speed02To2Percent,
                Speed2To4Percent, Speed4To55Percent, Speed55To7Percent, SpeedAbove7Percent,
                AccelerationsPerPlayerMinute,
            };
        }
    }
}
