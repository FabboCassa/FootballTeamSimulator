using Sim.Core.Domain;

namespace Fts.Services
{
    /// <summary>
    /// A user-club player put up for sale (task 5.3, the "transfer list" half of selling).
    /// Plain serialized data on the career: the player and the price the user is asking.
    /// Interested AI clubs generate <see cref="IncomingOffer"/>s against open listings during
    /// a transfer window (<see cref="LocalMarketService.GenerateListingOffers"/>). Cleared at
    /// season rollover (a listing never carries across the season break).
    /// </summary>
    public sealed class TransferListing
    {
        public int PlayerId { get; set; }

        /// <summary>The price the user wants. AI clubs meet it if they can afford it, else bid lower.</summary>
        public long AskingPrice { get; set; }
    }

    /// <summary>
    /// A pending bid from an AI club for one of the user's listed players (task 5.3). The user
    /// reviews it on the Market "Sell" tab and accepts (executes the sale) or rejects it. One
    /// offer per (player, club); deterministic — no RNG, a stable function of squads/budgets.
    /// </summary>
    public sealed class IncomingOffer
    {
        public int PlayerId { get; set; }
        public int FromClubId { get; set; }
        public string FromClubName { get; set; } = string.Empty;

        /// <summary>The fee the club is offering, in game-currency units.</summary>
        public long Fee { get; set; }
    }
}
