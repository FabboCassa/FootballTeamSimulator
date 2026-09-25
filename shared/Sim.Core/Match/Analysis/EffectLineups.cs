using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The lineups the R9/R10 effect measurements compare. Every method returns a NEW lineup and
    /// leaves the one it was given untouched; the players themselves are shared, read-only.
    /// </summary>
    public static class EffectLineups
    {
        /// <summary>
        /// The <paramref name="count"/> most tired outfield slots, most tired first. Within a match
        /// the engine tires a man by the clock and his Stamina alone, so at any minute the most tired
        /// are the lowest-Stamina outfielders (ties to the lower slot).
        /// </summary>
        public static int[] MostTired(Lineup lineup, int count)
        {
            var slots = new List<int>();
            for (int i = 0; i < lineup.Slots.Count; i++)
                if (lineup.Slots[i].Role != PositionRole.Goalkeeper) slots.Add(i);

            slots.Sort((a, b) =>
            {
                int byStamina = Stamina(lineup, a).CompareTo(Stamina(lineup, b));
                return byStamina != 0 ? byStamina : a.CompareTo(b);
            });

            if (count < slots.Count) slots.RemoveRange(count, slots.Count - count);
            return slots.ToArray();
        }

        /// <summary>
        /// The same eleven with <paramref name="slots"/> taken by fresh men of identical rating: a copy
        /// of each player whose id is shifted by <paramref name="idOffset"/>, so the engine sees a
        /// substitution.
        /// </summary>
        public static Lineup WithFreshSubs(Lineup lineup, IReadOnlyCollection<int> slots, int idOffset)
        {
            var copy = new Lineup { ClubId = lineup.ClubId };
            for (int i = 0; i < lineup.Slots.Count; i++)
            {
                LineupSlot slot = lineup.Slots[i];
                Player player = Contains(slots, i) ? Fresh(slot.Player, idOffset) : slot.Player;
                copy.Slots.Add(new LineupSlot { Role = slot.Role, Player = player, Position = slot.Position });
            }

            return copy;
        }

        /// <summary>
        /// The same shape and keeper with the outfield players in reverse slot order, which puts
        /// defenders up front and forwards at the back — out of their natural roles.
        /// </summary>
        public static Lineup OutOfRole(Lineup lineup)
        {
            var outfield = new List<int>();
            for (int i = 0; i < lineup.Slots.Count; i++)
                if (lineup.Slots[i].Role != PositionRole.Goalkeeper) outfield.Add(i);

            var copy = new Lineup { ClubId = lineup.ClubId };
            for (int i = 0; i < lineup.Slots.Count; i++)
            {
                LineupSlot slot = lineup.Slots[i];
                int k = outfield.IndexOf(i);
                Player player = k < 0 ? slot.Player : lineup.Slots[outfield[outfield.Count - 1 - k]].Player;
                copy.Slots.Add(new LineupSlot { Role = slot.Role, Player = player, Position = slot.Position });
            }

            return copy;
        }

        /// <summary>Slots whose role is not the player's natural one.</summary>
        public static int OutOfRoleCount(Lineup lineup)
        {
            int n = 0;
            foreach (LineupSlot slot in lineup.Slots)
                if (slot.Role != slot.Player.Role) n++;
            return n;
        }

        private static int Stamina(Lineup lineup, int slot) => lineup.Slots[slot].Player.Attributes.Stamina;

        private static bool Contains(IReadOnlyCollection<int> slots, int slot)
        {
            foreach (int s in slots)
                if (s == slot) return true;
            return false;
        }

        private static Player Fresh(Player p, int idOffset) => new Player
        {
            Id = p.Id + idOffset,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Age = p.Age,
            Role = p.Role,
            Nationality = p.Nationality,
            Attributes = p.Attributes,
            Condition = p.Condition,
            Development = p.Development,
            Contract = p.Contract,
            MarketValue = p.MarketValue
        };
    }
}
