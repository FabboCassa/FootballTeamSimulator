namespace Sim.Core.Tactics
{
    /// <summary>
    /// The axes a coach changed on top of a style's preset; null means "as the style says".
    /// Serializable like <see cref="TacticPlan"/>, so the host persists it as-is.
    /// </summary>
    public sealed class StyleOverrides
    {
        // In possession
        public Mentality? Mentality { get; set; }
        public PassingDirectness? PassingDirectness { get; set; }
        public Tempo? Tempo { get; set; }
        public Width? Width { get; set; }
        public BuildUp? BuildUp { get; set; }
        public FinalThirdApproach? FinalThirdApproach { get; set; }
        public ShotSelection? ShotSelection { get; set; }
        public DribbleFrequency? DribbleFrequency { get; set; }

        // Transitions
        public OnBallLost? OnBallLost { get; set; }
        public OnBallWon? OnBallWon { get; set; }

        // Out of possession
        public LineOfEngagement? LineOfEngagement { get; set; }
        public DefensiveLine? DefensiveLine { get; set; }
        public Pressing? Pressing { get; set; }
        public MarkingScheme? MarkingScheme { get; set; }
        public PressDirection? PressDirection { get; set; }

        /// <summary>How many axes are overridden.</summary>
        public int Count()
        {
            int n = 0;
            if (Mentality.HasValue) n++;
            if (PassingDirectness.HasValue) n++;
            if (Tempo.HasValue) n++;
            if (Width.HasValue) n++;
            if (BuildUp.HasValue) n++;
            if (FinalThirdApproach.HasValue) n++;
            if (ShotSelection.HasValue) n++;
            if (DribbleFrequency.HasValue) n++;
            if (OnBallLost.HasValue) n++;
            if (OnBallWon.HasValue) n++;
            if (LineOfEngagement.HasValue) n++;
            if (DefensiveLine.HasValue) n++;
            if (Pressing.HasValue) n++;
            if (MarkingScheme.HasValue) n++;
            if (PressDirection.HasValue) n++;
            return n;
        }

        /// <summary>Applies these overrides on top of a preset.</summary>
        public TacticInstructions ApplyTo(TacticInstructions preset) => new TacticInstructions(
            Mentality ?? preset.Mentality,
            PassingDirectness ?? preset.PassingDirectness,
            Tempo ?? preset.Tempo,
            Width ?? preset.Width,
            BuildUp ?? preset.BuildUp,
            FinalThirdApproach ?? preset.FinalThirdApproach,
            ShotSelection ?? preset.ShotSelection,
            DribbleFrequency ?? preset.DribbleFrequency,
            OnBallLost ?? preset.OnBallLost,
            OnBallWon ?? preset.OnBallWon,
            LineOfEngagement ?? preset.LineOfEngagement,
            DefensiveLine ?? preset.DefensiveLine,
            Pressing ?? preset.Pressing,
            MarkingScheme ?? preset.MarkingScheme,
            PressDirection ?? preset.PressDirection);

        /// <summary>The overrides that turn <paramref name="preset"/> into <paramref name="target"/>: only the axes that differ.</summary>
        public static StyleOverrides Between(TacticInstructions preset, TacticInstructions target) => new StyleOverrides
        {
            Mentality = Diff(preset.Mentality, target.Mentality),
            PassingDirectness = Diff(preset.PassingDirectness, target.PassingDirectness),
            Tempo = Diff(preset.Tempo, target.Tempo),
            Width = Diff(preset.Width, target.Width),
            BuildUp = Diff(preset.BuildUp, target.BuildUp),
            FinalThirdApproach = Diff(preset.FinalThirdApproach, target.FinalThirdApproach),
            ShotSelection = Diff(preset.ShotSelection, target.ShotSelection),
            DribbleFrequency = Diff(preset.DribbleFrequency, target.DribbleFrequency),
            OnBallLost = Diff(preset.OnBallLost, target.OnBallLost),
            OnBallWon = Diff(preset.OnBallWon, target.OnBallWon),
            LineOfEngagement = Diff(preset.LineOfEngagement, target.LineOfEngagement),
            DefensiveLine = Diff(preset.DefensiveLine, target.DefensiveLine),
            Pressing = Diff(preset.Pressing, target.Pressing),
            MarkingScheme = Diff(preset.MarkingScheme, target.MarkingScheme),
            PressDirection = Diff(preset.PressDirection, target.PressDirection)
        };

        private static T? Diff<T>(T preset, T target) where T : struct, System.Enum =>
            preset.Equals(target) ? (T?)null : target;
    }

    /// <summary>
    /// A coach's style choice as stored: the style plus the axes tweaked on top of it.
    /// Formation-independent; <see cref="ToTactic"/> pairs it with any shape.
    /// </summary>
    public sealed class StylePlan
    {
        public PlayingStyle Style { get; set; } = PlayingStyle.Balanced;
        public StyleOverrides Overrides { get; set; } = new StyleOverrides();

        /// <summary>The untweaked preset of a style.</summary>
        public static StylePlan Of(PlayingStyle style) => new StylePlan { Style = style };

        /// <summary>Stores <paramref name="instructions"/> as <paramref name="style"/> plus the axes that differ from it.</summary>
        public static StylePlan FromInstructions(PlayingStyle style, TacticInstructions instructions) => new StylePlan
        {
            Style = style,
            Overrides = StyleOverrides.Between(PlayingStyles.Get(style).Instructions, instructions)
        };

        /// <summary>The instructions the engine plays: the preset with the overrides applied.</summary>
        public TacticInstructions Resolve()
        {
            TacticInstructions preset = PlayingStyles.Get(Style).Instructions;
            // A stored "Overrides": null reads as no overrides.
            return Overrides?.ApplyTo(preset) ?? preset;
        }

        /// <summary>True when the overrides change at least one axis of the preset.</summary>
        public bool IsCustom() => !Resolve().Equals(PlayingStyles.Get(Style).Instructions);

        public Tactic ToTactic(Formation formation) => new Tactic(formation, Resolve());
    }
}
