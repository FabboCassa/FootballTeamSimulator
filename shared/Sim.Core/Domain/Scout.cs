namespace Sim.Core.Domain
{
    /// <summary>
    /// A scout employed by a club (task 5.4). Plain data; the scouting behaviour
    /// (how fast a watched player's attribute ranges narrow) lives in
    /// <see cref="Scouting.ScoutingModel"/> and is driven by <see cref="Level"/>.
    ///
    /// Additive to the domain and never referenced by the match engine, so adding
    /// scouts to a world leaves golden masters/replays unaffected. A club with no
    /// scouts still scouts slowly at ScoutingBalance.BaseClubScoutLevel.
    /// </summary>
    public sealed class Scout
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        private int _level = 1;

        /// <summary>Scout ability in [1, ScoutingBalance.MaxScoutLevel]; higher = faster knowledge. Clamped low end to 1.</summary>
        public int Level
        {
            get => _level;
            set => _level = value < 1 ? 1 : value;
        }
    }
}
