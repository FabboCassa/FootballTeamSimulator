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

        /// <summary>1 = top flight. Used by promotion/relegation.</summary>
        public int Division { get; set; } = 1;

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
