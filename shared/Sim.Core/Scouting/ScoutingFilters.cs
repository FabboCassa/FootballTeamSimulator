using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// The brief we hand a scout before sending him somewhere (task 11.2): what kind of player to
    /// come back with. The scout searches the area HIMSELF against these — the manager never browses
    /// the whole database by hand.
    ///
    /// The filters split in two on purpose, and the split is the anti-cheat of the whole feature:
    ///
    ///   • <see cref="MatchesFacts"/> tests only PUBLIC facts (role, age, nationality, contract
    ///     length, market value, wage). Anyone can read those off a team sheet, and testing them is
    ///     cheap — it is what keeps a continental scan from building a full report per player.
    ///   • <see cref="MatchesEstimates"/> tests ability and potential against what the scout
    ///     BELIEVES (the <see cref="ScoutedRange.Estimate"/> at his current knowledge), never
    ///     against the truth. A vague scout therefore brings back the occasional dud and misses the
    ///     occasional gem, which is exactly the intended texture — and no filter can ever leak a
    ///     hidden value.
    ///
    /// Every field is "off" at its default, so a fresh filter set matches everyone.
    /// </summary>
    public sealed class ScoutingFilters
    {
        /// <summary>Wanted <see cref="PositionRole"/> as an int; -1 (the default) = any role.</summary>
        public int Role { get; set; } = -1;

        /// <summary>Youngest acceptable age; 0 = no floor.</summary>
        public int MinAge { get; set; }

        /// <summary>Oldest acceptable age; 0 = no cap.</summary>
        public int MaxAge { get; set; }

        /// <summary>Minimum ESTIMATED overall; 0 = no floor.</summary>
        public int MinAbility { get; set; }

        /// <summary>Minimum ESTIMATED potential; 0 = no floor.</summary>
        public int MinPotential { get; set; }

        /// <summary>Required nationality code (e.g. "BRA"); empty = any.</summary>
        public string Nationality { get; set; } = string.Empty;

        /// <summary>Only players whose contract runs out within <see cref="ScoutingBalance.ExpiringContractSeasons"/>.</summary>
        public bool ExpiringContractOnly { get; set; }

        /// <summary>Maximum market value in game-currency units; 0 = no cap.</summary>
        public long MaxValue { get; set; }

        /// <summary>Maximum weekly wage in game-currency units; 0 = no cap.</summary>
        public long MaxWage { get; set; }

        /// <summary>True when nothing is set — the scout is told "bring me anyone".</summary>
        public bool IsEmpty =>
            Role < 0 && MinAge <= 0 && MaxAge <= 0 && MinAbility <= 0 && MinPotential <= 0
            && string.IsNullOrEmpty(Nationality) && !ExpiringContractOnly && MaxValue <= 0 && MaxWage <= 0;

        /// <summary>Public facts only — cheap, and never touches a hidden value. See the class summary.</summary>
        public bool MatchesFacts(Player player, ScoutingBalance cfg)
        {
            if (player == null)
                return false;

            if (Role >= 0 && (int)player.Role != Role)
                return false;

            if (MinAge > 0 && player.Age < MinAge)
                return false;

            if (MaxAge > 0 && player.Age > MaxAge)
                return false;

            if (!string.IsNullOrEmpty(Nationality) && player.Nationality != Nationality)
                return false;

            if (ExpiringContractOnly && player.Contract.SeasonsRemaining > cfg.ExpiringContractSeasons)
                return false;

            // A value of 0 means "never priced yet" (the valuation pass has not run), not "free":
            // such a player is not excluded by a price cap, he is simply unknown.
            if (MaxValue > 0 && player.MarketValue > MaxValue)
                return false;

            if (MaxWage > 0 && player.Contract.WeeklyWage > MaxWage)
                return false;

            return true;
        }

        /// <summary>What the scout BELIEVES about ability and potential — never the truth.</summary>
        public bool MatchesEstimates(ScoutedRange overall, ScoutedRange potential)
        {
            if (MinAbility > 0 && overall.Estimate < MinAbility)
                return false;

            if (MinPotential > 0 && potential.Estimate < MinPotential)
                return false;

            return true;
        }

        public ScoutingFilters Clone() => new ScoutingFilters
        {
            Role = Role,
            MinAge = MinAge,
            MaxAge = MaxAge,
            MinAbility = MinAbility,
            MinPotential = MinPotential,
            Nationality = Nationality,
            ExpiringContractOnly = ExpiringContractOnly,
            MaxValue = MaxValue,
            MaxWage = MaxWage
        };
    }
}
