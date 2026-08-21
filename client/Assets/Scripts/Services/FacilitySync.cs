using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;

namespace Fts.Services
{
    /// <summary>
    /// Keeps a club's scouting DEPARTMENT (<see cref="Club.Scouts"/>) in sync with its scouting
    /// FACILITY tier (task 5.5) — the tier is the single source of truth. The number of scouts
    /// (= how many briefs the club can run at once, task 11.2) equals the tier, and every scout's
    /// level is <see cref="FacilityEffects.ScoutLevel"/> (how fast knowledge accrues, task 5.4).
    /// So upgrading the scouting facility both adds a slot in the field and speeds up the work.
    ///
    /// Task 11.2 also gives each scout his three attributes (judging ability, judging potential,
    /// adaptability). They are derived from a stable (club, slot) hash rather than rolled: the
    /// department must survive a reload unchanged, and the project's determinism rule keeps live
    /// RNG out of anything the save depends on. The chief scout is deliberately the better JUDGE
    /// and the later hires the better TRAVELLERS, so "who do I send to South America" is a real
    /// question with a real answer from the first career day — no scout is uniformly better.
    ///
    /// Pure client glue — no Sim.Core change; the scouting model already reads Club.Scouts. Called
    /// at career creation, on a save migration, and whenever the user upgrades the scouting facility.
    /// </summary>
    public static class FacilitySync
    {
        /// <summary>Widest swing either side of the neutral attribute value, so a scout reads roughly 20..80.</summary>
        private const int AttributeSpread = 30;

        public static void ApplyScoutingTier(Club club, int tier, BalanceConfig cfg)
        {
            if (club == null)
                return;

            club.Facilities.Scouting = tier;
            int count = club.Facilities.Scouting;          // clamped to >= 1 by the setter
            int level = FacilityEffects.ScoutLevel(count, cfg);

            var scouts = new List<Scout>(count);
            for (int i = 1; i <= count; i++)
            {
                // The chief scout judges best; every later hire trades some judgement for reach.
                int seniority = i == 1 ? AttributeSpread / 2 : -(AttributeSpread / 3);

                scouts.Add(new Scout
                {
                    Id = i,
                    Name = i == 1 ? "Chief Scout" : "Scout",
                    Level = level,
                    JudgingAbility = Attribute(club.Id, i, 1, seniority),
                    JudgingPotential = Attribute(club.Id, i, 2, seniority),
                    Adaptability = Attribute(club.Id, i, 3, -seniority)
                });
            }

            club.Scouts = scouts;
        }

        /// <summary>
        /// A stable attribute in [1, 100] for one (club, slot, which) triple, centred on the neutral
        /// value and shifted by <paramref name="bias"/>. A splitmix-style integer hash: deterministic,
        /// platform-independent and it touches no RNG stream — the same club always employs the same
        /// scouts, on desktop and on WebGL alike.
        /// </summary>
        private static int Attribute(int clubId, int slot, int which, int bias)
        {
            ulong x = 0x9E3779B97F4A7C15UL;
            x = (x ^ (ulong)(uint)clubId) * 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27;
            x = (x ^ (ulong)(uint)slot) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            x = (x ^ (ulong)(uint)which) * 0xD6E8FEB86659FD93UL;
            x ^= x >> 32;

            int offset = (int)(x % (ulong)(2 * AttributeSpread + 1)) - AttributeSpread;
            return AttributeScale.ClampSkill(Scout.NeutralAttribute + offset + bias);
        }
    }
}
