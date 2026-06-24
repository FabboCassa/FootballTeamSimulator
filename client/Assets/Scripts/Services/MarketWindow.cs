using Sim.Core.Config;
using Sim.Core.Domain;

namespace Fts.Services
{
    /// <summary>
    /// When the user may trade (task 5.3). The user is restricted to the same two windows the
    /// AI market uses (decision with the user): a pre-season window and a mid-season window. The
    /// AI fires its instantaneous batch at each window's start (career open / the mid-season
    /// matchday); the user's window is the span of days around it during which he can buy, sell
    /// and list players. Outside both spans the market is closed and trading is disabled.
    ///
    /// Pure function of the season calendar (CurrentDay + the fixture span + the matchday
    /// cadence) — no state, no RNG — so it agrees everywhere and the banner is always truthful.
    /// </summary>
    public readonly struct MarketWindow
    {
        /// <summary>How many matchdays each window stays open (span = this × DaysBetweenRounds days).</summary>
        public const int WindowRounds = 3;

        /// <summary>True when a transfer window is currently open.</summary>
        public readonly bool IsOpen;

        /// <summary>0 = pre-season window, 1 = mid-season window, -1 = closed.</summary>
        public readonly int Index;

        /// <summary>Career days until the open window closes (when <see cref="IsOpen"/>).</summary>
        public readonly int DaysUntilClose;

        /// <summary>Career days until the next window opens (when closed); 0 if none remains this season.</summary>
        public readonly int DaysUntilOpen;

        private MarketWindow(bool isOpen, int index, int daysUntilClose, int daysUntilOpen)
        {
            IsOpen = isOpen;
            Index = index;
            DaysUntilClose = daysUntilClose;
            DaysUntilOpen = daysUntilOpen;
        }

        public static MarketWindow For(Season season, SeasonBalance cfg)
        {
            int day = season.CurrentDay;
            int span = (cfg.DaysBetweenRounds > 0 ? cfg.DaysBetweenRounds : 7) * WindowRounds;

            // Pre-season window: from day 1 for one span.
            int win0Open = 1;
            int win0Close = win0Open + span; // inclusive

            // Mid-season window: opens at the calendar midpoint (aligned with the AI's mid window).
            int maxDay = 0;
            foreach (Fixture f in season.Fixtures)
                if (f.Day > maxDay)
                    maxDay = f.Day;
            int win1Open = maxDay > 0 ? maxDay / 2 : win0Close + span;
            int win1Close = win1Open + span;

            if (day >= win0Open && day <= win0Close)
                return new MarketWindow(true, 0, win0Close - day, 0);
            if (day >= win1Open && day <= win1Close)
                return new MarketWindow(true, 1, win1Close - day, 0);

            // Closed: report the days until the next upcoming window opens (0 if both are past).
            int untilOpen = day < win1Open ? win1Open - day : 0;
            return new MarketWindow(false, -1, 0, untilOpen);
        }
    }
}
