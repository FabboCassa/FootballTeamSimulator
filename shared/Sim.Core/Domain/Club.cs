namespace Sim.Core.Domain
{
    /// <summary>A football club. Facilities and finances are added in Phase 5.</summary>
    public sealed class Club
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;

        public Squad Squad { get; set; } = new Squad();
        public Coach Coach { get; set; } = new Coach();
    }
}
