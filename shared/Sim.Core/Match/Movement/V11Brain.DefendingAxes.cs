using Sim.Core.Config;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V11Brain
        {
            private DefendingAxesBalance Axes => _sim._cfg.DefendingAxes;

            private TacticInstructions InstructionsOf(int side) => _sim._tactics[side].Instructions;

            private bool JustWonIt(int side) => _phases.PhaseOf(side) == TeamPhase.AttackTransition;

            private bool JustLostIt(int side) => _phases.PhaseOf(side) == TeamPhase.DefenceTransition;

            /// <summary>Tells the base brain, which presses, which side has just lost the ball.</summary>
            private void ShareTransitions()
            {
                for (int side = 0; side < SideCount; side++) _base.SetJustLostIt(side, JustLostIt(side));
            }

            private int ExpandTicks(int side) =>
                DefendingAxes.ExpandTicks(Axes, InstructionsOf(side), JustWonIt(side), _sim._cfg.V11ShapeExpandTicks);

            private int CollapseTicks(int side) =>
                DefendingAxes.CollapseTicks(Axes, InstructionsOf(side), JustLostIt(side), _sim._cfg.V11ShapeCollapseTicks);

            private int JobLeashDm(int side, BaseJob job) =>
                job == BaseJob.Cover
                    ? _sim._cfg.V11JobLeashDm
                    : DefendingAxes.JobLeashDm(Axes, InstructionsOf(side), job == BaseJob.Press, JustLostIt(side),
                        _sim._cfg.V11JobLeashDm);

            /// <summary>
            /// The on-win axis on the hold drawn for a man of this side. Read as the ball changes hands,
            /// before the phases do, a side not yet in possession is the one winning it.
            /// </summary>
            public void OnBallWonHold(int side, ref int min, ref int max)
            {
                bool justWonIt = JustWonIt(side) || !_phases.InPossession(side);
                min = DefendingAxes.HoldTicks(Axes, InstructionsOf(side), justWonIt, min);
                max = DefendingAxes.HoldTicks(Axes, InstructionsOf(side), justWonIt, max);
                if (min < 1) min = 1;
                if (max < min) max = min;
            }

            /// <summary>The press direction: the presser's goal-side spot moved toward the near touchline (or away from it).</summary>
            private int PressTouchlineU(int side, int ballY)
            {
                int toward = DefendingAxes.PressTouchlineDm(Axes, InstructionsOf(side));
                if (toward == 0) return 0;
                return U.Units(ballY < U.CenterYU ? -toward : toward);
            }

            /// <summary>The on-win axis on the man on the ball's gain and risk weights.</summary>
            private void ApplyOnBallWon(V11Scene s, int side)
            {
                bool justWonIt = JustWonIt(side);
                s.GainPercent = DefendingAxes.GainPercent(Axes, s.Instructions, justWonIt, s.GainPercent);
                s.RiskPercent = DefendingAxes.RiskPercent(Axes, s.Instructions, justWonIt, s.RiskPercent);
            }
        }
    }
}
