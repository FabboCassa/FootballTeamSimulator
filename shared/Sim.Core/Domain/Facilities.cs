namespace Sim.Core.Domain
{
    /// <summary>
    /// A club's four facility tiers (task 5.5, ARCHITECTURE.md §4.2). Each is a tier in
    /// [1, FinanceBalance.MaxFacilityTier]; tier 1 is the baseline every club starts at, so
    /// a fresh world's effects equal the pre-5.5 neutral defaults (the training tier maps to
    /// the neutral development facility level, scouting to the base scout level), and only
    /// upgrades change anything. <see cref="Market.FacilityEffects"/> turns a tier into its
    /// effect and into the next upgrade's cost.
    ///
    /// Additive plain data — defaults to all-tier-1, so it rides existing Club serialization
    /// with no save bump, and the match engine never reads it (golden masters unaffected).
    /// </summary>
    public sealed class Facilities
    {
        private int _stadium = 1;
        private int _training = 1;
        private int _scouting = 1;
        private int _academy = 1;

        /// <summary>Stadium tier → seating capacity → gate receipts.</summary>
        public int Stadium { get => _stadium; set => _stadium = Clamp(value); }

        /// <summary>Training-ground tier → development speed (the 5.5 ✅).</summary>
        public int Training { get => _training; set => _training = Clamp(value); }

        /// <summary>Scouting-department tier → effective scout level (task 5.4).</summary>
        public int Scouting { get => _scouting; set => _scouting = Clamp(value); }

        /// <summary>Academy tier → youth quality (youth intake itself is a later system).</summary>
        public int Academy { get => _academy; set => _academy = Clamp(value); }

        /// <summary>The current tier of one facility type.</summary>
        public int TierOf(FacilityType type)
        {
            switch (type)
            {
                case FacilityType.Stadium: return _stadium;
                case FacilityType.Training: return _training;
                case FacilityType.Scouting: return _scouting;
                default: return _academy;
            }
        }

        /// <summary>Sets the tier of one facility type (clamped low end to 1).</summary>
        public void SetTier(FacilityType type, int tier)
        {
            switch (type)
            {
                case FacilityType.Stadium: Stadium = tier; break;
                case FacilityType.Training: Training = tier; break;
                case FacilityType.Scouting: Scouting = tier; break;
                default: Academy = tier; break;
            }
        }

        private static int Clamp(int tier) => tier < 1 ? 1 : tier;
    }
}
