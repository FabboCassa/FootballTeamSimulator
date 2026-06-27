namespace Fts.Services
{
    /// <summary>
    /// Compact game-currency formatting (task 6.2). Pure integer math → culture-independent
    /// ("€206M", "€40.4M", "€625k", "€25k"). Lives in the Services layer so lower-layer code
    /// (the market/clock/career services that post Inbox notifications) can format fees without
    /// reaching up into Presenters; <see cref="Fts.Presenters.MoneyFormat"/> delegates here so
    /// there is a single source of truth for the format.
    /// </summary>
    public static class Money
    {
        /// <summary>Short form of a currency amount, e.g. 40_390_000 → "€40.4M", 625_000 → "€625k".</summary>
        public static string Short(long amount)
        {
            if (amount < 0) amount = 0;

            if (amount >= 1_000_000_000)
            {
                long b = amount / 1_000_000_000;
                long tenths = (amount % 1_000_000_000) / 100_000_000;
                return tenths == 0 ? $"€{b}B" : $"€{b}.{tenths}B";
            }

            if (amount >= 1_000_000)
            {
                long m = amount / 1_000_000;
                long tenths = (amount % 1_000_000) / 100_000;
                // No decimal above 100M (tidy) or when it would be ".0".
                return (m >= 100 || tenths == 0) ? $"€{m}M" : $"€{m}.{tenths}M";
            }

            if (amount >= 1_000)
                return $"€{amount / 1_000}k";

            return $"€{amount}";
        }
    }
}
