namespace Sim.Core.Config
{
    /// <summary>
    /// The R16 transition and out-of-possession axes (issue #91). Every table is indexed by the
    /// axis level; the middle entry (index 2 of the five-level defensive line, index 1 of the
    /// others) is the identity — 0 for an offset, 100 for a percentage — and the engine skips a
    /// middle level altogether, so a neutral side plays the v12 match bit for bit.
    /// Pressing intensity keeps its v11 tables in <see cref="MatchBalance"/>.
    /// </summary>
    public sealed class DefendingAxesBalance
    {
        // --- On losing the ball (counter-press · normal · regroup), for the transition window ---

        /// <summary>Percent on how far the chaser and the second man go to the ball.</summary>
        public int[] OnBallLostPressReachPercent { get; set; } = { 170, 100, 0 };

        /// <summary>Added to how far up the pitch the side presses at all, in decimetres.</summary>
        public int[] OnBallLostTriggerDm { get; set; } = { 1050, 0, 0 };

        /// <summary>Added to how far up the pitch a second man doubles up, in decimetres.</summary>
        public int[] OnBallLostSecondPressDm { get; set; } = { 1050, 0, -1050 };

        /// <summary>Percent on the time the shape takes to drop into its block.</summary>
        public int[] OnBallLostCollapsePercent { get; set; } = { 250, 100, 50 };

        /// <summary>Added to where the back line holds while the side has just lost the ball, in decimetres.</summary>
        public int[] OnBallLostLinePushDm { get; set; } = { 60, 0, -100 };

        /// <summary>Percent on how far from his place in the block a presser may go.</summary>
        public int[] OnBallLostJobLeashPercent { get; set; } = { 160, 100, 100 };

        // --- On winning the ball (counter · normal · hold shape), for the transition window ---

        /// <summary>Percent on the threat a move gains (V11 valuation).</summary>
        public int[] OnBallWonGainPercent { get; set; } = { 260, 100, 25 };

        /// <summary>Percent on the weight of the ball that can be lost (V11 valuation).</summary>
        public int[] OnBallWonRiskPercent { get; set; } = { 60, 100, 250 };

        /// <summary>Percent on how long the man who has just won the ball (or receives it) holds it before he looks to release it.</summary>
        public int[] OnBallWonHoldPercent { get; set; } = { 30, 100, 100 };

        /// <summary>Percent on the time the shape takes to spread into its attacking shape.</summary>
        public int[] OnBallWonExpandPercent { get; set; } = { 40, 100, 250 };

        // --- Line of engagement (low · mid · high) ---

        /// <summary>Added to how far up the pitch the side presses at all, in decimetres.</summary>
        public int[] EngagementTriggerDm { get; set; } = { -200, 0, 250 };

        /// <summary>Added to how far up the pitch a second man doubles up, in decimetres.</summary>
        public int[] EngagementSecondPressDm { get; set; } = { -100, 0, 200 };

        /// <summary>
        /// Out of possession, how far the front line stands from its place, in decimetres; the
        /// lines between it and the back line move in proportion, the back line not at all.
        /// </summary>
        public int[] EngagementFrontLinePushDm { get; set; } = { -80, 0, 100 };

        // --- Defensive line (very deep · deep · normal · high · very high) ---

        /// <summary>Out of possession, how far the back line (and the block spaced off it) moves, in decimetres.</summary>
        public int[] DefensiveLinePushDm { get; set; } = { -90, -45, 0, 50, 100 };

        // --- Marking (zonal · mixed · man-oriented) ---

        /// <summary>Added to how many opponents the side picks up man to man.</summary>
        public int[] MarkingMarkersDelta { get; set; } = { -4, 0, 4 };

        /// <summary>Added to how far from its own goal an opponent is picked up, in decimetres.</summary>
        public int[] MarkingZoneDepthDm { get; set; } = { -200, 0, 300 };

        /// <summary>Percent on how far off his man a marker stands.</summary>
        public int[] MarkingDistancePercent { get; set; } = { 100, 100, 60 };

        /// <summary>
        /// How far, in percent, a marker's aim moves from his own goal toward the ball: at 0 he is
        /// goal-side of his man, higher he stands on the passing line to him.
        /// </summary>
        public int[] MarkingBallSidePercent { get; set; } = { 0, 0, 60 };

        /// <summary>Percent on how far from his place in the block a marker may follow his man.</summary>
        public int[] MarkingJobLeashPercent { get; set; } = { 100, 100, 160 };

        // --- Press direction (force outside · none · force inside) ---

        /// <summary>
        /// How far the presser stands toward the near touchline from his goal-side spot, in
        /// decimetres. Positive takes the line away and closes the man against the touchline (the
        /// trap of a side that forces play outside); negative stands him inside, so the side lets
        /// the flank be and waits for the ball to come back into the middle.
        /// </summary>
        public int[] PressDirectionTouchlineDm { get; set; } = { 40, 0, -40 };

        /// <summary>
        /// Percent on how far a man goes to press a ball out wide (past PressWideThresholdDm from
        /// the centre line) and one in the middle: forcing outside shuts the middle off and springs
        /// the trap on the touchline, forcing inside the reverse.
        /// </summary>
        public int[] PressDirectionWideReachPercent { get; set; } = { 170, 100, 60 };
        public int[] PressDirectionCentralReachPercent { get; set; } = { 75, 100, 140 };
    }
}
