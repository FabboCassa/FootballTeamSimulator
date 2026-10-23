using System;
using System.Collections.Generic;

namespace Sim.Core.Tactics
{
    /// <summary>The R16 instruction axes, one per <see cref="TacticInstructions"/> field, in phase order.</summary>
    public enum InstructionAxis
    {
        // In possession
        Mentality, PassingDirectness, Tempo, Width, BuildUp, FinalThirdApproach, ShotSelection, DribbleFrequency,
        // Transitions
        OnBallLost, OnBallWon,
        // Out of possession
        LineOfEngagement, DefensiveLine, Pressing, MarkingScheme, PressDirection
    }

    /// <summary>The R16 phases an instruction belongs to.</summary>
    public enum InstructionPhase { InPossession, Transitions, OutOfPossession }

    /// <summary>
    /// The edits of the Tattiche screen (R21) on a <see cref="TacticPlan"/>. Every edit returns a
    /// new plan: picking a style loads its preset, tweaking an axis keeps the style, so the plan
    /// reads as "custom" while it differs from the preset.
    /// </summary>
    public static class TacticEditor
    {
        public static readonly IReadOnlyList<InstructionAxis> Axes =
            Array.AsReadOnly((InstructionAxis[])Enum.GetValues(typeof(InstructionAxis)));

        public static InstructionPhase PhaseOf(InstructionAxis axis) =>
            axis >= InstructionAxis.LineOfEngagement ? InstructionPhase.OutOfPossession
            : axis >= InstructionAxis.OnBallLost ? InstructionPhase.Transitions
            : InstructionPhase.InPossession;

        /// <summary>The enum whose values are the levels of <paramref name="axis"/>.</summary>
        public static Type LevelType(InstructionAxis axis)
        {
            switch (axis)
            {
                case InstructionAxis.Mentality: return typeof(Mentality);
                case InstructionAxis.PassingDirectness: return typeof(PassingDirectness);
                case InstructionAxis.Tempo: return typeof(Tempo);
                case InstructionAxis.Width: return typeof(Width);
                case InstructionAxis.BuildUp: return typeof(BuildUp);
                case InstructionAxis.FinalThirdApproach: return typeof(FinalThirdApproach);
                case InstructionAxis.ShotSelection: return typeof(ShotSelection);
                case InstructionAxis.DribbleFrequency: return typeof(DribbleFrequency);
                case InstructionAxis.OnBallLost: return typeof(OnBallLost);
                case InstructionAxis.OnBallWon: return typeof(OnBallWon);
                case InstructionAxis.LineOfEngagement: return typeof(LineOfEngagement);
                case InstructionAxis.DefensiveLine: return typeof(DefensiveLine);
                case InstructionAxis.Pressing: return typeof(Pressing);
                case InstructionAxis.MarkingScheme: return typeof(MarkingScheme);
                case InstructionAxis.PressDirection: return typeof(PressDirection);
                default: throw new ArgumentOutOfRangeException(nameof(axis));
            }
        }

        public static int LevelCount(InstructionAxis axis) => axis == InstructionAxis.DefensiveLine ? 5 : 3;

        /// <summary>The level <paramref name="plan"/> plays on <paramref name="axis"/>, 0-based.</summary>
        public static int LevelOf(TacticPlan plan, InstructionAxis axis)
        {
            switch (axis)
            {
                case InstructionAxis.Mentality: return (int)plan.Mentality;
                case InstructionAxis.PassingDirectness: return (int)plan.PassingDirectness;
                case InstructionAxis.Tempo: return (int)plan.Tempo;
                case InstructionAxis.Width: return (int)plan.Width;
                case InstructionAxis.BuildUp: return (int)plan.BuildUp;
                case InstructionAxis.FinalThirdApproach: return (int)plan.FinalThirdApproach;
                case InstructionAxis.ShotSelection: return (int)plan.ShotSelection;
                case InstructionAxis.DribbleFrequency: return (int)plan.DribbleFrequency;
                case InstructionAxis.OnBallLost: return (int)plan.OnBallLost;
                case InstructionAxis.OnBallWon: return (int)plan.OnBallWon;
                case InstructionAxis.LineOfEngagement: return (int)plan.LineOfEngagement;
                case InstructionAxis.DefensiveLine: return (int)plan.DefensiveLine;
                case InstructionAxis.Pressing: return (int)plan.Pressing;
                case InstructionAxis.MarkingScheme: return (int)plan.MarkingScheme;
                case InstructionAxis.PressDirection: return (int)plan.PressDirection;
                default: throw new ArgumentOutOfRangeException(nameof(axis));
            }
        }

        public static TacticPlan WithFormation(TacticPlan plan, Formation formation)
        {
            TacticPlan next = plan.Copy();
            next.Formation = formation;
            return next;
        }

        /// <summary>The plan with <paramref name="style"/>'s untweaked preset; the formation stays.</summary>
        public static TacticPlan WithStyle(TacticPlan plan, PlayingStyle style) =>
            TacticPlan.FromStyle(plan.Formation, StylePlan.Of(style));

        /// <summary>The plan with one axis set to <paramref name="level"/>; the style stays, so it may now read custom.</summary>
        public static TacticPlan WithInstruction(TacticPlan plan, InstructionAxis axis, int level)
        {
            if (level < 0 || level >= LevelCount(axis)) throw new ArgumentOutOfRangeException(nameof(level));

            TacticPlan next = plan.Copy();
            switch (axis)
            {
                case InstructionAxis.Mentality: next.Mentality = (Mentality)level; break;
                case InstructionAxis.PassingDirectness: next.PassingDirectness = (PassingDirectness)level; break;
                case InstructionAxis.Tempo: next.Tempo = (Tempo)level; break;
                case InstructionAxis.Width: next.Width = (Width)level; break;
                case InstructionAxis.BuildUp: next.BuildUp = (BuildUp)level; break;
                case InstructionAxis.FinalThirdApproach: next.FinalThirdApproach = (FinalThirdApproach)level; break;
                case InstructionAxis.ShotSelection: next.ShotSelection = (ShotSelection)level; break;
                case InstructionAxis.DribbleFrequency: next.DribbleFrequency = (DribbleFrequency)level; break;
                case InstructionAxis.OnBallLost: next.OnBallLost = (OnBallLost)level; break;
                case InstructionAxis.OnBallWon: next.OnBallWon = (OnBallWon)level; break;
                case InstructionAxis.LineOfEngagement: next.LineOfEngagement = (LineOfEngagement)level; break;
                case InstructionAxis.DefensiveLine: next.DefensiveLine = (DefensiveLine)level; break;
                case InstructionAxis.Pressing: next.Pressing = (Pressing)level; break;
                case InstructionAxis.MarkingScheme: next.MarkingScheme = (MarkingScheme)level; break;
                case InstructionAxis.PressDirection: next.PressDirection = (PressDirection)level; break;
                default: throw new ArgumentOutOfRangeException(nameof(axis));
            }
            return next;
        }

        /// <summary>True when the plan plays something other than its style's preset.</summary>
        public static bool IsCustom(TacticPlan plan) => plan.ToStylePlan().IsCustom();
    }
}
