using Sim.Core.Career;
using Sim.Core.Market;

namespace Fts.Services
{
    /// <summary>
    /// End-of-season orchestration (task 2.7): runs the Sim.Core rollover,
    /// swaps in the new season and saves. Keeps the last result in memory for
    /// the season-end screen. Lives in the Game scope.
    /// </summary>
    public sealed class SeasonService
    {
        private readonly CareerState _career;
        private readonly Persistence.ISaveRepository _saveRepository;
        private readonly FinanceProgressor _finance = new FinanceProgressor();

        public RolloverResult LastRollover { get; private set; }

        public SeasonService(CareerState career, Persistence.ISaveRepository saveRepository)
        {
            _career = career;
            _saveRepository = saveRepository;
        }

        public bool IsSeasonComplete
        {
            get
            {
                foreach (Sim.Core.Domain.Fixture fixture in _career.Season.Fixtures)
                {
                    if (!fixture.Played)
                        return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Awards prize money on the COMPLETED season's final standings (task 5.5), before any
        /// rollover. Credits each club per its finishing position, so a good season also fattens next
        /// season's finance-based transfer budget. Split out of <see cref="EndSeason"/> so the coach
        /// career (task 5.6) can run its season-end evaluation between the prize award and the rollover.
        /// Does not save — the caller (CareerService) saves after the full evaluation.
        /// </summary>
        public void AwardPrize()
        {
            if (!IsSeasonComplete)
                return;
            _finance.AwardPrizeMoney(_career.Leagues, _career.Season);
        }

        /// <summary>
        /// One-shot end of season (kept for any non-career-aware path): award prize then roll over.
        /// The live flow runs <see cref="AwardPrize"/> + the coach evaluation + <see cref="Rollover"/>
        /// via <see cref="CareerService"/> instead, so the user can act on offers/sacking in between.
        /// </summary>
        public void EndSeason()
        {
            if (!IsSeasonComplete)
                return;

            AwardPrize();
            Rollover();
        }

        /// <summary>
        /// Runs the Sim.Core rollover (promotion/relegation, aging, new fixtures), resets the
        /// season-local counters and saves. Prize money is awarded separately by <see cref="AwardPrize"/>
        /// (already credited by the time this runs in the career flow).
        /// </summary>
        public void Rollover()
        {
            if (!IsSeasonComplete)
                return;

            LastRollover = new SeasonRollover().EndSeason(_career.Leagues, _career.Season, _career.Seed);
            _career.Season = LastRollover.NewSeason;

            // Clear the season-to-date income/expense display counters for the new season (task 5.5);
            // the cash balance (and the prize just credited) carries over and seeds the new budget.
            FinanceProgressor.ResetSeasonCounters(_career.Leagues);

            // New season: restart the season-local training-week counter so development
            // keeps running every season (the new season's CurrentDay resets to 1; without
            // this the week guard would never fire again). The per-week RNG is decorrelated
            // by year in LocalClock, so seasons don't share development rolls. The minutes
            // window doesn't carry across the season break either.
            _career.LastTrainingWeek = 0;
            _career.StartsSinceTraining.Clear();
            _career.UserMatchesSinceTraining = 0;

            // Support-action cooldowns are stored against the season-local day counter,
            // which resets to 1 now; clear them (and any pending rests) so no cooldown goes
            // stale across the season break (task 4.5). Cooldowns are short, so a clean slate
            // at season start is harmless.
            _career.SupportCooldowns.Clear();
            _career.RestedSinceTraining.Clear();

            // New season: reset the transfer-window counter so the start-of-season window fires
            // again (re-seeding budgets) and clear last season's transfer news feed (task 5.2b).
            // The windows themselves run from LocalClock/GameSessionService on the next advance.
            _career.TransferWindowsRun = 0;
            _career.TransferNews.Clear();

            // The user's own pending market state is season-local too (task 5.3): clear listings
            // and any incoming offers at rollover (budgets reset, the calendar resets). The
            // shortlist is a watch list, so it intentionally persists across the season break.
            _career.TransferList.Clear();
            _career.IncomingOffers.Clear();

            _saveRepository.Save(_career);
        }
    }
}
