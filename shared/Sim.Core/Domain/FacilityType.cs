namespace Sim.Core.Domain
{
    /// <summary>
    /// The four upgradeable club facilities (task 5.5, ARCHITECTURE.md §4.2). Each has a
    /// tier in <see cref="Facilities"/>; <see cref="Market.FacilityEffects"/> maps a tier to
    /// its in-game effect (training → development speed, stadium → gate capacity, scouting →
    /// scout level, academy → youth quality) and to the cost of the next upgrade.
    /// </summary>
    public enum FacilityType
    {
        Stadium = 0,
        Training = 1,
        Scouting = 2,
        Academy = 3
    }
}
