using System;
using System.Collections.Generic;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// The R18 named styles. Stored values are persisted, so append new styles; never renumber.
    /// Balanced is 0 so a default plan is the neutral identity.
    /// </summary>
    public enum PlayingStyle
    {
        Balanced = 0,
        TikiTaka = 1,
        PositionalPlay = 2,
        Gegenpress = 3,
        VerticalTikiTaka = 4,
        ControlPossession = 5,
        WingPlay = 6,
        RouteOne = 7,
        Direct = 8,
        FluidCounter = 9,
        DirectCounter = 10,
        MidBlock = 11,
        Catenaccio = 12,
        ParkTheBus = 13,
        ManOrientedPress = 14
    }

    /// <summary>
    /// A named preset over the R16 instructions. Formation-independent: the suggested
    /// formations are a hint for the AI and the UI, never part of the preset.
    /// </summary>
    public sealed class StyleDefinition
    {
        public PlayingStyle Style { get; }
        public string Name { get; }
        public TacticInstructions Instructions { get; }
        public IReadOnlyList<Formation> SuggestedFormations { get; }

        public StyleDefinition(PlayingStyle style, string name, TacticInstructions instructions,
            params Formation[] suggestedFormations)
        {
            Style = style;
            Name = name;
            Instructions = instructions;
            SuggestedFormations = Array.AsReadOnly((Formation[])suggestedFormations.Clone());
        }
    }

    /// <summary>
    /// The R18 style catalogue. Retuned in #95 so no style dominates the R20 tournament while each
    /// keeps its #94 fingerprint: zonal marking behind a high line, and a very high line, lost most
    /// games (Positional play, Gegenpress and Vertical tiki-taka mark Mixed, Gegenpress holds High);
    /// Control possession engages from a mid block, Route one keeps a normal line and shot, and Park
    /// the bus sits deep rather than very deep, with normal width and patient shooting.
    /// </summary>
    public static class PlayingStyles
    {
        public static readonly IReadOnlyList<StyleDefinition> All = Array.AsReadOnly(new[]
        {
            new StyleDefinition(PlayingStyle.Balanced, "Balanced", TacticInstructions.Neutral,
                Formation.F433, Formation.F442, Formation.F4231),

            new StyleDefinition(PlayingStyle.TikiTaka, "Tiki-taka", new TacticInstructions(
                    Mentality.Balanced, PassingDirectness.Short, Tempo.Fast, Width.Narrow,
                    BuildUp.PlayOutFromBack, FinalThirdApproach.WorkIntoBox, ShotSelection.Patient,
                    DribbleFrequency.Normal,
                    OnBallLost.CounterPress, OnBallWon.Normal,
                    LineOfEngagement.High, DefensiveLine.High, Pressing.Medium,
                    MarkingScheme.Zonal, PressDirection.ForceInside),
                Formation.F433, Formation.F343),

            new StyleDefinition(PlayingStyle.PositionalPlay, "Positional play", new TacticInstructions(
                    Mentality.Attacking, PassingDirectness.Short, Tempo.Fast, Width.Wide,
                    BuildUp.PlayOutFromBack, FinalThirdApproach.WorkIntoBox, ShotSelection.Patient,
                    DribbleFrequency.Less,
                    OnBallLost.CounterPress, OnBallWon.HoldShape,
                    LineOfEngagement.High, DefensiveLine.High, Pressing.High,
                    MarkingScheme.Mixed, PressDirection.None),
                Formation.F433, Formation.F4231, Formation.F343),

            new StyleDefinition(PlayingStyle.Gegenpress, "Gegenpress", new TacticInstructions(
                    Mentality.Attacking, PassingDirectness.Mixed, Tempo.Fast, Width.Normal,
                    BuildUp.Mixed, FinalThirdApproach.Mixed, ShotSelection.Normal,
                    DribbleFrequency.Normal,
                    OnBallLost.CounterPress, OnBallWon.Counter,
                    LineOfEngagement.High, DefensiveLine.High, Pressing.High,
                    MarkingScheme.Mixed, PressDirection.None),
                Formation.F433, Formation.F4231, Formation.F442),

            new StyleDefinition(PlayingStyle.VerticalTikiTaka, "Vertical tiki-taka", new TacticInstructions(
                    Mentality.Attacking, PassingDirectness.Short, Tempo.Fast, Width.Narrow,
                    BuildUp.PlayOutFromBack, FinalThirdApproach.WorkIntoBox, ShotSelection.Normal,
                    DribbleFrequency.Normal,
                    OnBallLost.CounterPress, OnBallWon.Normal,
                    LineOfEngagement.High, DefensiveLine.High, Pressing.High,
                    MarkingScheme.Mixed, PressDirection.None),
                Formation.F433, Formation.F4231),

            new StyleDefinition(PlayingStyle.ControlPossession, "Control possession", new TacticInstructions(
                    Mentality.Balanced, PassingDirectness.Short, Tempo.Fast, Width.Normal,
                    BuildUp.PlayOutFromBack, FinalThirdApproach.WorkIntoBox, ShotSelection.Patient,
                    DribbleFrequency.Less,
                    OnBallLost.Normal, OnBallWon.HoldShape,
                    LineOfEngagement.Mid, DefensiveLine.Normal, Pressing.Medium,
                    MarkingScheme.Mixed, PressDirection.None),
                Formation.F433, Formation.F4231, Formation.F352),

            new StyleDefinition(PlayingStyle.WingPlay, "Wing play", new TacticInstructions(
                    Mentality.Balanced, PassingDirectness.Direct, Tempo.Slow, Width.Wide,
                    BuildUp.GoalKickLong, FinalThirdApproach.CrossEarly, ShotSelection.Normal,
                    DribbleFrequency.More,
                    OnBallLost.Normal, OnBallWon.Counter,
                    LineOfEngagement.Mid, DefensiveLine.Normal, Pressing.Medium,
                    MarkingScheme.Mixed, PressDirection.None),
                Formation.F442, Formation.F433, Formation.F343),

            new StyleDefinition(PlayingStyle.RouteOne, "Route one", new TacticInstructions(
                    Mentality.Balanced, PassingDirectness.Direct, Tempo.Fast, Width.Normal,
                    BuildUp.GoalKickLong, FinalThirdApproach.Mixed, ShotSelection.Normal,
                    DribbleFrequency.More,
                    OnBallLost.Normal, OnBallWon.Counter,
                    LineOfEngagement.Mid, DefensiveLine.Normal, Pressing.Medium,
                    MarkingScheme.Mixed, PressDirection.None),
                Formation.F442, Formation.F532),

            new StyleDefinition(PlayingStyle.Direct, "Direct", new TacticInstructions(
                    Mentality.Attacking, PassingDirectness.Direct, Tempo.Fast, Width.Wide,
                    BuildUp.Mixed, FinalThirdApproach.Mixed, ShotSelection.ShootOnSight,
                    DribbleFrequency.More,
                    OnBallLost.Normal, OnBallWon.Counter,
                    LineOfEngagement.Mid, DefensiveLine.Normal, Pressing.Medium,
                    MarkingScheme.Mixed, PressDirection.None),
                Formation.F442, Formation.F433, Formation.F4231),

            new StyleDefinition(PlayingStyle.FluidCounter, "Fluid counter", new TacticInstructions(
                    Mentality.Balanced, PassingDirectness.Direct, Tempo.Slow, Width.Normal,
                    BuildUp.Mixed, FinalThirdApproach.Mixed, ShotSelection.Normal,
                    DribbleFrequency.More,
                    OnBallLost.Regroup, OnBallWon.Counter,
                    LineOfEngagement.Mid, DefensiveLine.Deep, Pressing.Medium,
                    MarkingScheme.Zonal, PressDirection.None),
                Formation.F442, Formation.F4231, Formation.F433, Formation.F352),

            new StyleDefinition(PlayingStyle.DirectCounter, "Direct counter", new TacticInstructions(
                    Mentality.Balanced, PassingDirectness.Direct, Tempo.Slow, Width.Wide,
                    BuildUp.GoalKickLong, FinalThirdApproach.Mixed, ShotSelection.ShootOnSight,
                    DribbleFrequency.More,
                    OnBallLost.Normal, OnBallWon.Counter,
                    LineOfEngagement.Low, DefensiveLine.VeryDeep, Pressing.Low,
                    MarkingScheme.Zonal, PressDirection.None),
                Formation.F442, Formation.F532, Formation.F4231),

            new StyleDefinition(PlayingStyle.MidBlock, "Mid block", new TacticInstructions(
                    Mentality.Balanced, PassingDirectness.Short, Tempo.Fast, Width.Narrow,
                    BuildUp.PlayOutFromBack, FinalThirdApproach.Mixed, ShotSelection.Normal,
                    DribbleFrequency.Less,
                    OnBallLost.Regroup, OnBallWon.Normal,
                    LineOfEngagement.Mid, DefensiveLine.Normal, Pressing.Low,
                    MarkingScheme.Zonal, PressDirection.None),
                Formation.F442, Formation.F4231, Formation.F532),

            new StyleDefinition(PlayingStyle.Catenaccio, "Catenaccio", new TacticInstructions(
                    Mentality.Defensive, PassingDirectness.Direct, Tempo.Slow, Width.Narrow,
                    BuildUp.GoalKickLong, FinalThirdApproach.Mixed, ShotSelection.Normal,
                    DribbleFrequency.More,
                    OnBallLost.Regroup, OnBallWon.Normal,
                    LineOfEngagement.Low, DefensiveLine.Deep, Pressing.Low,
                    MarkingScheme.Mixed, PressDirection.None),
                Formation.F532, Formation.F352),

            new StyleDefinition(PlayingStyle.ParkTheBus, "Park the bus", new TacticInstructions(
                    Mentality.Defensive, PassingDirectness.Direct, Tempo.Slow, Width.Normal,
                    BuildUp.GoalKickLong, FinalThirdApproach.Mixed, ShotSelection.Patient,
                    DribbleFrequency.More,
                    OnBallLost.Normal, OnBallWon.Counter,
                    LineOfEngagement.Low, DefensiveLine.Deep, Pressing.Low,
                    MarkingScheme.Zonal, PressDirection.None),
                Formation.F532, Formation.F442),

            new StyleDefinition(PlayingStyle.ManOrientedPress, "Man-oriented press", new TacticInstructions(
                    Mentality.Attacking, PassingDirectness.Direct, Tempo.Normal, Width.Wide,
                    BuildUp.GoalKickLong, FinalThirdApproach.CrossEarly, ShotSelection.Normal,
                    DribbleFrequency.More,
                    OnBallLost.CounterPress, OnBallWon.Counter,
                    LineOfEngagement.High, DefensiveLine.High, Pressing.High,
                    MarkingScheme.ManOriented, PressDirection.None),
                Formation.F343, Formation.F352),
        });

        /// <summary>The catalogue entry of a style.</summary>
        public static StyleDefinition Get(PlayingStyle style)
        {
            foreach (StyleDefinition def in All)
                if (def.Style == style) return def;
            throw new ArgumentOutOfRangeException(nameof(style));
        }
    }
}
