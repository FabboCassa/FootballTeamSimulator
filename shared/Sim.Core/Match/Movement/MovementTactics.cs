using Sim.Core.Config;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// What the coach's instructions MEAN on the pitch (task 13.2). The four axes the
    /// tactics screen exposes already move the result through <see cref="TacticModifiers"/>;
    /// here they move the picture, which is what makes the shape you arranged the shape you
    /// watch. Presentation only — nothing here can touch a score.
    ///
    /// Mentality is how high the block sits and how many bodies join the attack; Pressing is
    /// how far a defender will leave his position to close the ball down; Tempo is how long a
    /// player holds it and how much he favours the forward option; Width is how far the shape
    /// spreads across the pitch.
    /// </summary>
    internal readonly struct MovementTactics
    {
        /// <summary>Permille of pitch length the block shifts forward when the team has the ball.</summary>
        public readonly int AttackShiftPermille;

        /// <summary>Permille it drops when the team has lost it.</summary>
        public readonly int DefendShiftPermille;

        /// <summary>Percent applied to each player's distance from the centre line.</summary>
        public readonly int WidthPercent;

        /// <summary>How far from his position a player will go to press the ball, in units.</summary>
        public readonly int PressReachU;

        /// <summary>How many players run to support the man on the ball.</summary>
        public readonly int Supporters;

        /// <summary>Ticks a player keeps the ball before he looks to release it.</summary>
        public readonly int HoldTicksMin;
        public readonly int HoldTicksMax;

        /// <summary>How strongly a forward option is preferred over a safe one when passing.</summary>
        public readonly int ForwardBias;

        private MovementTactics(
            int attackShift, int defendShift, int widthPercent, int pressReachU,
            int supporters, int holdMin, int holdMax, int forwardBias)
        {
            AttackShiftPermille = attackShift;
            DefendShiftPermille = defendShift;
            WidthPercent = widthPercent;
            PressReachU = pressReachU;
            Supporters = supporters;
            HoldTicksMin = holdMin;
            HoldTicksMax = holdMax;
            ForwardBias = forwardBias;
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

            return new MovementTactics(
                attackShift: Pick(cfg.MentalityAttackShiftPermille, mentality),
                defendShift: Pick(cfg.MentalityDefendShiftPermille, mentality),
                widthPercent: Pick(cfg.WidthSpreadPercent, width),
                pressReachU: U.Units(Pick(cfg.PressReachDm, pressing)),
                supporters: Pick(cfg.MentalitySupporters, mentality),
                holdMin: Pick(cfg.TempoHoldTicksMin, tempo),
                holdMax: Pick(cfg.TempoHoldTicksMax, tempo),
                forwardBias: Pick(cfg.TempoForwardBias, tempo));
        }

        private static int Pick(int[] table, int index) =>
            table != null && index >= 0 && index < table.Length ? table[index] : 0;
    }
}
