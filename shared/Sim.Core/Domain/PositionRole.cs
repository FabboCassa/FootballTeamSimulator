namespace Sim.Core.Domain
{
    /// <summary>On-pitch role of a player. Kept coarse on purpose (no L/R variants in v1).</summary>
    public enum PositionRole
    {
        Goalkeeper = 0,
        CentreBack = 1,
        FullBack = 2,
        DefensiveMidfielder = 3,
        CentralMidfielder = 4,
        AttackingMidfielder = 5,
        Winger = 6,
        Striker = 7
    }
}
