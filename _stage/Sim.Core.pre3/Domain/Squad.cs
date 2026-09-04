using System.Collections.Generic;

namespace Sim.Core.Domain
{
    /// <summary>The set of players belonging to a club.</summary>
    public sealed class Squad
    {
        public List<Player> Players { get; set; } = new List<Player>();
    }
}
