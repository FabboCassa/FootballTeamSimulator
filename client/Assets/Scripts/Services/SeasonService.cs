using Sim.Core.Career;

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

        public void EndSeason()
        {
            if (!IsSeasonComplete)
                return;

            LastRollover = new SeasonRollover().EndSeason(_career.Leagues, _career.Season, _career.Seed);
            _career.Season = LastRollover.NewSeason;

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

            _saveRepository.Save(_career);
        }
    }
}
