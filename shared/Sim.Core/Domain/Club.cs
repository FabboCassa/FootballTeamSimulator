using System.Collections.Generic;

namespace Sim.Core.Domain
{
    /// <summary>A football club. Full facilities and finances are added in task 5.5.</summary>
    public sealed class Club
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;

        /// <summary>
        /// The club's generated strength baseline on the attribute scale (task 11.1): roughly the
        /// overall of its average first-team player before per-player noise. Set once at world
        /// generation and kept as data so a BACKGROUND league can be resolved
        /// (<see cref="Career.QuickResultResolver"/>) without aggregating a lineup, and so a
        /// DATA-ONLY club — which has no real squad — still has a meaningful size for scouting.
        /// Additive — defaults 0, in which case callers fall back to rating the squad.
        /// </summary>
        public int Strength { get; set; }

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

        /// <summary>
        /// The club's upgradeable facilities (task 5.5): stadium, training ground, scouting
        /// department, academy. Each starts at tier 1 (the baseline that reproduces the
        /// pre-5.5 neutral effects), and <see cref="Market.FacilityEffects"/> turns a tier
        /// into its effect. Additive — defaults to all-tier-1, no save bump.
        /// </summary>
        public Facilities Facilities { get; set; } = new Facilities();

        /// <summary>
        /// The club's running finances (task 5.5): operating cash, season income/expense
        /// breakdown. <see cref="Market.FinanceProgressor"/> evolves it; the transfer kitty
        /// (<see cref="TransferBudget"/>) is seeded from it each season. Additive — defaults
        /// to a zero balance (the host seeds a starting balance at career creation), no save bump.
        /// </summary>
        public Finances Finances { get; set; } = new Finances();
    }
}
