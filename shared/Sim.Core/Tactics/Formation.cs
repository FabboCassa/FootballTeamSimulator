using System;
using Sim.Core.Domain;

namespace Sim.Core.Tactics
{
    /// <summary>The shapes a team can line up in (task 3.2).</summary>
    public enum Formation
    {
        F433 = 0,
        F442 = 1,
        F352 = 2,
        F4231 = 3,
        F532 = 4,
        F343 = 5
    }

    /// <summary>
    /// Maps each <see cref="Formation"/> to its 11 role slots. This is structural
    /// data (the shape's geometry), not balance: the engine derives attack/midfield/
    /// defense strength from the role mix via <see cref="Match.TeamRatings"/>, so a
    /// more attacking shape is stronger going forward simply by having more attacking
    /// roles - no separate per-formation rating fudge.
    /// </summary>
    public static class Formations
    {
        private const PositionRole GK = PositionRole.Goalkeeper;
        private const PositionRole CB = PositionRole.CentreBack;
        private const PositionRole FB = PositionRole.FullBack;
        private const PositionRole DM = PositionRole.DefensiveMidfielder;
        private const PositionRole CM = PositionRole.CentralMidfielder;
        private const PositionRole AM = PositionRole.AttackingMidfielder;
        private const PositionRole W = PositionRole.Winger;
        private const PositionRole ST = PositionRole.Striker;

        // Buckets the engine reads (TeamRatings): CB/FB -> defense, DM/CM/AM ->
        // midfield, W/ST -> attack. Wide MIDFIELDERS are encoded CM (stay in the
        // midfield bucket); only genuine wide FORWARDS are W. So each shape has a
        // distinct def/mid/att profile.
        private static readonly PositionRole[] _433 =   // def4 mid3 att3
            { GK, CB, CB, FB, FB, DM, CM, CM, W, W, ST };
        private static readonly PositionRole[] _442 =   // def4 mid4 att2
            { GK, CB, CB, FB, FB, CM, CM, CM, CM, ST, ST };
        private static readonly PositionRole[] _352 =   // def3 mid5 att2
            { GK, CB, CB, CB, CM, CM, CM, CM, CM, ST, ST };
        private static readonly PositionRole[] _4231 =  // def4 mid3 att3 (DM-anchored)
            { GK, CB, CB, FB, FB, DM, DM, AM, W, W, ST };
        private static readonly PositionRole[] _532 =   // def5 mid3 att2
            { GK, CB, CB, CB, FB, FB, CM, CM, CM, ST, ST };
        private static readonly PositionRole[] _343 =   // def3 mid4 att3
            { GK, CB, CB, CB, CM, CM, CM, CM, W, W, ST };

        /// <summary>The 11 role slots of a formation. Do not mutate the returned array.</summary>
        public static PositionRole[] Roles(Formation formation)
        {
            switch (formation)
            {
                case Formation.F433: return _433;
                case Formation.F442: return _442;
                case Formation.F352: return _352;
                case Formation.F4231: return _4231;
                case Formation.F532: return _532;
                case Formation.F343: return _343;
                default: throw new ArgumentOutOfRangeException(nameof(formation));
            }
        }

        /// <summary>All defined formations, for harness sweeps and pickers.</summary>
        public static readonly Formation[] All =
        {
            Formation.F433, Formation.F442, Formation.F352,
            Formation.F4231, Formation.F532, Formation.F343
        };
    }
}
