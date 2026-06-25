using System.Collections.Generic;

namespace Sim.Core.Domain
{
    /// <summary>A football club. Full facilities and finances are added in task 5.5.</summary>
    public sealed class Club
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;

        public Squad Squad { get; set; } = new Squad();
        public Coach Coach { get; set; } = new Coach();

        /// <summary>
        /// The club's scouts (task 5.4). Their levels drive how fast a watched player's
        /// attribute ranges narrow toward the truth (see <see cref="Scouting.ScoutingModel"/>).
        /// Additive — defaults empty, so it rides existing Club serialization with no save
        /// bump; an empty list means the club scouts at ScoutingBalance.BaseClubScoutLevel.
        /// </summary>
        public List<Scout> Scouts { get; set; } = new List<Scout>();

        /// <summary>
        /// Money available for buying players, in game-currency units (task 5.2). A minimal
        /// transfer budget so the AI market has spending limits before full finances land
        /// (task 5.5 will derive/replenish this from wages, gate receipts, prize money and
        /// sponsors). Seeded per season from squad strength + division by
        /// <see cref="Market.BudgetModel"/>; a sale credits it, a purchase debits it.
        /// Additive — defaults 0, so it rides existing Club serialization with no save bump.
        /// </summary>
        public long TransferBudget { get; set; }
    }
}
