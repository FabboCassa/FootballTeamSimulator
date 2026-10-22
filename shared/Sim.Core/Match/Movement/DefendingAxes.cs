using Sim.Core.Config;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// What the R16 transition and out-of-possession axes mean on the pitch (issue #91): on losing
    /// the ball, on winning it, the line of engagement, the defensive line, the marking scheme and
    /// the press direction. Pressing intensity is the v11 Pressing axis and is read by
    /// <see cref="MovementTactics"/>.
    ///
    /// Every reading returns its input unchanged (or 0) at the middle level, without touching the
    /// table, and nothing here draws: a neutral side plays the v12 match bit for bit.
    /// </summary>
    internal static class DefendingAxes
    {
        private const int Middle = 1;
        private const int LineMiddle = (int)DefensiveLine.Normal;

        /// <summary>Added to the depth up to which the side presses at all, in decimetres.</summary>
        public static int PressTriggerShiftDm(DefendingAxesBalance c, TacticInstructions i, bool justLostIt) =>
            Offset(c.EngagementTriggerDm, (int)i.LineOfEngagement)
            + (justLostIt ? Offset(c.OnBallLostTriggerDm, (int)i.OnBallLost) : 0);

        /// <summary>Added to the depth up to which a second man doubles up, in decimetres.</summary>
        public static int SecondPressShiftDm(DefendingAxesBalance c, TacticInstructions i, bool justLostIt) =>
            Offset(c.EngagementSecondPressDm, (int)i.LineOfEngagement)
            + (justLostIt ? Offset(c.OnBallLostSecondPressDm, (int)i.OnBallLost) : 0);

        /// <summary>How far a man goes to press the ball, in units.</summary>
        public static long PressReach(DefendingAxesBalance c, TacticInstructions i, bool justLostIt, long reach) =>
            justLostIt ? Scale(reach, c.OnBallLostPressReachPercent, (int)i.OnBallLost) : reach;

        /// <summary>How far from his place a man with a press or a mark job may go, in decimetres.</summary>
        public static int JobLeashDm(DefendingAxesBalance c, TacticInstructions i, bool press, bool justLostIt, int leashDm)
        {
            if (press)
                return justLostIt ? (int)Scale(leashDm, c.OnBallLostJobLeashPercent, (int)i.OnBallLost) : leashDm;
            return (int)Scale(leashDm, c.MarkingJobLeashPercent, (int)i.MarkingScheme);
        }

        /// <summary>Added to where the back line holds while the side has just lost the ball, in decimetres.</summary>
        public static int OnBallLostLinePushDm(DefendingAxesBalance c, TacticInstructions i) =>
            Offset(c.OnBallLostLinePushDm, (int)i.OnBallLost);

        /// <summary>The hold before a man who has just won the ball looks to release it, in ticks.</summary>
        public static int HoldTicks(DefendingAxesBalance c, TacticInstructions i, bool justWonIt, int ticks) =>
            justWonIt ? (int)Scale(ticks, c.OnBallWonHoldPercent, (int)i.OnBallWon) : ticks;

        /// <summary>The ticks the shape takes to drop into its block.</summary>
        public static int CollapseTicks(DefendingAxesBalance c, TacticInstructions i, bool justLostIt, int ticks) =>
            justLostIt ? AtLeastOne(Scale(ticks, c.OnBallLostCollapsePercent, (int)i.OnBallLost)) : ticks;

        /// <summary>The ticks the shape takes to spread into its attacking shape.</summary>
        public static int ExpandTicks(DefendingAxesBalance c, TacticInstructions i, bool justWonIt, int ticks) =>
            justWonIt ? AtLeastOne(Scale(ticks, c.OnBallWonExpandPercent, (int)i.OnBallWon)) : ticks;

        /// <summary>Percent on the threat a move gains, for the man on the ball.</summary>
        public static int GainPercent(DefendingAxesBalance c, TacticInstructions i, bool justWonIt, int percent) =>
            justWonIt ? (int)Scale(percent, c.OnBallWonGainPercent, (int)i.OnBallWon) : percent;

        /// <summary>Percent on the weight of the ball that can be lost, for the man on the ball.</summary>
        public static int RiskPercent(DefendingAxesBalance c, TacticInstructions i, bool justWonIt, int percent) =>
            justWonIt ? (int)Scale(percent, c.OnBallWonRiskPercent, (int)i.OnBallWon) : percent;

        /// <summary>Out of possession, how far the back line moves, in decimetres.</summary>
        public static int BackLinePushDm(DefendingAxesBalance c, TacticInstructions i) =>
            (int)i.DefensiveLine == LineMiddle ? 0 : MovementTactics.Pick(c.DefensiveLinePushDm, (int)i.DefensiveLine);

        /// <summary>Out of possession, how far a man of the given line moves: all of it for the front line, none for the back.</summary>
        public static int FrontLinePushDm(DefendingAxesBalance c, TacticInstructions i, int lineRank, int lineCount)
        {
            int push = Offset(c.EngagementFrontLinePushDm, (int)i.LineOfEngagement);
            return push == 0 || lineCount <= 1 ? 0 : push * lineRank / (lineCount - 1);
        }

        /// <summary>How many opponents the side picks up man to man.</summary>
        public static int Markers(DefendingAxesBalance c, TacticInstructions i, int markers) =>
            markers + Offset(c.MarkingMarkersDelta, (int)i.MarkingScheme);

        /// <summary>How far from its own goal an opponent is picked up, in decimetres.</summary>
        public static int MarkZoneDepthDm(DefendingAxesBalance c, TacticInstructions i, int depthDm) =>
            depthDm + Offset(c.MarkingZoneDepthDm, (int)i.MarkingScheme);

        /// <summary>How far off his man a marker stands, in decimetres.</summary>
        public static int MarkDistanceDm(DefendingAxesBalance c, TacticInstructions i, int distanceDm) =>
            (int)Scale(distanceDm, c.MarkingDistancePercent, (int)i.MarkingScheme);

        /// <summary>How far, in percent, a marker's aim moves from his goal toward the ball (0: plain goal-side).</summary>
        public static int MarkBallSidePercent(DefendingAxesBalance c, TacticInstructions i) =>
            Offset(c.MarkingBallSidePercent, (int)i.MarkingScheme);

        /// <summary>How far the presser stands toward the near touchline from his goal-side spot, in decimetres.</summary>
        public static int PressTouchlineDm(DefendingAxesBalance c, TacticInstructions i) =>
            Offset(c.PressDirectionTouchlineDm, (int)i.PressDirection);

        /// <summary>
        /// The press direction on how far a man goes to press: forcing the play outside lets the
        /// central ball be and springs on the man who receives it out wide; forcing it inside the reverse.
        /// </summary>
        public static long PressDirectionReach(DefendingAxesBalance c, TacticInstructions i, bool wide, long reach) =>
            Scale(reach, wide ? c.PressDirectionWideReachPercent : c.PressDirectionCentralReachPercent, (int)i.PressDirection);

        private static int Offset(int[] table, int level) => level == Middle ? 0 : MovementTactics.Pick(table, level);

        private static int AtLeastOne(long ticks) => ticks < 1 ? 1 : (int)ticks;

        private static long Scale(long value, int[] table, int level) =>
            level == Middle ? value : value * MovementTactics.Percent(table, level) / 100;
    }
}
