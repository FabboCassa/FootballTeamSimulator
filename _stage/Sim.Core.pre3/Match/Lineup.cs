using System;
using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Match
{
    /// <summary>A slot in the starting eleven: the role it is played as, and who fills it.</summary>
    public sealed class LineupSlot
    {
        public PositionRole Role { get; set; }
        public Player Player { get; set; } = null!;

        /// <summary>
        /// Optional custom on-pitch spot for free positioning (task 6.10). Null = the player
        /// sits on his role's canonical anchor (no positional tilt). When set, the engine's
        /// opt-in positioning applies a small shape tilt from the offset vs the role anchor;
        /// the Role itself is the already-resolved zone role (see <see cref="Tactics.ZoneRole"/>).
        /// Runtime-only (the serializable form lives on <see cref="LineupPlanSlot"/>).
        /// </summary>
        public SlotPosition? Position { get; set; }
    }

    /// <summary>A starting eleven. Players may be deployed out of their natural role (at a rating cost).</summary>
    public sealed class Lineup
    {
        public const int Size = 11;

        public int ClubId { get; set; }
        public List<LineupSlot> Slots { get; set; } = new List<LineupSlot>();

        public void Validate()
        {
            if (Slots.Count != Size)
                throw new InvalidOperationException($"Lineup needs {Size} players, has {Slots.Count}.");

            int goalkeepers = 0;
            var ids = new HashSet<int>();
            foreach (LineupSlot slot in Slots)
            {
                if (slot.Player == null)
                    throw new InvalidOperationException("Lineup slot without a player.");
                if (!ids.Add(slot.Player.Id))
                    throw new InvalidOperationException($"Player {slot.Player.Id} appears twice.");
                if (slot.Role == PositionRole.Goalkeeper) goalkeepers++;
            }

            if (goalkeepers != 1)
                throw new InvalidOperationException($"Lineup needs exactly 1 goalkeeper slot, has {goalkeepers}.");
        }
    }
}
