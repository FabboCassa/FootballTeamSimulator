using Sim.Core.Domain;

namespace Sim.Core.Market
{
    /// <summary>
    /// One completed transfer in a window (task 5.2). Plain data for the host to log, show in
    /// the transfer news feed (task 5.3) and persist. Identifies the player and both clubs by id
    /// (and carries display names so the host need not re-resolve a player who has changed squads).
    /// </summary>
    public sealed class TransferRecord
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public PositionRole Role { get; set; }

        public int FromClubId { get; set; }
        public string FromClubName { get; set; } = string.Empty;

        public int ToClubId { get; set; }
        public string ToClubName { get; set; } = string.Empty;

        /// <summary>Agreed fee in game-currency units.</summary>
        public long Fee { get; set; }
    }
}
