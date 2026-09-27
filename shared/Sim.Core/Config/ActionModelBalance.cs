namespace Sim.Core.Config
{
    /// <summary>
    /// Tunables for the action-value models (watchable-match-engine R3): the expected-threat
    /// grid, the xG shot model and simplified pitch control. Read only by
    /// <c>Sim.Core.Match.Movement.Models</c>; nothing in the current engine consumes them, so
    /// changing a value here cannot move an existing match.
    /// </summary>
    public sealed class ActionModelBalance
    {
        // --- Expected threat (Karun Singh) ---

        /// <summary>Grid columns along the pitch, from the side's own goal line to the attacked one.</summary>
        public int XtColumns { get; set; } = 12;

        /// <summary>Grid rows across the pitch, from the Y = 0 touchline.</summary>
        public int XtRows { get; set; } = 8;

        /// <summary>
        /// Karun Singh's 12x8 open-play xT grid, in ten-thousandths of a goal, row-major (row 0 =
        /// the Y = 0 touchline, column 0 = own goal line). The two central rows had one noisy dip
        /// in the own half (column 2 below column 1); it is lifted to its neighbour so the value
        /// never falls as the ball moves toward goal.
        /// </summary>
        public int[] XtGridPer10k { get; set; } =
        {
            64, 78, 84, 98, 113, 125, 147, 175, 212, 276, 349, 379,
            75, 88, 94, 106, 121, 138, 161, 187, 240, 295, 407, 465,
            89, 98, 100, 111, 127, 143, 169, 194, 241, 286, 549, 644,
            94, 108, 108, 113, 126, 148, 169, 200, 239, 351, 1081, 2575,
            94, 108, 108, 113, 126, 148, 169, 200, 239, 351, 1081, 2575,
            89, 98, 100, 111, 127, 143, 169, 194, 241, 286, 549, 644,
            75, 88, 94, 106, 121, 138, 161, 187, 240, 295, 407, 465,
            64, 78, 84, 98, 113, 125, 147, 175, 212, 276, 349, 379,
        };

        // --- Expected goals ---

        /// <summary>Distance between two entries of <see cref="XgByDistancePermille"/>.</summary>
        public int XgDistanceStepDm { get; set; } = 50;

        /// <summary>
        /// xG of a central, unpressed shot by an average finisher, by distance from the goal
        /// centre (0, 5, 10 ... 40 m), in permille. Interpolated between entries; the last one
        /// holds beyond it.
        /// </summary>
        public int[] XgByDistancePermille { get; set; } = { 760, 560, 300, 130, 70, 40, 24, 14, 8 };

        /// <summary>
        /// Least the angle leaves of a chance, in permille. The angle cuts xG by the square of the
        /// cosine of the shooter's bearing off the goal's axis, down to this floor.
        /// </summary>
        public int XgAngleFloorPermille { get; set; } = 40;

        /// <summary>Share of the chance full pressure (1000 permille) takes away, in percent.</summary>
        public int XgPressureCutPercent { get; set; } = 40;

        /// <summary>Weight of Shooting against Technique in the finisher's rating, out of ten.</summary>
        public int XgShootingWeight { get; set; } = 7;

        /// <summary>
        /// Swing of the finisher, in percent: an average one takes the table value, the worst
        /// loses half this and the best gains half.
        /// </summary>
        public int XgSkillSpreadPercent { get; set; } = 60;

        // --- Pitch control (simplified Spearman) ---

        /// <summary>
        /// How long a player carries on at his current velocity before he can turn toward a new
        /// target. Matches the engine's pass reaction, so the model reads the game the way the
        /// engine plays it.
        /// </summary>
        public int PitchControlReactionMs { get; set; } = 300;

        /// <summary>How close to a point a player must get to play the ball there.</summary>
        public int PitchControlReachDm { get; set; } = 10;

        /// <summary>
        /// Arrival-time gap over which control swings from certain loss to certain win: at zero
        /// gap it is even, at half this either way it is decided.
        /// </summary>
        public int PitchControlSpanMs { get; set; } = 900;

        /// <summary>Points sampled along a pass lane, between the passer and the receiver.</summary>
        public int PitchControlLaneSamples { get; set; } = 7;
    }
}
