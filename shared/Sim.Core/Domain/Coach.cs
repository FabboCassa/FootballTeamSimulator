namespace Sim.Core.Domain
{
    /// <summary>A coach - the user or an AI opponent. Career data grows in Phase 5.</summary>
    public sealed class Coach
    {
        private int _reputation = 50;

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsHuman { get; set; }

        /// <summary>Standing in the football world, in [0, 100]. Drives job offers.</summary>
        public int Reputation { get => _reputation; set => _reputation = AttributeScale.ClampCondition(value); }
    }
}
