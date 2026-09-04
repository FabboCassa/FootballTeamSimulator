namespace Sim.Core.Domain
{
    /// <summary>Employment terms binding a player to a club.</summary>
    public sealed class Contract
    {
        /// <summary>Wage per in-game week, in game currency units.</summary>
        public long WeeklyWage { get; set; }

        public int SeasonsRemaining { get; set; }
    }
}
