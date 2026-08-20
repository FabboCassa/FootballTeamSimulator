using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Generation
{
    /// <summary>
    /// Everything <see cref="WorldGenerator"/> needs (task 11.1): the scope the player chose plus
    /// the DATA the world is built from.
    ///
    /// The data is injectable on purpose. The world stays procedural — the user's plan is that real
    /// clubs and players arrive later as player-made mods — so a mod loader only has to deserialise
    /// its own <see cref="Nations"/> and <see cref="Cultures"/> and hand them over here. Sim.Core
    /// never reads a file itself (ARCHITECTURE.md rule), so the host owns the loading.
    /// </summary>
    public sealed class WorldGenerationOptions
    {
        /// <summary>Which nations are playable and how big the surrounding database is.</summary>
        public WorldScope Scope { get; set; } = new WorldScope();

        /// <summary>The atlas. Defaults to the built-in one; ORDER drives id allocation.</summary>
        public List<NationProfile> Nations { get; set; } = NationDatabase.BuiltIn();

        /// <summary>Naming pools keyed by <see cref="NationProfile.CultureId"/>.</summary>
        public Dictionary<string, NameCulture> Cultures { get; set; } = CultureDatabase.BuiltIn();

        /// <summary>Used when <see cref="WorldScope.Size"/> is Custom; ignored otherwise.</summary>
        public DatabaseSizePreset? CustomPreset { get; set; }

        public DatabaseSizePreset ResolvePreset() =>
            Scope.Size == DatabaseSize.Custom && CustomPreset != null
                ? CustomPreset
                : DatabaseSizePreset.For(Scope.Size);
    }
}
