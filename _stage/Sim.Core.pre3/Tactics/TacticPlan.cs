namespace Sim.Core.Tactics
{
    /// <summary>
    /// A serializable tactic selection (a <see cref="Tactic"/> flattened into
    /// plain get/set properties), suitable for saves and, later, online input
    /// submission — mirroring how <see cref="Match.LineupPlan"/> persists a lineup.
    /// The host (client/server) owns persistence; Sim.Core stays I/O-free.
    /// </summary>
    public sealed class TacticPlan
    {
        public Formation Formation { get; set; } = Formation.F433;
        public Mentality Mentality { get; set; } = Mentality.Balanced;
        public Pressing Pressing { get; set; } = Pressing.Medium;
        public Tempo Tempo { get; set; } = Tempo.Normal;
        public Width Width { get; set; } = Width.Normal;

        /// <summary>The all-neutral 4-3-3 plan: yields the engine-identity tactic.</summary>
        public static TacticPlan Neutral() => new TacticPlan();

        /// <summary>Resolves this plan to the value-typed <see cref="Tactic"/> the engine consumes.</summary>
        public Tactic ToTactic() =>
            new Tactic(Formation, new TacticInstructions(Mentality, Pressing, Tempo, Width));

        /// <summary>Flattens a <see cref="Tactic"/> into a serializable plan.</summary>
        public static TacticPlan FromTactic(Tactic tactic) => new TacticPlan
        {
            Formation = tactic.Formation,
            Mentality = tactic.Instructions.Mentality,
            Pressing = tactic.Instructions.Pressing,
            Tempo = tactic.Instructions.Tempo,
            Width = tactic.Instructions.Width
        };

        /// <summary>
        /// Stable string identity (e.g. "0:1111"), suitable as a familiarity map
        /// key in a host save. Equal plans produce equal keys.
        /// </summary>
        public string Key() =>
            $"{(int)Formation}:{(int)Mentality}{(int)Pressing}{(int)Tempo}{(int)Width}";
    }
}
