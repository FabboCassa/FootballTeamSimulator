using System.Collections.Generic;
using Fts.Services.Persistence;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;
using UnityEngine;

namespace Fts.Services
{
    /// <summary>
    /// Single-player transfer market (task 5.2b): drives the Sim.Core whole-world AI market
    /// (<see cref="TransferMarket.RunWindow"/>) at the two season windows — start of season and
    /// mid-season — seeding every club's transfer budget at the start window. The user's own
    /// club is excluded (his buying/selling is the 5.3 negotiation UI); the rest of the world
    /// trades AI↔AI. Deterministic per (world seed, window index): a window replays identically.
    ///
    /// Opt-in by being called (the match engine never references any of this), so golden
    /// masters/replays are unaffected; AI squads simply evolve across windows, like condition
    /// went live in 4.2. Lives in the Game scope. The single guard is
    /// <see cref="CareerState.TransferWindowsRun"/>, so a window never double-fires within a
    /// season and a reload mid-season resumes correctly.
    /// </summary>
    public sealed class LocalMarketService
    {
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly BalanceConfig _config = new BalanceConfig();
        private readonly TransferMarket _market;
        private readonly BudgetModel _budgets;

        public LocalMarketService(CareerState career, ISaveRepository saveRepository)
        {
            _career = career;
            _saveRepository = saveRepository;
            _market = new TransferMarket(_config);
            _budgets = new BudgetModel(_config);
        }

        /// <summary>
        /// Runs any transfer window now due and saves if anything changed. Called at career open
        /// and at the top of each day advance; idempotent within a season via
        /// <see cref="CareerState.TransferWindowsRun"/>. The start window (index 0) seeds budgets
        /// first; the mid-season window (index 1) fires once the calendar reaches halfway.
        /// </summary>
        public bool RunDueWindows()
        {
            bool ran = false;

            if (_career.TransferWindowsRun <= 0)
            {
                _budgets.SeedBudgets(_career.Leagues); // start-of-season kitties (overwrites; only here)
                RunWindow(0);
                _career.TransferWindowsRun = 1;
                ran = true;
            }

            if (_career.TransferWindowsRun == 1 && _career.Season.CurrentDay >= MidSeasonDay())
            {
                RunWindow(1);
                _career.TransferWindowsRun = 2;
                ran = true;
            }

            if (ran)
                _saveRepository.Save(_career);

            return ran;
        }

        private void RunWindow(int windowIndex)
        {
            List<TransferRecord> records =
                _market.RunWindow(_career.Leagues, _career.Seed, windowIndex, _career.UserClubId);

            _career.TransferNews.AddRange(records);
            Debug.Log($"[Market] Season {_career.Season.Year} window {windowIndex}: {records.Count} AI transfers.");
        }

        /// <summary>The matchday at/after which the mid-season window opens (half the season length).</summary>
        private int MidSeasonDay()
        {
            int maxDay = 0;
            foreach (Fixture f in _career.Season.Fixtures)
                if (f.Day > maxDay)
                    maxDay = f.Day;

            return maxDay > 0 ? maxDay / 2 : 1;
        }
    }
}
