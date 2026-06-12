using Sim.Core.Domain;

namespace Sim.Core.Generation
{
    /// <summary>Realistic 22-man squad composition used by the generator.</summary>
    public static class SquadTemplate
    {
        public static readonly (PositionRole Role, int Count)[] Default =
        {
            (PositionRole.Goalkeeper, 3),
            (PositionRole.CentreBack, 4),
            (PositionRole.FullBack, 3),
            (PositionRole.DefensiveMidfielder, 2),
            (PositionRole.CentralMidfielder, 3),
            (PositionRole.AttackingMidfielder, 2),
            (PositionRole.Winger, 3),
            (PositionRole.Striker, 2)
        };

        public static int TotalPlayers
        {
            get
            {
                int total = 0;
                foreach (var (_, count) in Default) total += count;
                return total;
            }
        }
    }
}
