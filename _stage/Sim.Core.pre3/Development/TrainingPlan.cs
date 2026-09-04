using System.Collections.Generic;

namespace Sim.Core.Development
{
    /// <summary>
    /// A serializable training selection (task 4.3): the squad-wide team focus plus an
    /// optional per-player individual focus. Plain get/set properties + player ids and
    /// enums, mirroring how <see cref="Match.LineupPlan"/> / <see cref="Tactics.TacticPlan"/>
    /// persist their state. The host (client/server) owns persistence; Sim.Core stays
    /// I/O-free. A default plan (<see cref="Balanced"/>) is the neutral choice the AI uses.
    /// </summary>
    public sealed class TrainingPlan
    {
        public TeamTrainingFocus TeamFocus { get; set; } = TeamTrainingFocus.Balanced;

        /// <summary>Player id → individual focus. Absent ids follow the team focus only.</summary>
        public Dictionary<int, IndividualTrainingFocus> IndividualFocuses { get; set; }
            = new Dictionary<int, IndividualTrainingFocus>();

        /// <summary>The default balanced plan (no individual focuses) — what AI clubs train.</summary>
        public static TrainingPlan Balanced() => new TrainingPlan();

        /// <summary>This player's individual focus, or <see cref="IndividualTrainingFocus.None"/> if unset.</summary>
        public IndividualTrainingFocus FocusFor(int playerId) =>
            IndividualFocuses != null && IndividualFocuses.TryGetValue(playerId, out IndividualTrainingFocus f)
                ? f
                : IndividualTrainingFocus.None;
    }
}
