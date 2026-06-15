using System;

namespace Sim.Core.Tactics
{
    // Each axis has a neutral middle value (index 1) whose self-effect and counter
    // contribution are zero, so a fully neutral tactic is the identity.

    /// <summary>How far the team commits forward (attack/defense trade-off).</summary>
    public enum Mentality { Defensive = 0, Balanced = 1, Attacking = 2 }

    /// <summary>How high/aggressively the team presses (win-ball vs space-behind).</summary>
    public enum Pressing { Low = 0, Medium = 1, High = 2 }

    /// <summary>How directly/quickly the team plays (directness vs control).</summary>
    public enum Tempo { Slow = 0, Normal = 1, Fast = 2 }

    /// <summary>How wide the team spreads (chance creation vs central control).</summary>
    public enum Width { Narrow = 0, Normal = 1, Wide = 2 }

    /// <summary>The four instruction axes that drive self-effects and the counter-matrix.</summary>
    public readonly struct TacticInstructions : IEquatable<TacticInstructions>
    {
        public readonly Mentality Mentality;
        public readonly Pressing Pressing;
        public readonly Tempo Tempo;
        public readonly Width Width;

        public TacticInstructions(Mentality mentality, Pressing pressing, Tempo tempo, Width width)
        {
            Mentality = mentality;
            Pressing = pressing;
            Tempo = tempo;
            Width = width;
        }

        /// <summary>The all-neutral instruction set: zero self-effect, zero counter (identity).</summary>
        public static TacticInstructions Neutral =>
            new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Normal);

        public bool Equals(TacticInstructions other) =>
            Mentality == other.Mentality && Pressing == other.Pressing
            && Tempo == other.Tempo && Width == other.Width;

        public override bool Equals(object? obj) => obj is TacticInstructions o && Equals(o);

        public override int GetHashCode() =>
            ((int)Mentality * 81) + ((int)Pressing * 27) + ((int)Tempo * 9) + (int)Width;
    }

    /// <summary>A complete tactic: a shape plus its instruction set. Value-equatable so it can key familiarity.</summary>
    public readonly struct Tactic : IEquatable<Tactic>
    {
        public readonly Formation Formation;
        public readonly TacticInstructions Instructions;

        public Tactic(Formation formation, TacticInstructions instructions)
        {
            Formation = formation;
            Instructions = instructions;
        }

        public Tactic(Formation formation) : this(formation, TacticInstructions.Neutral) { }

        /// <summary>4-3-3, all-neutral instructions: the engine-identity tactic.</summary>
        public static Tactic Neutral => new Tactic(Formation.F433, TacticInstructions.Neutral);

        public bool Equals(Tactic other) =>
            Formation == other.Formation && Instructions.Equals(other.Instructions);

        public override bool Equals(object? obj) => obj is Tactic o && Equals(o);

        public override int GetHashCode() => ((int)Formation * 1000) + Instructions.GetHashCode();
    }
}
