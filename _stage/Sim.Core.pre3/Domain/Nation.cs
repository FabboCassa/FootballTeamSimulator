using System.Collections.Generic;

namespace Sim.Core.Domain
{
    /// <summary>
    /// A football nation and its pyramid (task 11.1). Clubs and players inside it are still
    /// procedurally generated — the nation only supplies the shape (how many tiers, how big,
    /// how strong) and the naming flavour. A future mod layer replaces the profile that built
    /// it, not this object.
    /// </summary>
    public sealed class Nation
    {
        /// <summary>Stable index in the nation registry; drives deterministic id allocation.</summary>
        public int Id { get; set; }

        /// <summary>Three-letter code, e.g. "ITA". Unique in a world.</summary>
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public Continent Continent { get; set; }

        /// <summary>1..100. Drives club strength in this nation and how likely it is to be loaded at all.</summary>
        public int Reputation { get; set; }

        /// <summary>
        /// The naming culture the nation was generated with (see Generation.CultureDatabase). Kept on
        /// the saved world so players created LATER — a promoted club topping its squad up to full
        /// size — are named in the same flavour as the rest of the nation.
        /// </summary>
        public string CultureId { get; set; } = string.Empty;

        /// <summary>The nation's divisions, tier 1 first.</summary>
        public List<League> Leagues { get; set; } = new List<League>();

        public League? FindTier(int tier)
        {
            foreach (League league in Leagues)
            {
                if (league.Division == tier)
                    return league;
            }

            return null;
        }
    }
}
