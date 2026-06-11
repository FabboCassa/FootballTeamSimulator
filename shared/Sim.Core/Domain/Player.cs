namespace Sim.Core.Domain
{
    /// <summary>A footballer. Pure data; behaviour lives in the engine systems.</summary>
    public sealed class Player
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Age { get; set; }
        public PositionRole Role { get; set; }

        public PlayerAttributes Attributes { get; set; } = new PlayerAttributes();
        public PlayerCondition Condition { get; set; } = new PlayerCondition();
        public PlayerDevelopment Development { get; set; } = new PlayerDevelopment();
        public Contract Contract { get; set; } = new Contract();

        public string FullName => string.IsNullOrEmpty(FirstName) ? LastName : $"{FirstName} {LastName}";
    }
}
