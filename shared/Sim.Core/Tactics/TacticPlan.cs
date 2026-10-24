namespace Sim.Core.Tactics
{
    /// <summary>
    /// A serializable tactic selection (a <see cref="Tactic"/> flattened into
    /// plain get/set properties), suitable for saves and, later, online input
    /// submission — mirroring how <see cref="Match.LineupPlan"/> persists a lineup.
    /// The host (client/server) owns persistence; Sim.Core stays I/O-free.
    /// Every axis defaults to its identity, so a plan stored with only the four v11
    /// axes reads back as their v11 mapping.
    /// </summary>
    public sealed class TacticPlan
    {
        public Formation Formation { get; set; } = Formation.F433;

        // In possession
        public Mentality Mentality { get; set; } = Mentality.Balanced;
        public PassingDirectness PassingDirectness { get; set; } = PassingDirectness.Mixed;
        public Tempo Tempo { get; set; } = Tempo.Normal;
        public Width Width { get; set; } = Width.Normal;
        public BuildUp BuildUp { get; set; } = BuildUp.Mixed;
        public FinalThirdApproach FinalThirdApproach { get; set; } = FinalThirdApproach.Mixed;
        public ShotSelection ShotSelection { get; set; } = ShotSelection.Normal;
        public DribbleFrequency DribbleFrequency { get; set; } = DribbleFrequency.Normal;

        // Transitions
        public OnBallLost OnBallLost { get; set; } = OnBallLost.Normal;
        public OnBallWon OnBallWon { get; set; } = OnBallWon.Normal;

        // Out of possession
        public LineOfEngagement LineOfEngagement { get; set; } = LineOfEngagement.Mid;
        public DefensiveLine DefensiveLine { get; set; } = DefensiveLine.Normal;
        public Pressing Pressing { get; set; } = Pressing.Medium;
        public MarkingScheme MarkingScheme { get; set; } = MarkingScheme.Mixed;
        public PressDirection PressDirection { get; set; } = PressDirection.None;

        /// <summary>
        /// The style the axes above start from (save v19). The axes hold what is played; the
        /// style's overrides are the axes that differ from its preset (<see cref="ToStylePlan"/>).
        /// </summary>
        public PlayingStyle Style { get; set; } = PlayingStyle.Balanced;

        /// <summary>The all-neutral 4-3-3 plan: yields the engine-identity tactic.</summary>
        public static TacticPlan Neutral() => new TacticPlan();

        /// <summary>Resolves this plan to the value-typed <see cref="Tactic"/> the engine consumes.</summary>
        public Tactic ToTactic() =>
            new Tactic(Formation, new TacticInstructions(
                Mentality, PassingDirectness, Tempo, Width,
                BuildUp, FinalThirdApproach, ShotSelection, DribbleFrequency,
                OnBallLost, OnBallWon,
                LineOfEngagement, DefensiveLine, Pressing, MarkingScheme, PressDirection));

        /// <summary>A shape played with a style and its overrides.</summary>
        public static TacticPlan FromStyle(Formation formation, StylePlan style)
        {
            TacticPlan plan = FromTactic(style.ToTactic(formation));
            plan.Style = style.Style;
            return plan;
        }

        /// <summary>This plan's instructions as its <see cref="Style"/> plus the axes that differ from it.</summary>
        public StylePlan ToStylePlan() => StylePlan.FromInstructions(Style, ToTactic().Instructions);

        /// <summary>An independent copy of every field.</summary>
        public TacticPlan Copy() => (TacticPlan)MemberwiseClone();

        /// <summary>Flattens a <see cref="Tactic"/> into a serializable plan (style Balanced).</summary>
        public static TacticPlan FromTactic(Tactic tactic)
        {
            TacticInstructions i = tactic.Instructions;
            return new TacticPlan
            {
                Formation = tactic.Formation,
                Mentality = i.Mentality,
                PassingDirectness = i.PassingDirectness,
                Tempo = i.Tempo,
                Width = i.Width,
                BuildUp = i.BuildUp,
                FinalThirdApproach = i.FinalThirdApproach,
                ShotSelection = i.ShotSelection,
                DribbleFrequency = i.DribbleFrequency,
                OnBallLost = i.OnBallLost,
                OnBallWon = i.OnBallWon,
                LineOfEngagement = i.LineOfEngagement,
                DefensiveLine = i.DefensiveLine,
                Pressing = i.Pressing,
                MarkingScheme = i.MarkingScheme,
                PressDirection = i.PressDirection
            };
        }

        /// <summary>
        /// Stable string identity (e.g. "0:1111"), the familiarity map key in a host save. Equal
        /// tactics produce equal keys; the style label is not part of it. A plan whose expanded
        /// axes are all at identity keeps its v11 key, so familiarity stored before the expansion
        /// stays valid; otherwise the expanded axes are appended (":" + one digit per axis).
        /// </summary>
        public string Key()
        {
            string v11 = $"{(int)Formation}:{(int)Mentality}{(int)Pressing}{(int)Tempo}{(int)Width}";
            TacticInstructions played = ToTactic().Instructions;
            if (played.Equals(TacticInstructions.FromV11(Mentality, Pressing, Tempo, Width)))
                return v11;

            return v11 + ":" + (int)PassingDirectness + (int)BuildUp + (int)FinalThirdApproach
                   + (int)ShotSelection + (int)DribbleFrequency + (int)OnBallLost + (int)OnBallWon
                   + (int)LineOfEngagement + (int)DefensiveLine + (int)MarkingScheme + (int)PressDirection;
        }
    }
}
