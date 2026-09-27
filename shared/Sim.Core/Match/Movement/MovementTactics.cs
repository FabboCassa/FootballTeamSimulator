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
    ///
    /// A touchline shout (watchable-match spec R11) is folded in on top, through a
    /// <see cref="ShoutEffect"/> that is the identity in the same sense when nobody shouts.
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
        /// <summary>Percent applied to the match fatigue the side's legs carry (a shout's cost).</summary>
        public readonly int FatiguePercent;

        /// <summary>Percent of the pressure a man on the ball feels, anywhere on the pitch.</summary>
        public readonly int PressureFeltPercent;

        /// <summary>Percent of the pressure a man on the ball feels in his own half.</summary>
        public readonly int OwnHalfPressureFeltPercent;

        /// <summary>A shout's weight on the ball that can be lost, and on the threat gained (V11).</summary>
        public readonly int RiskPercent;
        public readonly int GainPercent;
        public readonly int DuelLossPercent;
        public readonly int OwnHalfDuelLossPercent;

        /// <summary>A shout's push on the whole block, in decimetres (V11 reads it on top of its shape).</summary>
        public readonly int ShoutLinePushDm;

        private MovementTactics(
            int linePushDm, int frontLineGapDm, int widthPercent, int widePassBiasDm,
            int pressReachU, int pressTriggerDepthDm, int secondPressDepthDm, int pressStandOffU,
            int supporters, int holdMin, int holdMax, int forwardBias, int shotAppetitePercent,
            TacticInstructions instructions,
            ShoutEffect shout, int fatiguePercent)
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
            FatiguePercent = fatiguePercent;
            PressureFeltPercent = shout.PressureFeltPercent;
            OwnHalfPressureFeltPercent = shout.OwnHalfPressureFeltPercent;
            RiskPercent = shout.RiskPercent;
            GainPercent = shout.GainPercent;
            DuelLossPercent = shout.DuelLossPercent;
            ShoutLinePushDm = shout.LinePushDm;
            OwnHalfDuelLossPercent = shout.OwnHalfDuelLossPercent;
        }

        public static MovementTactics From(TacticContext? context, MatchBalance cfg, ShoutEffect shout)
        {
            TacticInstructions i = context.HasValue
                ? context.Value.Tactic.Instructions
                : TacticInstructions.Neutral;

            int mentality = (int)i.Mentality;   // 0 defensive · 1 balanced · 2 attacking
            int pressing = (int)i.Pressing;     // 0 low · 1 medium · 2 high
            int tempo = (int)i.Tempo;           // 0 slow · 1 normal · 2 fast
            int width = (int)i.Width;           // 0 narrow · 1 normal · 2 wide

            // V11 reads the same tables at a spread of its own per axis (R8); V10 at full spread.
            bool v11 = cfg.Brain == MatchBrainVersion.V11;
            int ms = v11 ? cfg.V11MentalitySpreadPercent : 100;
            int ps = v11 ? cfg.V11PressingSpreadPercent : 100;
            int ts = v11 ? cfg.V11TempoSpreadPercent : 100;
            int ws = v11 ? cfg.V11WidthSpreadPercent : 100;

            // Neutral reads 100 * 100 / 100 = 100, and the site that spends it divides by 100
            // again — so a neutral side prices a goal at exactly GoalValueDm, as phase 6 left it.
            int appetite =
                InstructionTable.Percent(cfg.MentalityShotAppetitePercent, mentality, ms)
                * InstructionTable.Percent(cfg.TempoShotAppetitePercent, tempo, ts) / 100
                * shout.ShotAppetitePercent / 100;

            int forwardBias = InstructionTable.Pick(cfg.TempoForwardBias, tempo, ts) + shout.ForwardBias;
            if (shout.ForwardBias != 0 && forwardBias < 0) forwardBias = 0;

            // A shout's hold is a tempo too, and V11 reads it at the same spread.
            int hold = v11 ? 100 + (shout.HoldPercent - 100) * ts / 100 : shout.HoldPercent;

            int supporters = InstructionTable.Pick(cfg.MentalitySupporters, mentality, ms) + shout.Supporters;
            if (shout.Supporters != 0 && supporters < 0) supporters = 0;

            return new MovementTactics(
                linePushDm: InstructionTable.Pick(cfg.MentalityLinePushDm, mentality, ms) + shout.LinePushDm,
                frontLineGapDm: cfg.FrontLineGoalGapDm
                                * InstructionTable.Percent(cfg.MentalityFrontLineGapPercent, mentality, ms) / 100
                                * shout.FrontLineGapPercent / 100,
                widthPercent: InstructionTable.Percent(cfg.WidthSpreadPercent, width, ws),
                widePassBiasDm: InstructionTable.Pick(cfg.WidthWidePassBiasDm, width, ws),
                pressReachU: U.Units(InstructionTable.Pick(cfg.PressReachDm, pressing, ps)) * shout.PressReachPercent / 100,
                pressTriggerDepthDm: InstructionTable.Pick(cfg.PressTriggerDepthDm, pressing, ps) + shout.PressTriggerDepthDm,
                secondPressDepthDm: cfg.SecondPressDepthDm
                                    * InstructionTable.Percent(cfg.PressingSecondPressPercent, pressing, ps) / 100
                                    * shout.SecondPressPercent / 100,
                pressStandOffU: U.Units(cfg.PressDistanceDm)
                                * InstructionTable.Percent(cfg.PressingStandOffPercent, pressing, ps) / 100
                                * shout.PressStandOffPercent / 100,
                supporters: supporters,
                holdMin: cfg.TicksOfMs(InstructionTable.Pick(cfg.TempoHoldMsMin, tempo, ts) * hold / 100),
                holdMax: cfg.TicksOfMs(InstructionTable.Pick(cfg.TempoHoldMsMax, tempo, ts) * hold / 100),
                forwardBias: forwardBias,
                shotAppetitePercent: appetite,
                instructions: i,
                shout: shout,
                fatiguePercent: v11
                    ? shout.FatiguePercent * Percent(cfg.V11PressingFatiguePercent, pressing) / 100
                    : shout.FatiguePercent);
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
