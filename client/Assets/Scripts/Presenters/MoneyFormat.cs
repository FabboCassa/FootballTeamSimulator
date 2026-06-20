namespace Fts.Presenters
{
    /// <summary>
    /// Compact game-currency formatting for the UI (task 5.1). Pure integer math, so the
    /// output is culture-independent (no stray comma/dot decimal separator across locales):
    /// "€206M", "€40.4M", "€625k", "€25k". Used wherever a market value is shown
    /// (Squad roster, player profile, later the market screen).
    /// </summary>
    public static class MoneyFormat
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
