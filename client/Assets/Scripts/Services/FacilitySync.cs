using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;

namespace Fts.Services
{
    /// <summary>
    /// Keeps a club's scouting DEPARTMENT (<see cref="Club.Scouts"/>) in sync with its scouting
    /// FACILITY tier (task 5.5) — the tier is the single source of truth. The number of scouts
    /// (= the watch capacity on the Scouting screen) equals the tier, and every scout's level is
    /// <see cref="FacilityEffects.ScoutLevel"/> (how fast watched players' ranges narrow, task 5.4).
    /// So upgrading the scouting facility both adds a watch slot and speeds up knowledge.
    ///
    /// Pure client glue — no Sim.Core change; the scouting model already reads Club.Scouts. Called
    /// at career creation, on a save migration, and whenever the user upgrades the scouting facility.
    /// </summary>
    public static class FacilitySync
    {
        public static void ApplyScoutingTier(Club club, int tier, BalanceConfig cfg)
        {
            if (club == null)
                return;

            club.Facilities.Scouting = tier;
            int count = club.Facilities.Scouting;          // clamped to >= 1 by the setter
            int level = FacilityEffects.ScoutLevel(count, cfg);

            var scouts = new List<Scout>(count);
            for (int i = 1; i <= count; i++)
                scouts.Add(new Scout { Id = i, Name = i == 1 ? "Chief Scout" : "Scout", Level = level });

            club.Scouts = scouts;
        }
    }
}
