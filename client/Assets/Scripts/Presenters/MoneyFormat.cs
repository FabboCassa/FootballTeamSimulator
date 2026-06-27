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
        /// <summary>
        /// Short form of a currency amount, e.g. 40_390_000 → "€40.4M". Delegates to the
        /// Services-layer <see cref="Fts.Services.Money"/> so the format has a single source of
        /// truth (task 6.2) — this facade keeps the existing Presenters call sites unchanged.
        /// </summary>
        public static string Short(long amount) => Fts.Services.Money.Short(amount);
    }
}
