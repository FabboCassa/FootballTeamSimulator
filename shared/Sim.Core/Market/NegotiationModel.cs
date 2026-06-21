using Sim.Core.Config;

namespace Sim.Core.Market
{
    /// <summary>How important a player is to his club — shapes the asking price and the floor below which he is never sold.</summary>
    public enum PlayerImportance
    {
        /// <summary>Surplus to requirements — priced to move.</summary>
        Surplus = 0,
        /// <summary>A useful squad player — priced at value.</summary>
        Squad = 1,
        /// <summary>A regular starter (best XI) — priced at a steep premium and never sold for peanuts.</summary>
        Starter = 2
    }

    public enum SellerDecision { Accept, Counter, Reject }
    public enum BuyerDecision { Accept, Offer, GiveUp }

    /// <summary>A seller's reply to an offer.</summary>
    public readonly struct SellerResponse
    {
        public readonly SellerDecision Decision;
        /// <summary>The new (lower) asking price when <see cref="Decision"/> is Counter.</summary>
        public readonly long CounterAsk;
        public SellerResponse(SellerDecision decision, long counterAsk)
        {
            Decision = decision;
            CounterAsk = counterAsk;
        }
    }

    /// <summary>A buyer's reply to a counter-ask.</summary>
    public readonly struct BuyerResponse
    {
        public readonly BuyerDecision Decision;
        /// <summary>The new (higher) offer when <see cref="Decision"/> is Offer; the fee to pay when Accept.</summary>
        public readonly long Offer;
        public BuyerResponse(BuyerDecision decision, long offer)
        {
            Decision = decision;
            Offer = offer;
        }
    }

    /// <summary>Result of running a full automatic negotiation.</summary>
    public readonly struct NegotiationResult
    {
        public readonly bool Agreed;
        public readonly long Fee;
        public readonly int Rounds;
        public NegotiationResult(bool agreed, long fee, int rounds)
        {
            Agreed = agreed;
            Fee = fee;
            Rounds = rounds;
        }
    }

    /// <summary>
    /// Multi-round offer/counteroffer negotiation (task 5.2, ARCHITECTURE.md §4.7:
    /// "negotiation = offer/counteroffer rounds"). PURE and deterministic — integer math,
    /// NO RNG — so a negotiation is a stable function of its inputs and replays identically;
    /// the only randomness in the market is the world-orchestration ordering in
    /// <see cref="TransferMarket"/>.
    ///
    /// The pieces are exposed as small steps so the interactive client (task 5.3) can drive a
    /// HUMAN buyer (the user makes each offer, the AI seller responds) AND the AI-vs-AI market
    /// can run the whole loop via <see cref="AutoNegotiate"/>. Two guards keep the AI honest:
    /// it never accepts below <see cref="MinSalePrice"/> (a permille of the player's plain
    /// value, much higher for a starter) — the "never sells its best XI for peanuts" ✅ —
    /// and the seller's counters converge toward the offer so a deal closes within a few rounds.
    /// </summary>
    public static class NegotiationModel
    {
        /// <summary>
        /// The seller's opening asking price: value shaped by importance and the seller's
        /// personality, then floored at <see cref="MinSalePrice"/> so a club never asks below what
        /// it would accept (keeps the no-peanuts guard airtight even for a Seller-type club's starter).
        /// </summary>
        public static long AskingPrice(long value, PlayerImportance importance, PersonalityProfile seller, TransferBalance cfg)
        {
            long ask = value;
            if (importance == PlayerImportance.Starter)
                ask = ask * cfg.StarterAskPremillePermille / 1000;
            else if (importance == PlayerImportance.Surplus)
                ask = ask * cfg.SurplusAskPermille / 1000;

            ask = ask * seller.AskPermille / 1000;

            long floor = MinSalePrice(value, importance, cfg);
            if (ask < floor) ask = floor;
            if (ask < 1) ask = 1;
            return ask;
        }

        /// <summary>The hard floor below which the seller never sells — no peanuts (steeper for a starter).</summary>
        public static long MinSalePrice(long value, PlayerImportance importance, TransferBalance cfg)
        {
            int permille = importance == PlayerImportance.Starter ? cfg.StarterMinSalePermille : cfg.MinSalePermille;
            long floor = value * permille / 1000;
            return floor < 1 ? 1 : floor;
        }

        /// <summary>The most a buyer will pay for a target: valuation × willingness × personality, capped by budget.</summary>
        public static long BuyerMaxPrice(long value, PersonalityProfile buyer, long budget, TransferBalance cfg)
        {
            long max = value * cfg.BuyerMaxValuePermille / 1000;
            max = max * buyer.BuyAggressionPermille / 1000;
            if (max > budget) max = budget;
            if (max < 0) max = 0;
            return max;
        }

        /// <summary>The buyer's opening offer against an asking price.</summary>
        public static long OpeningOffer(long askingPrice, long buyerMax, TransferBalance cfg)
        {
            long offer = askingPrice * cfg.OpeningOfferPermille / 1000;
            if (offer > buyerMax) offer = buyerMax;
            if (offer < 1) offer = 1;
            return offer;
        }

        /// <summary>
        /// The seller's reply to <paramref name="offer"/> against its current ask. Accepts a fair
        /// offer, rejects a lowball, otherwise counters with a lower ask that converges toward the
        /// offer — but never below <paramref name="minSale"/>.
        /// </summary>
        public static SellerResponse EvaluateOffer(long currentAsk, long offer, long minSale, TransferBalance cfg)
        {
            // Accept: offer meets the ask, or is within the accept band AND clears the no-peanuts floor.
            if (offer >= currentAsk)
                return new SellerResponse(SellerDecision.Accept, offer);
            if (offer >= currentAsk * cfg.SellerAcceptPermille / 1000 && offer >= minSale)
                return new SellerResponse(SellerDecision.Accept, offer);

            // Reject: a lowball well under the ask, or anything below the floor it would have to counter into.
            if (offer < currentAsk * cfg.SellerWalkAwayPermille / 1000)
                return new SellerResponse(SellerDecision.Reject, currentAsk);

            // Counter: concede part of the gap, but never drop below the no-peanuts floor.
            long newAsk = currentAsk - (currentAsk - offer) * cfg.SellerConcessionPermille / 1000;
            if (newAsk < minSale) newAsk = minSale;
            if (newAsk <= offer) // already met the buyer — accept rather than counter below the offer
                return new SellerResponse(SellerDecision.Accept, offer);
            return new SellerResponse(SellerDecision.Counter, newAsk);
        }

        /// <summary>
        /// The buyer's reply to a seller's counter-ask. Accepts an ask within its max, otherwise
        /// raises its offer toward the ask (capped by its max); gives up if it cannot improve.
        /// </summary>
        public static BuyerResponse RespondToCounter(long lastOffer, long sellerAsk, long buyerMax, TransferBalance cfg)
        {
            if (sellerAsk <= buyerMax)
                return new BuyerResponse(BuyerDecision.Accept, sellerAsk);

            long newOffer = lastOffer + (sellerAsk - lastOffer) * cfg.BuyerConcessionPermille / 1000;
            if (newOffer > buyerMax) newOffer = buyerMax;
            if (newOffer <= lastOffer)
                return new BuyerResponse(BuyerDecision.GiveUp, lastOffer);
            return new BuyerResponse(BuyerDecision.Offer, newOffer);
        }

        /// <summary>
        /// Runs a full automatic negotiation between an AI buyer and an AI seller and returns the
        /// outcome. Used by <see cref="TransferMarket"/>; the interactive client uses the steps above.
        /// </summary>
        public static NegotiationResult AutoNegotiate(
            long value, PlayerImportance importance,
            PersonalityProfile seller, PersonalityProfile buyer, long buyerBudget, TransferBalance cfg)
        {
            long buyerMax = BuyerMaxPrice(value, buyer, buyerBudget, cfg);
            long minSale = MinSalePrice(value, importance, cfg);

            // If the buyer can't even reach the seller's floor, there is no deal.
            if (buyerMax < minSale)
                return new NegotiationResult(false, 0, 0);

            long ask = AskingPrice(value, importance, seller, cfg);
            long offer = OpeningOffer(ask, buyerMax, cfg);

            for (int round = 1; round <= cfg.MaxNegotiationRounds; round++)
            {
                SellerResponse s = EvaluateOffer(ask, offer, minSale, cfg);
                if (s.Decision == SellerDecision.Accept)
                    return new NegotiationResult(true, s.CounterAsk, round);
                if (s.Decision == SellerDecision.Reject)
                    return new NegotiationResult(false, 0, round);

                ask = s.CounterAsk;
                BuyerResponse b = RespondToCounter(offer, ask, buyerMax, cfg);
                if (b.Decision == BuyerDecision.Accept)
                    return new NegotiationResult(true, b.Offer, round);
                if (b.Decision == BuyerDecision.GiveUp)
                    return new NegotiationResult(false, 0, round);

                offer = b.Offer;
            }

            // Ran out of rounds: close if the parties have effectively met, else no deal.
            if (offer >= ask && offer >= minSale)
                return new NegotiationResult(true, offer, cfg.MaxNegotiationRounds);
            return new NegotiationResult(false, 0, cfg.MaxNegotiationRounds);
        }
    }
}
