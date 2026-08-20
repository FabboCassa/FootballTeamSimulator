using System.Collections.Generic;
using Sim.Core.Career;
using Sim.Core.Domain;
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

        /// <summary>
        /// The rollover as the PLAYER saw it: his own nation's champion and the clubs that went up
        /// and down in his own pyramid. The world rollover moves hundreds of clubs across sixty-odd
        /// nations; the season-end screen wants the handful that concern him, and every id it shows
        /// has to be one his screens can name.
        /// </summary>
        public RolloverResult LastRollover { get; private set; }

        /// <summary>The full world rollover behind <see cref="LastRollover"/> (task 11.1b).</summary>
        public WorldRolloverResult LastWorldRollover { get; private set; }

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

            // Task 11.1b: the whole world rolls over, not just the player's divisions — promotion and
            // relegation run down every simulated pyramid, the background season is finished and
            // rebuilt, and a club promoted out of a background tier into a playable one has its squad
            // topped up on the way in.
            LastWorldRollover = new WorldRollover().EndSeason(_career.World, _career.Season, _career.Seed);
            _career.Season = LastWorldRollover.NewCareerSeason;
            LastRollover = ToUserView(LastWorldRollover);

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

        /// <summary>
        /// Narrows a world rollover down to the player's own nation, in the shape the season-end
        /// screen has always consumed. Clubs from other nations are dropped rather than renamed:
        /// showing "Club 1043002" would be worse than showing nothing.
        /// </summary>
        private RolloverResult ToUserView(WorldRolloverResult world)
        {
            var view = new RolloverResult
            {
                EndedYear = world.EndedYear,
                NewSeason = world.NewCareerSeason
            };

            Nation userNation = _career.World.NationOf(_career.UserClubId);
            if (userNation == null)
                return view;

            if (world.ChampionByNation.TryGetValue(userNation.Code, out int champion))
                view.ChampionClubId = champion;

            view.PromotedClubIds.AddRange(InNation(world.PromotedClubIds, userNation));
            view.RelegatedClubIds.AddRange(InNation(world.RelegatedClubIds, userNation));
            return view;
        }

        private List<int> InNation(List<int> clubIds, Nation nation)
        {
            var mine = new List<int>();
            foreach (int clubId in clubIds)
            {
                Nation owner = _career.World.NationOf(clubId);
                if (owner != null && ReferenceEquals(owner, nation))
                    mine.Add(clubId);
            }

            return mine;
        }
    }
}
