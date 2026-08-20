namespace Sim.Core.Domain
{
    /// <summary>
    /// How many clubs/players are loaded OUTSIDE the playable leagues (task 11.1). The knob is
    /// orthogonal to the choice of playable nations: playable leagues are always full detail,
    /// this only decides how much of the rest of the world exists around them.
    /// Chosen at career creation and immutable for that save.
    /// </summary>
    public enum DatabaseSize
    {
        /// <summary>Only the playable nations plus a thin layer of top-division clubs elsewhere.</summary>
        Small = 0,

        /// <summary>The default: strong nations get a simulated top flight, the rest are data only.</summary>
        Medium = 1,

        /// <summary>Everything the built-in nation database knows about, at the deepest supported level.</summary>
        Large = 2,

        /// <summary>Every knob set by hand (see Generation.DatabaseSizePreset).</summary>
        Custom = 3
    }
}
