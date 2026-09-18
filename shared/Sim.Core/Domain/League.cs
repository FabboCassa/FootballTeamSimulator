using System.Collections.Generic;

namespace Sim.Core.Domain
{
    /// <summary>
    /// A division with a fixed number of clubs (like real leagues).
    /// Seasons, fixtures and standings are added in tasks 1.2 / 2.3.
    /// </summary>
    public sealed class League
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 1 = top flight. Used by promotion/relegation. Since task 11.1 this is the tier
        /// WITHIN <see cref="NationCode"/>: every nation has its own Division 1.
        /// </summary>
        public int Division { get; set; } = 1;

        /// <summary>
        /// Code of the nation this division belongs to, e.g. "ITA" (task 11.1). Empty on the
        /// pre-11.1 two-division worlds, which is exactly how the season rollover tells a legacy
        /// world (one flat pyramid) from a multi-nation one (promotion/relegation per nation).
        /// Additive — defaults empty, no save bump.
        /// </summary>
        public string NationCode { get; set; } = string.Empty;

        /// <summary>
        /// 1..100. Economic reputation of the nation this division belongs to (task: nation &amp;
        /// division wealth), scaling gate/sponsor/prize revenue via <see cref="Market.FinanceModel"/>.
        /// Denormalised from <see cref="Nation.EconomicReputation"/> onto the league at generation
        /// time so the finance progressor never needs a nation lookup. Defaults to 100 (a top-tier
        /// nation, i.e. no discount) — exactly the pre-change behaviour — for flat/legacy leagues
        /// that carry no nation info (empty <see cref="NationCode"/>) and for worlds built outside the
        /// multi-nation pipeline (ranked worlds, the balance harness). Additive — no save bump.
        /// </summary>
        public int EconomicReputation { get; set; } = 100;

        /// <summary>
        /// How much of this league is simulated (task 11.1). Defaults to
        /// <see cref="LeagueDetailLevel.Playable"/>, which is what every pre-11.1 league was,
        /// so old saves deserialize unchanged.
        /// </summary>
        public LeagueDetailLevel DetailLevel { get; set; } = LeagueDetailLevel.Playable;

        public List<Club> Clubs { get; set; } = new List<Club>();

        public Club? FindClub(int clubId)
        {
            foreach (Club club in Clubs)
            {
                if (club.Id == clubId)
                    return club;
            }

            return null;
        }

        /// <summary>Finds a player across every club's squad (null if absent).</summary>
        public Player? FindPlayer(int playerId)
        {
            foreach (Club club in Clubs)
            {
                foreach (Player player in club.Squad.Players)
                {
                    if (player.Id == playerId)
                        return player;
                }
            }

            return null;
        }
    }
}
