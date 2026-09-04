using Sim.Core.Random;

namespace Sim.Core.Market
{
    /// <summary>
    /// AI club transfer personality (task 5.2, ARCHITECTURE.md §4.7: "AI clubs with
    /// personalities — seller/hoarder/youth-focused"). Shapes how a club prices what it
    /// sells and how aggressively it buys, on top of the plain valuation.
    /// </summary>
    public enum ClubPersonality
    {
        /// <summary>No bias — prices and buys at the model's neutral.</summary>
        Balanced = 0,
        /// <summary>Happy to cash in — lower asking prices, normal buying.</summary>
        Seller = 1,
        /// <summary>Clings to its players — inflated asking, reluctant buyer.</summary>
        Hoarder = 2,
        /// <summary>Chases prospects and protects its own youth.</summary>
        YouthFocused = 3,
        /// <summary>Ambitious — pays over the odds to land targets.</summary>
        BigSpender = 4
    }

    /// <summary>
    /// The permille knobs a <see cref="ClubPersonality"/> applies (1000 = neutral). PURE data:
    /// the structural effect lives in <see cref="NegotiationModel"/> / <see cref="TransferMarket"/>,
    /// these are just the magnitudes per personality.
    /// </summary>
    public readonly struct PersonalityProfile
    {
        /// <summary>Multiplies the asking price (Hoarder overprices, Seller discounts).</summary>
        public readonly int AskPermille;
        /// <summary>Multiplies the buyer's max price (BigSpender pays up).</summary>
        public readonly int BuyAggressionPermille;
        /// <summary>Bias toward youth: &gt;1000 favours young targets when scoring buys and resists selling young players.</summary>
        public readonly int YouthBiasPermille;

        public PersonalityProfile(int askPermille, int buyAggressionPermille, int youthBiasPermille)
        {
            AskPermille = askPermille;
            BuyAggressionPermille = buyAggressionPermille;
            YouthBiasPermille = youthBiasPermille;
        }
    }

    /// <summary>
    /// Maps a <see cref="ClubPersonality"/> to its <see cref="PersonalityProfile"/> and derives a
    /// club's personality deterministically from its id and the world seed — so it is stable
    /// across sessions and needs NO persistence (no save-version bump), like the per-club RNG
    /// streams the condition/development progressors use.
    /// </summary>
    public static class ClubPersonalities
    {
        /// <summary>Odd constant decorrelating per-club personality draws from other per-club streams.</summary>
        private const ulong PersonalitySeedMix = 0x2545F4914F6CDD1DUL;

        // Indexed by (int)ClubPersonality. Neutral = Balanced. Magnitudes are first-pass tunables.
        private static readonly PersonalityProfile[] Profiles =
        {
            /* Balanced     */ new PersonalityProfile(askPermille: 1000, buyAggressionPermille: 1000, youthBiasPermille: 1000),
            /* Seller       */ new PersonalityProfile(askPermille:  920, buyAggressionPermille: 1000, youthBiasPermille: 1000),
            /* Hoarder      */ new PersonalityProfile(askPermille: 1160, buyAggressionPermille:  950, youthBiasPermille: 1000),
            /* YouthFocused */ new PersonalityProfile(askPermille: 1000, buyAggressionPermille: 1020, youthBiasPermille: 1250),
            /* BigSpender   */ new PersonalityProfile(askPermille: 1050, buyAggressionPermille: 1150, youthBiasPermille: 1000)
        };

        public static PersonalityProfile Profile(ClubPersonality personality) => Profiles[(int)personality];

        /// <summary>
        /// The (stable, save-free) personality of a club in a given world. Deterministic per
        /// (worldSeed, clubId) so it never changes between sessions and is identical on every platform.
        /// </summary>
        public static ClubPersonality For(int clubId, ulong worldSeed)
        {
            var rng = new Pcg32(worldSeed ^ ((ulong)(uint)clubId * PersonalitySeedMix), 9001UL);
            return (ClubPersonality)rng.NextInt(0, Profiles.Length);
        }
    }
}
