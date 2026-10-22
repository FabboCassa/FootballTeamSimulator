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

        /// <summary>The all-neutral 4-3-3 plan: yields the engine-identity tactic.</summary>
        public static TacticPlan Neutral() => new TacticPlan();

        /// <summary>Resolves this plan to the value-typed <see cref="Tactic"/> the engine consumes.</summary>
        public Tactic ToTactic() =>
            new Tactic(Formation, new TacticInstructions(
                Mentality, PassingDirectness, Tempo, Width,
                BuildUp, FinalThirdApproach, ShotSelection, DribbleFrequency,
                OnBallLost, OnBallWon,
                LineOfEngagement, DefensiveLine, Pressing, MarkingScheme, PressDirection));

        /// <summary>Flattens a <see cref="Tactic"/> into a serializable plan.</summary>
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
        /// Stable string identity (e.g. "0:1111"), suitable as a familiarity map
        /// key in a host save. Equal plans produce equal keys. It covers the four v11
        /// axes only, so keys already stored in saves stay valid.
        /// </summary>
        public string Key() =>
            $"{(int)Formation}:{(int)Mentality}{(int)Pressing}{(int)Tempo}{(int)Width}";
    }
}
