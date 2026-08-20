using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Generation
{
    /// <summary>One division of a nation's pyramid (task 11.1).</summary>
    public sealed class DivisionProfile
    {
        /// <summary>Display name without the nation, e.g. "Premier Division". Must stay even-sized.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Clubs in the division. MUST be even: the double round-robin schedule
        /// (<see cref="FixtureGenerator"/>) rejects an odd count.
        /// </summary>
        public int ClubCount { get; set; } = 20;
    }

    /// <summary>
    /// The shape of a nation's football (task 11.1) — NOT its clubs. Everything inside is still
    /// generated from the world seed; the profile only says how many tiers there are, how big they
    /// are, how strong the nation is and which naming culture it uses.
    ///
    /// MOD HOOK: profiles are plain data handed to the generator through
    /// <see cref="WorldGenerationOptions.Nations"/>. A player-supplied pack (the "real teams and
    /// players" mods the user plans) replaces this list — nothing about the generator is hard-wired
    /// to the built-in set.
    /// </summary>
    public sealed class NationProfile
    {
        /// <summary>Three-letter code, e.g. "ITA". Unique, stable forever (ids derive from it).</summary>
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public Continent Continent { get; set; }

        /// <summary>
        /// 1..100. Two jobs: it scales club strength in this nation (a top English club is stronger
        /// than a top Icelandic one) and it decides which database presets bother loading it at all.
        /// </summary>
        public int Reputation { get; set; } = 50;

        /// <summary>Key into the culture pool set (see <see cref="CultureDatabase"/>).</summary>
        public string CultureId { get; set; } = string.Empty;

        /// <summary>Tier 1 first. A nation always has at least one.</summary>
        public List<DivisionProfile> Divisions { get; set; } = new List<DivisionProfile>();
    }
}
