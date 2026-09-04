namespace Sim.Core.Domain
{
    /// <summary>
    /// A club's running finances (task 5.5, ARCHITECTURE.md §4.2). <see cref="Balance"/> is
    /// operating cash in game-currency units; <see cref="Market.FinanceProgressor"/> credits
    /// income (gate receipts, sponsors, prize money) and debits expenses (wages), then floors
    /// the balance at zero — the board covers any operating shortfall, so bankruptcy is
    /// impossible (the 5.5 acceptance). The season-to-date counters are display only and let
    /// the UI show where the money went; they reset each season.
    ///
    /// The transfer kitty itself stays on <see cref="Club.TransferBudget"/> (seeded each season
    /// from these finances by FinanceProgressor, then debited/credited by transfers) so the 5.2
    /// market keeps working unchanged. Additive plain data — defaults to a zero balance, so it
    /// rides existing Club serialization with no save bump (the host seeds a starting balance at
    /// career creation); the match engine never reads it (golden masters unaffected).
    /// </summary>
    public sealed class Finances
    {
        /// <summary>Operating cash on hand. Floored at zero by the progressor (board backs shortfalls).</summary>
        public long Balance { get; set; }

        // --- Season-to-date breakdown (display; reset at season rollover) ---
        public long SeasonGateIncome { get; set; }
        public long SeasonSponsorIncome { get; set; }
        public long SeasonPrizeIncome { get; set; }
        public long SeasonWageExpense { get; set; }

        /// <summary>Total income booked this season.</summary>
        public long SeasonIncome => SeasonGateIncome + SeasonSponsorIncome + SeasonPrizeIncome;

        /// <summary>Total expenses booked this season.</summary>
        public long SeasonExpense => SeasonWageExpense;

        /// <summary>Net result this season (income − expenses), before transfers.</summary>
        public long SeasonNet => SeasonIncome - SeasonExpense;

        /// <summary>Clears the season-to-date counters (call at rollover); keeps the cash balance.</summary>
        public void ResetSeasonCounters()
        {
            SeasonGateIncome = 0;
            SeasonSponsorIncome = 0;
            SeasonPrizeIncome = 0;
            SeasonWageExpense = 0;
        }
    }
}
