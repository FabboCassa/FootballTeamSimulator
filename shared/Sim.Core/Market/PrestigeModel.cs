using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Market
{
    public enum RefusalReason
    {
        None = 0,
        PrestigeTooLow = 1
    }

    public enum NegotiationOutcome
    {
        Agreed = 0,
        Rejected = 1,
        Refused = 2
    }

    /// <summary>
    /// Pure, deterministic model for club prestige and player transfer refusal (task: prestige-based
    /// transfer refusal, R11 of realistic-club-economy spec):
    /// - Club prestige = f(league wealth, club stature), pure function;
    /// - A player refuses a buying club whose prestige is below a threshold relative to his current
    ///   club and his own level;
    /// - Players aged &lt;= 21 or &gt;= 32 and non-best-XI players have lower thresholds.
    /// </summary>
    public static class PrestigeModel
    {
        /// <summary>
        /// Computes a club's prestige in [0, 1000] from its division's wealth (permille) and its own
        /// stature (0..100). Pure, integer arithmetic.
        /// </summary>
        public static int ClubPrestige(int leagueWealthMultiplierPermille, int clubStature, TransferBalance cfg)
        {
            if (cfg == null) throw new System.ArgumentNullException(nameof(cfg));
            if (leagueWealthMultiplierPermille < 0) leagueWealthMultiplierPermille = 0;
            if (clubStature < 0) clubStature = 0;
            if (clubStature > 100) clubStature = 100;

            int prestige = (leagueWealthMultiplierPermille * cfg.PrestigeLeagueWeightPermille
                            + (clubStature * 10) * cfg.PrestigeStatureWeightPermille) / 1000;
            if (prestige < 0) prestige = 0;
            if (prestige > 1000) prestige = 1000;
            return prestige;
        }

        /// <summary>Overload computing club prestige directly from club and league context.</summary>
        public static int ClubPrestige(Club club, int leagueLevel, int economicReputation, BalanceConfig cfg)
        {
            if (club == null) throw new System.ArgumentNullException(nameof(club));
            if (cfg == null) throw new System.ArgumentNullException(nameof(cfg));
            int wealth = FinanceModel.NationDivisionMultiplierPermille(economicReputation, leagueLevel, cfg.Finance);
            return ClubPrestige(wealth, club.Stature, cfg.Transfer);
        }

        /// <summary>
        /// The minimum buyer prestige required for a player at a club with <paramref name="sellerPrestige"/>
        /// to accept a transfer. Lower for bench (non-best-XI) players and for youth/veterans.
        /// </summary>
        public static int RequiredPrestige(Player player, PlayerImportance importance, int sellerPrestige, TransferBalance cfg)
        {
            if (cfg == null) throw new System.ArgumentNullException(nameof(cfg));

            int required = sellerPrestige - cfg.PrestigeMaxStarterDrop;

            if (importance != PlayerImportance.Starter)
                required -= cfg.PrestigeBenchToleranceDrop;

            if (player != null)
            {
                if (player.Age <= cfg.PrestigeYoungAgeThreshold || player.Age >= cfg.PrestigeVeteranAgeThreshold)
                    required -= cfg.PrestigeAgeToleranceDrop;

                int overall = PlayerRating.Overall(player);
                if (overall > cfg.PrestigeEliteOverallThreshold)
                    required += (overall - cfg.PrestigeEliteOverallThreshold) * cfg.PrestigePerOverallPoint;
            }

            if (required < cfg.PrestigeMinFloor) required = cfg.PrestigeMinFloor;
            return required;
        }

        /// <summary>Whether a player at <paramref name="sellerPrestige"/> will accept moving to <paramref name="buyerPrestige"/>.</summary>
        public static bool WillAccept(
            Player player, PlayerImportance importance,
            int sellerPrestige, int buyerPrestige,
            TransferBalance cfg)
        {
            int required = RequiredPrestige(player, importance, sellerPrestige, cfg);
            return buyerPrestige >= required;
        }

        /// <summary>Evaluates player refusal: returns RefusalReason.None if accepted, or PrestigeTooLow if refused.</summary>
        public static RefusalReason EvaluateRefusal(
            Player player, PlayerImportance importance,
            int sellerPrestige, int buyerPrestige,
            TransferBalance cfg)
        {
            return WillAccept(player, importance, sellerPrestige, buyerPrestige, cfg)
                ? RefusalReason.None
                : RefusalReason.PrestigeTooLow;
        }

        /// <summary>Evaluates player refusal with full club and league context.</summary>
        public static RefusalReason EvaluateRefusal(
            Player player, PlayerImportance importance,
            Club seller, int sellerLeagueLevel, int sellerEconomicReputation,
            Club buyer, int buyerLeagueLevel, int buyerEconomicReputation,
            BalanceConfig cfg)
        {
            if (seller == null) throw new System.ArgumentNullException(nameof(seller));
            if (buyer == null) throw new System.ArgumentNullException(nameof(buyer));
            if (cfg == null) throw new System.ArgumentNullException(nameof(cfg));

            int sellerPrestige = ClubPrestige(seller, sellerLeagueLevel, sellerEconomicReputation, cfg);
            int buyerPrestige = ClubPrestige(buyer, buyerLeagueLevel, buyerEconomicReputation, cfg);
            return EvaluateRefusal(player, importance, sellerPrestige, buyerPrestige, cfg.Transfer);
        }

        /// <summary>Convenience boolean check for refusal.</summary>
        public static bool IsRefused(
            Player player, PlayerImportance importance,
            Club seller, int sellerLeagueLevel, int sellerEconomicReputation,
            Club buyer, int buyerLeagueLevel, int buyerEconomicReputation,
            BalanceConfig cfg)
        {
            return EvaluateRefusal(player, importance, seller, sellerLeagueLevel, sellerEconomicReputation,
                                   buyer, buyerLeagueLevel, buyerEconomicReputation, cfg) != RefusalReason.None;
        }
    }
}
