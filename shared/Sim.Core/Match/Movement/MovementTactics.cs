using Sim.Core.Config;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// What the coach's instructions MEAN on the pitch (task 13.2; the levers themselves are
    /// engine phase 8). The four axes the tactics screen exposes already move the result through
    /// <see cref="TacticModifiers"/>; here they move the picture, which is what makes the shape
    /// you arranged the shape you watch. Presentation only — nothing here reaches into the
    /// result model, and since phase 6 it does not have to: the picture IS the result.
    ///
    /// Mentality is how high the back line holds — and therefore how high the whole block
    /// sits, since every line is spaced off it — how near the goal the front line is allowed to
    /// stand, how many bodies join the attack, and how readily a man has a go; Pressing is how
    /// far a defender will leave his position to close the ball down, how deep in the other
    /// side's half a second man joins him, and how tight he gets once he is there; Tempo is how
    /// long a player holds it, how much he favours the forward option and — the other half of
    /// the shot appetite — how willing he is to take the shot that is on instead of the extra
    /// pass; Width is how far the shape spreads across the pitch and what the man on the
    /// touchline is worth to the player on the ball.
    ///
    /// THE ONE INVARIANT OF PHASE 8: read at the neutral instruction (Balanced / Medium /
    /// Normal / Normal — which is also what a null <see cref="TacticContext"/> means), every
    /// field below is the identity. The additive ones come back 0, the percentages come back
    /// 100, and every site that consumes them does so as <c>x * 100 / 100</c> or <c>x + 0</c>,
    /// which is exact in integers. A neutral match is therefore the phase 7 match, bit for bit —
    /// the golden master, the twenty `pitch` readings and every balance figure are untouched by
    /// this phase, and <c>InstructionsTests.Neutral_Instructions_AreTheIdentity</c> holds the
    /// claim to it.
    /// </summary>
    internal readonly struct MovementTactics
    {
        /// <summary>Where mentality puts the back line, in decimetres from its own goal.</summary>
        public readonly int LinePushDm;

        /// <summary>
        /// The ceiling on the most advanced line, in decimetres from the goal being attacked.
        /// Mentality's push moves where the line HOLDS; this moves how far up it is allowed to
        /// be pushed, which is what the push runs into as soon as the side attacks.
        /// </summary>
        public readonly int FrontLineGapDm;

        /// <summary>Percent applied to each player's distance from the centre line.</summary>
        public readonly int WidthPercent;

        /// <summary>
        /// What a team-mate in a wide channel is worth to the man on the ball, in decimetres of
        /// forward progress. Negative under a narrow instruction: he would rather go through.
        /// </summary>
        public readonly int WidePassBiasDm;

        /// <summary>How far from his position a player will go to press the ball, in units.</summary>
        public readonly int PressReachU;

        /// <summary>
        /// How far up the pitch the side will chase the man on the ball at all, in decimetres
        /// from its own goal (engine phase 3). Past it a low block simply keeps its shape.
        /// </summary>
        public readonly int PressTriggerDepthDm;

        /// <summary>
        /// How deep in its own end the side sends a SECOND man at the ball, in decimetres from
        /// its own goal. A high press doubles up in the other side's half; a low block does it
        /// on the edge of its own box.
        /// </summary>
        public readonly int SecondPressDepthDm;

        /// <summary>How close the presser stands to the ball, in units.</summary>
        public readonly int PressStandOffU;

        /// <summary>How many players run to support the man on the ball.</summary>
        public readonly int Supporters;

        /// <summary>Ticks a player keeps the ball before he looks to release it.</summary>
        public readonly int HoldTicksMin;
        public readonly int HoldTicksMax;

        /// <summary>How strongly a forward option is preferred over a safe one when passing.</summary>
        public readonly int ForwardBias;

        /// <summary>
        /// What a goal is worth to the man deciding, as a percentage of
        /// <see cref="MatchBalance.GoalValueDm"/>: Mentality and Tempo multiplied together.
        /// </summary>
        public readonly int ShotAppetitePercent;

        /// <summary>The instructions these were read from, for the V11 brain, which reads them its own way.</summary>
        public readonly TacticInstructions Instructions;

        private MovementTactics(
            int linePushDm, int frontLineGapDm, int widthPercent, int widePassBiasDm,
            int pressReachU, int pressTriggerDepthDm, int secondPressDepthDm, int pressStandOffU,
            int supporters, int holdMin, int holdMax, int forwardBias, int shotAppetitePercent,
            TacticInstructions instructions)
        {
            LinePushDm = linePushDm;
            FrontLineGapDm = frontLineGapDm;
            WidthPercent = widthPercent;
            WidePassBiasDm = widePassBiasDm;
            PressReachU = pressReachU;
            PressTriggerDepthDm = pressTriggerDepthDm;
            SecondPressDepthDm = secondPressDepthDm;
            PressStandOffU = pressStandOffU;
            Supporters = supporters;
            HoldTicksMin = holdMin;
            HoldTicksMax = holdMax;
            ForwardBias = forwardBias;
            ShotAppetitePercent = shotAppetitePercent;
            Instructions = instructions;
        }

        public static MovementTactics From(TacticContext? context, MatchBalance cfg)
        {
            TacticInstructions i = context.HasValue
                ? context.Value.Tactic.Instructions
                : TacticInstructions.Neutral;

            int mentality = (int)i.Mentality;   // 0 defensive · 1 balanced · 2 attacking
            int pressing = (int)i.Pressing;     // 0 low · 1 medium · 2 high
            int tempo = (int)i.Tempo;           // 0 slow · 1 normal · 2 fast
            int width = (int)i.Width;           // 0 narrow · 1 normal · 2 wide

            // Neutral reads 100 * 100 / 100 = 100, and the site that spends it divides by 100
            // again — so a neutral side prices a goal at exactly GoalValueDm, as phase 6 left it.
            int appetite =
                Percent(cfg.MentalityShotAppetitePercent, mentality)
                * Percent(cfg.TempoShotAppetitePercent, tempo) / 100;

            return new MovementTactics(
                linePushDm: Pick(cfg.MentalityLinePushDm, mentality),
                frontLineGapDm: cfg.FrontLineGoalGapDm
                                * Percent(cfg.MentalityFrontLineGapPercent, mentality) / 100,
                widthPercent: Percent(cfg.WidthSpreadPercent, width),
                widePassBiasDm: Pick(cfg.WidthWidePassBiasDm, width),
                pressReachU: U.Units(Pick(cfg.PressReachDm, pressing)),
                pressTriggerDepthDm: Pick(cfg.PressTriggerDepthDm, pressing),
                secondPressDepthDm: cfg.SecondPressDepthDm
                                    * Percent(cfg.PressingSecondPressPercent, pressing) / 100,
                pressStandOffU: U.Units(cfg.PressDistanceDm)
                                * Percent(cfg.PressingStandOffPercent, pressing) / 100,
                supporters: Pick(cfg.MentalitySupporters, mentality),
                holdMin: cfg.TicksOfMs(Pick(cfg.TempoHoldMsMin, tempo)),
                holdMax: cfg.TicksOfMs(Pick(cfg.TempoHoldMsMax, tempo)),
                forwardBias: Pick(cfg.TempoForwardBias, tempo),
                shotAppetitePercent: appetite,
                instructions: i);
        }

        internal static int Pick(int[] table, int index) =>
            table != null && index >= 0 && index < table.Length ? table[index] : 0;

        /// <summary>
        /// A table whose missing value is the IDENTITY rather than zero. A percentage table that
        /// fell back to 0 would switch the thing it scales off altogether — a config someone has
        /// hand-edited down to two entries would stop the press instead of leaving it neutral.
        /// </summary>
        internal static int Percent(int[] table, int index) =>
            table != null && index >= 0 && index < table.Length ? table[index] : 100;
    }
}
