namespace Sim.Core.Domain
{
    /// <summary>
    /// Long-term growth data. Potential is hidden from the user (scouting estimates it).
    /// Growth-curve logic arrives in Phase 4 (task 4.4).
    /// </summary>
    public sealed class PlayerDevelopment
    {
        private int _potential = 50;

        /// <summary>Ceiling of the player's overall ability, in [1, 100].</summary>
        public int Potential { get => _potential; set => _potential = AttributeScale.ClampSkill(value); }
    }
}
