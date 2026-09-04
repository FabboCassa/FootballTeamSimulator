using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// PUBLIC KNOWLEDGE — what everybody already knows about a player without sending anyone to
    /// look (task 11.3, the user's rule: "you can look at the whole world, but if nobody has
    /// watched him you will not see much — except the famous ones: the more famous a player is,
    /// the more public his numbers are").
    ///
    /// This is what makes a world-wide search screen possible without dismantling task 11.2. The
    /// manager may now browse every one of the tens of thousands of players in the database, but
    /// what he READS is still governed by knowledge: an unknown 19-year-old in a Chilean second
    /// division shows as a name and a role with useless ±22 bands, while the continent's best
    /// striker reads almost as well as a scouted player, because that is exactly how football
    /// works — you do not need a scout to know roughly how good the man on television is.
    ///
    /// FAME is derived, never stored: overall ability weighted against the SPOTLIGHT he plays in
    /// (his nation's reputation, his division's tier, his club's stature), damped for the very
    /// young — a 17-year-old is not famous yet however good he already is, which keeps the wonderkid
    /// hunt firmly the scouts' job. Deriving it rather than storing it means no save bump, and it
    /// tracks a transfer or a promotion by itself: sign him from a data-only club into your Serie A
    /// side and he becomes public property the same week.
    ///
    /// It turns into a FLOOR on knowledge (<see cref="Floor"/>), never a ceiling and never a
    /// replacement: the club's real, scouted knowledge always wins when it is higher, so scouting a
    /// famous player is still worth it — it takes him from "roughly known" to exact. Because the
    /// floor is applied by the CALLER (the host's read boundary and the search index) rather than
    /// inside <see cref="ScoutingModel"/>, every task 5.4 / 11.2 number, every stored save and the
    /// golden master are untouched.
    ///
    /// Pure integer math, no RNG, no allocation.
    /// </summary>
    public static class PublicKnowledge
    {
        /// <summary>
        /// How famous a player is, 0..100, from public facts only. Never touches a hidden value —
        /// potential plays no part, which is precisely why a scout is still the only way to find
        /// the next great player before everyone else does.
        /// </summary>
        public static int Fame(int overall, int age, int clubStrength, int division,
                               int nationReputation, ScoutingBalance cfg)
        {
            int nation = nationReputation > 0 ? nationReputation : cfg.FameDefaultNationReputation;
            if (nation > 100) nation = 100;

            int club = clubStrength > 0 ? clubStrength : cfg.FameDefaultClubStrength;
            if (club > 100) club = 100;

            int tier = division > 0 ? division : 1;
            int tierVisibility = 100 - (tier - 1) * cfg.FameDivisionDropPerTier;
            if (tierVisibility < cfg.FameDivisionFloor) tierVisibility = cfg.FameDivisionFloor;

            int spotlightWeight = cfg.FameNationWeight + cfg.FameDivisionWeight + cfg.FameClubWeight;
            if (spotlightWeight <= 0)
                return 0;

            int spotlight = (nation * cfg.FameNationWeight
                             + tierVisibility * cfg.FameDivisionWeight
                             + club * cfg.FameClubWeight) / spotlightWeight;

            int total = cfg.FameAbilityWeight + cfg.FameSpotlightWeight;
            if (total <= 0)
                return 0;

            int ability = AttributeScale.ClampSkill(overall);
            int fame = (ability * cfg.FameAbilityWeight + spotlight * cfg.FameSpotlightWeight) / total;

            // Reputation lags talent: a teenager is not a household name yet, however good he is.
            if (age > 0 && age < cfg.FameYouthAge)
                fame -= (cfg.FameYouthAge - age) * cfg.FameYouthDropPerYear;

            if (fame < 0) fame = 0;
            return fame > 100 ? 100 : fame;
        }

        /// <summary>Fame of a player in his current surroundings; any missing piece falls back to the config default.</summary>
        public static int FameOf(Player player, Club? club, League? league, Nation? nation, ScoutingBalance cfg)
        {
            if (player == null)
                return 0;

            return Fame(
                PlayerRating.Overall(player),
                player.Age,
                club != null ? Stature(club) : 0,
                league != null ? league.Division : 1,
                nation != null ? nation.Reputation : 0,
                cfg);
        }

        /// <summary>
        /// The same read, resolved from the world (convenience for a one-off lookup — the search
        /// index precomputes this for every player instead of calling it in a loop).
        /// </summary>
        public static int FameInWorld(World? world, Player player, ScoutingBalance cfg)
        {
            if (world == null || player == null)
                return 0;

            Club? club = world.ClubOfPlayer(player.Id);
            League? league = club != null ? world.LeagueOf(club.Id) : null;
            Nation? nation = league != null && !string.IsNullOrEmpty(league.NationCode)
                ? world.FindNation(league.NationCode)
                : null;

            return FameOf(player, club, league, nation, cfg);
        }

        /// <summary>
        /// A club's stature on the attribute scale: its generated <see cref="Club.Strength"/>, or the
        /// average of the squad it actually holds when the world predates that field (task 11.1) or
        /// the club was built by hand.
        /// </summary>
        public static int Stature(Club club)
        {
            if (club == null)
                return 0;

            if (club.Strength > 0)
                return club.Strength;

            int count = club.Squad.Players.Count;
            if (count == 0)
                return 0;

            int total = 0;
            foreach (Player player in club.Squad.Players)
                total += PlayerRating.Overall(player);

            return total / count;
        }

        /// <summary>
        /// The knowledge every club has of this player for free. Zero below
        /// <see cref="ScoutingBalance.PublicFameThreshold"/> — most of the database is genuinely
        /// anonymous — then linear up to <see cref="ScoutingBalance.PublicMaxKnowledgePercent"/> of
        /// full knowledge at fame 100. The ceiling is deliberately well short of 100%: even the best
        /// player in the world is "known", not "measured", until somebody actually goes and watches him.
        /// </summary>
        public static int Floor(int fame, ScoutingBalance cfg)
        {
            int threshold = cfg.PublicFameThreshold;
            if (threshold >= 100)
                return 0;

            if (fame <= threshold)
                return 0;

            int span = 100 - threshold;
            int percent = (fame - threshold) * cfg.PublicMaxKnowledgePercent / span;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            return cfg.MaxKnowledge * percent / 100;
        }

        /// <summary>The knowledge a club actually reads a player at: the better of what it learned and what everyone knows.</summary>
        public static int Effective(int storedKnowledge, int publicFloor)
        {
            int stored = storedKnowledge > 0 ? storedKnowledge : 0;
            return publicFloor > stored ? publicFloor : stored;
        }

        /// <summary>
        /// The lowest fame that reads as <paramref name="tier"/> — the exact inverse of
        /// <see cref="Tier"/>. The search screen's "reputation" filter steps through these, so moving
        /// <see cref="ScoutingBalance.PublicFameThreshold"/> moves the filter with the labels instead
        /// of silently putting the two out of step.
        /// </summary>
        public static int TierFloor(int tier, ScoutingBalance cfg)
        {
            if (tier <= 0)
                return 0;

            int threshold = cfg.PublicFameThreshold;
            int span = 100 - threshold;
            if (span <= 0)
                return threshold;

            if (tier == 1)
                return threshold + 1;

            return tier == 2 ? threshold + span / 3 + 1 : threshold + span * 2 / 3 + 1;
        }

        /// <summary>
        /// Fame as a coarse band for the UI, 0..3 (unknown / known locally / known / a name everyone
        /// knows). The host maps it to a word; the thresholds live here so the label can never drift
        /// away from the floor it describes.
        /// </summary>
        public static int Tier(int fame, ScoutingBalance cfg)
        {
            int threshold = cfg.PublicFameThreshold;
            if (fame <= threshold)
                return 0;

            int span = 100 - threshold;
            if (span <= 0)
                return 3;

            int above = fame - threshold;
            if (above * 3 <= span) return 1;
            if (above * 3 <= span * 2) return 2;
            return 3;
        }
    }
}
