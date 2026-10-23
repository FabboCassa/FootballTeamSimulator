using System;
using Sim.Core.Domain;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// The shapes a team can line up in (task 3.2; real-match spec R17). Values are saved, so
    /// they never change: new shapes are appended with explicit values.
    /// </summary>
    public enum Formation
    {
        F433 = 0,
        F442 = 1,
        F352 = 2,
        F4231 = 3,
        F532 = 4,
        F343 = 5,
        F442Diamond = 6,
        F41212 = 7,
        F4411 = 8,
        F433Holding = 9,
        F4141 = 10,
        F451 = 11,
        F4321 = 12,
        F4222 = 13,
        F4312 = 14,
        F4132 = 15,
        F424 = 16,
        F460 = 17,
        F3421 = 18,
        F3412 = 19,
        F3142 = 20,
        F3511 = 21,
        F541 = 22,
        F523 = 23,
        F5212 = 24,
        F3241 = 25,
        F5221 = 26
    }

    /// <summary>
    /// Maps each <see cref="Formation"/> to its 11 role slots. This is structural
    /// data (the shape's geometry), not balance: the engine derives attack/midfield/
    /// defense strength from the role mix via <see cref="Match.TeamRatings"/>, so a
    /// more attacking shape is stronger going forward simply by having more attacking
    /// roles - no separate per-formation rating fudge.
    ///
    /// The catalogue is R1's (docs/research/football-reference.md §4): each shape also
    /// carries its name and the first in- and out-of-possession shape the doc lists, as
    /// outfield lines from the back.
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
        // distinct def/mid/att profile. R1's codes map as: RB/LB -> FB; wing-backs ->
        // CM in front of a back three (as the original 3-5-2) and FB in a back five;
        // RM/LM -> CM; RAM/LAM and RW/LW -> W (as the original 4-2-3-1); SS and the
        // false 9 -> AM. Two shapes whose R1 in-possession shape pushes RM/LM up front
        // carry them as W: the 4-1-3-2 (2-1-3-4; as CM it would be the diamond) and the
        // 4-5-1 (4-3-3).
        // Indexed by the enum value.
        private static readonly Entry[] _table =
        {
            new Entry(Formation.F433, "4-3-3", "2-3-5", "4-5-1",
                GK, CB, CB, FB, FB, DM, CM, CM, W, W, ST),
            new Entry(Formation.F442, "4-4-2", "2-4-4", "4-4-2",
                GK, CB, CB, FB, FB, CM, CM, CM, CM, ST, ST),
            new Entry(Formation.F352, "3-5-2", "3-1-4-2", "5-3-2",
                GK, CB, CB, CB, CM, CM, CM, CM, CM, ST, ST),
            new Entry(Formation.F4231, "4-2-3-1", "2-3-5", "4-4-2",
                GK, CB, CB, FB, FB, DM, DM, AM, W, W, ST),
            new Entry(Formation.F532, "5-3-2", "3-5-2", "5-3-2",
                GK, CB, CB, CB, FB, FB, CM, CM, CM, ST, ST),
            new Entry(Formation.F343, "3-4-3", "3-4-3", "5-4-1",
                GK, CB, CB, CB, CM, CM, CM, CM, W, W, ST),
            new Entry(Formation.F442Diamond, "4-4-2 diamond", "2-3-3-2", "4-3-1-2",
                GK, CB, CB, FB, FB, DM, CM, CM, AM, ST, ST),
            // R1 keeps it as the narrow variant of the diamond, on the same slots.
            new Entry(Formation.F41212, "4-1-2-1-2", "2-3-3-2", "4-3-1-2",
                GK, CB, CB, FB, FB, DM, CM, CM, AM, ST, ST),
            new Entry(Formation.F4411, "4-4-1-1", "4-2-3-1", "4-4-1-1",
                GK, CB, CB, FB, FB, CM, CM, CM, CM, AM, ST),
            new Entry(Formation.F433Holding, "4-3-3 holding", "2-4-4", "4-4-1-1",
                GK, CB, CB, FB, FB, DM, DM, CM, W, W, ST),
            new Entry(Formation.F4141, "4-1-4-1", "2-3-5", "4-1-4-1",
                GK, CB, CB, FB, FB, DM, CM, CM, CM, CM, ST),
            new Entry(Formation.F451, "4-5-1", "4-3-3", "4-5-1",
                GK, CB, CB, FB, FB, CM, CM, CM, W, W, ST),
            new Entry(Formation.F4321, "4-3-2-1", "2-3-2-3", "4-3-2-1",
                GK, CB, CB, FB, FB, CM, CM, CM, AM, AM, ST),
            new Entry(Formation.F4222, "4-2-2-2", "2-2-2-4", "4-4-2",
                GK, CB, CB, FB, FB, DM, DM, AM, AM, ST, ST),
            new Entry(Formation.F4312, "4-3-1-2", "2-3-3-2", "4-3-1-2",
                GK, CB, CB, FB, FB, CM, CM, CM, AM, ST, ST),
            new Entry(Formation.F4132, "4-1-3-2", "2-1-3-4", "4-1-3-2",
                GK, CB, CB, FB, FB, DM, AM, W, W, ST, ST),
            new Entry(Formation.F424, "4-2-4", "2-2-6", "4-4-2",
                GK, CB, CB, FB, FB, CM, CM, W, W, ST, ST),
            new Entry(Formation.F460, "4-6-0", "2-3-5", "4-5-1",
                GK, CB, CB, FB, FB, DM, CM, CM, AM, W, W),
            new Entry(Formation.F3421, "3-4-2-1", "3-2-5", "5-4-1",
                GK, CB, CB, CB, CM, CM, CM, CM, AM, AM, ST),
            new Entry(Formation.F3412, "3-4-1-2", "3-4-1-2", "5-3-2",
                GK, CB, CB, CB, CM, CM, CM, CM, AM, ST, ST),
            new Entry(Formation.F3142, "3-1-4-2", "3-1-4-2", "5-3-2",
                GK, CB, CB, CB, DM, CM, CM, CM, CM, ST, ST),
            new Entry(Formation.F3511, "3-5-1-1", "3-4-2-1", "5-3-1-1",
                GK, CB, CB, CB, CM, CM, CM, CM, CM, AM, ST),
            new Entry(Formation.F541, "5-4-1", "3-6-1", "5-4-1",
                GK, CB, CB, CB, FB, FB, CM, CM, CM, CM, ST),
            new Entry(Formation.F523, "5-2-3", "3-4-3", "5-2-3",
                GK, CB, CB, CB, FB, FB, CM, CM, W, W, ST),
            new Entry(Formation.F5212, "5-2-1-2", "3-4-1-2", "5-3-2",
                GK, CB, CB, CB, FB, FB, CM, CM, AM, ST, ST),
            new Entry(Formation.F3241, "3-2-4-1", "3-2-4-1", "4-4-2",
                GK, CB, CB, CB, DM, DM, AM, AM, W, W, ST),
            new Entry(Formation.F5221, "5-2-2-1", "3-4-2-1", "5-2-2-1",
                GK, CB, CB, CB, FB, FB, CM, CM, AM, AM, ST),
        };

        /// <summary>The 11 role slots of a formation. Do not mutate the returned array.</summary>
        public static PositionRole[] Roles(Formation formation) => Find(formation).Roles;

        /// <summary>The formation's R1 name, e.g. "4-4-2 diamond".</summary>
        public static string Name(Formation formation) => Find(formation).Name;

        /// <summary>The R1 in-possession shape, outfield lines from the back (e.g. "2-3-5").</summary>
        public static string InPossession(Formation formation) => Find(formation).InPossession;

        /// <summary>The R1 out-of-possession shape, outfield lines from the back (e.g. "4-5-1").</summary>
        public static string OutOfPossession(Formation formation) => Find(formation).OutOfPossession;

        /// <summary>
        /// The six original shapes: the ones the ranked lineup picker and the tactic presets offer;
        /// the Tactics screen (#99) offers the whole <see cref="Catalogue"/>.
        /// </summary>
        public static readonly Formation[] All =
        {
            Formation.F433, Formation.F442, Formation.F352,
            Formation.F4231, Formation.F532, Formation.F343
        };

        /// <summary>Every formation of the R1 catalogue, in its table order, for harness sweeps and the Tactics screen.</summary>
        public static readonly Formation[] Catalogue =
        {
            Formation.F442, Formation.F442Diamond, Formation.F41212, Formation.F4411,
            Formation.F433, Formation.F433Holding, Formation.F4231, Formation.F4141,
            Formation.F451, Formation.F4321, Formation.F4222, Formation.F4312,
            Formation.F4132, Formation.F424, Formation.F460,
            Formation.F352, Formation.F343, Formation.F3421, Formation.F3412,
            Formation.F3142, Formation.F3511,
            Formation.F532, Formation.F541, Formation.F523, Formation.F5212,
            Formation.F3241, Formation.F5221
        };

        private static Entry Find(Formation formation)
        {
            int i = (int)formation;
            if (i < 0 || i >= _table.Length) throw new ArgumentOutOfRangeException(nameof(formation));

            Entry entry = _table[i];
            if (entry.Formation != formation) throw new InvalidOperationException($"{formation}: table out of order");
            return entry;
        }

        private sealed class Entry
        {
            public readonly Formation Formation;
            public readonly string Name;
            public readonly string InPossession;
            public readonly string OutOfPossession;
            public readonly PositionRole[] Roles;

            public Entry(Formation formation, string name, string inPossession, string outOfPossession,
                params PositionRole[] roles)
            {
                Formation = formation;
                Name = name;
                InPossession = inPossession;
                OutOfPossession = outOfPossession;
                Roles = roles;
            }
        }
    }
}
