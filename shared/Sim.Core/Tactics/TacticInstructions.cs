using System;

namespace Sim.Core.Tactics
{
    // Every axis has a neutral middle level whose self-effect and counter contribution are
    // zero, so a fully neutral tactic is the identity. The R16 model groups the axes by phase:
    // in possession, transitions, out of possession.

    // ---------------------------------------------------------------- in possession

    /// <summary>How far the team commits forward (attack/defense trade-off).</summary>
    public enum Mentality { Defensive = 0, Balanced = 1, Attacking = 2 }

    /// <summary>How directly the team passes (short and patient vs long and forward).</summary>
    public enum PassingDirectness { Short = 0, Mixed = 1, Direct = 2 }

    /// <summary>How directly/quickly the team plays (directness vs control).</summary>
    public enum Tempo { Slow = 0, Normal = 1, Fast = 2 }

    /// <summary>How wide the team spreads (chance creation vs central control).</summary>
    public enum Width { Narrow = 0, Normal = 1, Wide = 2 }

    /// <summary>How the team starts its play from the back.</summary>
    public enum BuildUp { PlayOutFromBack = 0, Mixed = 1, GoalKickLong = 2 }

    /// <summary>How the team gets the ball into the box from the final third.</summary>
    public enum FinalThirdApproach { WorkIntoBox = 0, Mixed = 1, CrossEarly = 2 }

    /// <summary>How readily a player takes the shot that is on.</summary>
    public enum ShotSelection { Patient = 0, Normal = 1, ShootOnSight = 2 }

    /// <summary>How often a player takes his man on.</summary>
    public enum DribbleFrequency { Less = 0, Normal = 1, More = 2 }

    // ---------------------------------------------------------------- transitions

    /// <summary>What the side does the moment it loses the ball.</summary>
    public enum OnBallLost { CounterPress = 0, Normal = 1, Regroup = 2 }

    /// <summary>What the side does the moment it wins the ball.</summary>
    public enum OnBallWon { Counter = 0, Normal = 1, HoldShape = 2 }

    // ---------------------------------------------------------------- out of possession

    /// <summary>Where the side starts to engage the ball.</summary>
    public enum LineOfEngagement { Low = 0, Mid = 1, High = 2 }

    /// <summary>
    /// How high the back line holds. The spec lists deep to very high; VeryDeep makes it five
    /// levels so that Normal is the middle one, the identity.
    /// </summary>
    public enum DefensiveLine { VeryDeep = 0, Deep = 1, Normal = 2, High = 3, VeryHigh = 4 }

    /// <summary>How high/aggressively the team presses (win-ball vs space-behind).</summary>
    public enum Pressing { Low = 0, Medium = 1, High = 2 }

    /// <summary>Whether defenders mark space or men.</summary>
    public enum MarkingScheme { Zonal = 0, Mixed = 1, ManOriented = 2 }

    /// <summary>Where the press steers the man on the ball.</summary>
    public enum PressDirection { ForceOutside = 0, None = 1, ForceInside = 2 }

    /// <summary>
    /// The R16 instruction set. The four v11 axes keep their names: Mentality, Tempo, Width
    /// (attacking width) and Pressing (pressing intensity).
    /// </summary>
    public readonly struct TacticInstructions : IEquatable<TacticInstructions>
    {
        // In possession
        public readonly Mentality Mentality;
        public readonly PassingDirectness PassingDirectness;
        public readonly Tempo Tempo;
        public readonly Width Width;
        public readonly BuildUp BuildUp;
        public readonly FinalThirdApproach FinalThirdApproach;
        public readonly ShotSelection ShotSelection;
        public readonly DribbleFrequency DribbleFrequency;

        // Transitions
        public readonly OnBallLost OnBallLost;
        public readonly OnBallWon OnBallWon;

        // Out of possession
        public readonly LineOfEngagement LineOfEngagement;
        public readonly DefensiveLine DefensiveLine;
        public readonly Pressing Pressing;
        public readonly MarkingScheme MarkingScheme;
        public readonly PressDirection PressDirection;

        public TacticInstructions(
            Mentality mentality, PassingDirectness passingDirectness, Tempo tempo, Width width,
            BuildUp buildUp, FinalThirdApproach finalThirdApproach, ShotSelection shotSelection,
            DribbleFrequency dribbleFrequency,
            OnBallLost onBallLost, OnBallWon onBallWon,
            LineOfEngagement lineOfEngagement, DefensiveLine defensiveLine, Pressing pressing,
            MarkingScheme markingScheme, PressDirection pressDirection)
        {
            Mentality = mentality;
            PassingDirectness = passingDirectness;
            Tempo = tempo;
            Width = width;
            BuildUp = buildUp;
            FinalThirdApproach = finalThirdApproach;
            ShotSelection = shotSelection;
            DribbleFrequency = dribbleFrequency;
            OnBallLost = onBallLost;
            OnBallWon = onBallWon;
            LineOfEngagement = lineOfEngagement;
            DefensiveLine = defensiveLine;
            Pressing = pressing;
            MarkingScheme = markingScheme;
            PressDirection = pressDirection;
        }

        /// <summary>
        /// The v11 mapping: each v11 axis sets its R16 namesake and every other axis stays at
        /// identity, so a v11 tactic keeps playing as in v12 whatever the new axes come to do.
        /// </summary>
        public TacticInstructions(Mentality mentality, Pressing pressing, Tempo tempo, Width width)
            : this(mentality, PassingDirectness.Mixed, tempo, width,
                   BuildUp.Mixed, FinalThirdApproach.Mixed, ShotSelection.Normal, DribbleFrequency.Normal,
                   OnBallLost.Normal, OnBallWon.Normal,
                   LineOfEngagement.Mid, DefensiveLine.Normal, pressing, MarkingScheme.Mixed, PressDirection.None)
        {
        }

        /// <summary>Maps a stored v11 instruction set (four axes) onto the R16 model.</summary>
        public static TacticInstructions FromV11(Mentality mentality, Pressing pressing, Tempo tempo, Width width) =>
            new TacticInstructions(mentality, pressing, tempo, width);

        /// <summary>The all-neutral instruction set: zero self-effect, zero counter (identity).</summary>
        public static TacticInstructions Neutral =>
            new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Normal);

        public bool Equals(TacticInstructions other) =>
            Mentality == other.Mentality && PassingDirectness == other.PassingDirectness
            && Tempo == other.Tempo && Width == other.Width
            && BuildUp == other.BuildUp && FinalThirdApproach == other.FinalThirdApproach
            && ShotSelection == other.ShotSelection && DribbleFrequency == other.DribbleFrequency
            && OnBallLost == other.OnBallLost && OnBallWon == other.OnBallWon
            && LineOfEngagement == other.LineOfEngagement && DefensiveLine == other.DefensiveLine
            && Pressing == other.Pressing && MarkingScheme == other.MarkingScheme
            && PressDirection == other.PressDirection;

        public override bool Equals(object? obj) => obj is TacticInstructions o && Equals(o);

        // The expanded axes add only their distance from identity, so a v11 set keeps its v11 hash.
        public override int GetHashCode()
        {
            unchecked
            {
                int expanded = (int)PassingDirectness - 1;
                expanded = expanded * 31 + (int)BuildUp - 1;
                expanded = expanded * 31 + (int)FinalThirdApproach - 1;
                expanded = expanded * 31 + (int)ShotSelection - 1;
                expanded = expanded * 31 + (int)DribbleFrequency - 1;
                expanded = expanded * 31 + (int)OnBallLost - 1;
                expanded = expanded * 31 + (int)OnBallWon - 1;
                expanded = expanded * 31 + (int)LineOfEngagement - 1;
                expanded = expanded * 31 + (int)DefensiveLine - 2;
                expanded = expanded * 31 + (int)MarkingScheme - 1;
                expanded = expanded * 31 + (int)PressDirection - 1;
                return ((int)Mentality * 81) + ((int)Pressing * 27) + ((int)Tempo * 9) + (int)Width
                       + expanded * 243;
            }
        }
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
