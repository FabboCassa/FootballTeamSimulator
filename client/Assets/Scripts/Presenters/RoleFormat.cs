using Sim.Core.Domain;

namespace Fts.Presenters
{
    /// <summary>
    /// Maps a Sim.Core <see cref="PositionRole"/> to the reparto GROUP int the dumb views use to
    /// colour the role cell (task 6.9): 0 = goalkeeper (yellow), 1 = defence (green), 2 = midfield
    /// (blue), 3 = attack (red). The grouping mirrors the engine's own def/mid/att buckets
    /// (CB/FB → defence, DM/CM/AM → midfield, W/ST → attack) so the colours match how the sim
    /// actually classifies a player.
    /// </summary>
    public static class RoleFormat
    {
        public static int Group(PositionRole role)
        {
            switch (role)
            {
                case PositionRole.Goalkeeper:
                    return 0;
                case PositionRole.CentreBack:
                case PositionRole.FullBack:
                    return 1;
                case PositionRole.DefensiveMidfielder:
                case PositionRole.CentralMidfielder:
                case PositionRole.AttackingMidfielder:
                    return 2;
                default: // Winger, Striker
                    return 3;
            }
        }
    }
}
