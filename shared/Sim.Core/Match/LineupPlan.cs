using System;
using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Match
{
    /// <summary>A slot in a planned lineup: the role to play and the chosen player's id.</summary>
    public sealed class LineupPlanSlot
    {
        public PositionRole Role { get; set; }
        public int PlayerId { get; set; }
    }

    /// <summary>
    /// A serializable lineup selection (player ids, not references), suitable
    /// for saves and, later, for online input submission. Resolved against a
    /// club's current squad when the match is simulated.
    /// </summary>
    public sealed class LineupPlan
    {
        public int ClubId { get; set; }
        public List<LineupPlanSlot> Slots { get; set; } = new List<LineupPlanSlot>();

        /// <summary>Builds a plan from a resolved lineup (e.g. LineupSelector output).</summary>
        public static LineupPlan From(Lineup lineup)
        {
            var plan = new LineupPlan { ClubId = lineup.ClubId };
            foreach (LineupSlot slot in lineup.Slots)
                plan.Slots.Add(new LineupPlanSlot { Role = slot.Role, PlayerId = slot.Player.Id });
            return plan;
        }

        /// <summary>Resolves the plan against the club's squad. Throws if the plan is not valid.</summary>
        public Lineup Materialize(Club club)
        {
            var lineup = new Lineup { ClubId = club.Id };

            foreach (LineupPlanSlot slot in Slots)
            {
                Player? player = FindPlayer(club, slot.PlayerId);
                if (player == null)
                    throw new InvalidOperationException(
                        $"Lineup plan for club {ClubId}: player {slot.PlayerId} is not in club {club.Id}'s squad.");

                lineup.Slots.Add(new LineupSlot { Role = slot.Role, Player = player });
            }

            lineup.Validate();
            return lineup;
        }

        /// <summary>Non-throwing variant; returns false (lineup null) when the plan is invalid.</summary>
        public bool TryMaterialize(Club club, out Lineup? lineup)
        {
            try
            {
                lineup = Materialize(club);
                return true;
            }
            catch (InvalidOperationException)
            {
                lineup = null;
                return false;
            }
        }

        private static Player? FindPlayer(Club club, int playerId)
        {
            foreach (Player player in club.Squad.Players)
            {
                if (player.Id == playerId)
                    return player;
            }

            return null;
        }
    }
}
