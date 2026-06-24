namespace Fts.Services
{
    /// <summary>Which side of a negotiation the user is on (task 5.3).</summary>
    public enum NegotiationMode
    {
        /// <summary>The user is buying an AI club's player (he makes the offers).</summary>
        Buy = 0,
        /// <summary>The user is selling one of his players to an AI club (he names the ask).</summary>
        Sell = 1
    }

    /// <summary>
    /// Hands the negotiation setup to the pushed Negotiation screen (task 5.3) — the same
    /// transient-handoff pattern as <see cref="PlayerProfileTarget"/>: ScreenNavigator.Push
    /// resolves presenters from DI with no constructor argument, so the Market screen stamps
    /// these fields just before pushing. Lives in the Game scope; never persisted.
    /// </summary>
    public sealed class MarketTarget
    {
        public NegotiationMode Mode { get; set; }

        /// <summary>The player being negotiated over.</summary>
        public int PlayerId { get; set; }

        /// <summary>The other club: the seller when buying, the buyer when selling.</summary>
        public int CounterpartyClubId { get; set; }
    }
}
