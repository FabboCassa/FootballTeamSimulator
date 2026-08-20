namespace Sim.Core.Generation
{
    /// <summary>
    /// A naming flavour: the pools a nation draws its invented players and clubs from (task 11.1).
    ///
    /// Everything in the world stays PROCEDURAL — no real clubs, no real footballers, no licensing.
    /// A culture only decides what the invented names SOUND like, so an Italian league reads Italian
    /// and a Japanese one reads Japanese. Towns are deliberately fictional toponyms.
    ///
    /// MOD HOOK: cultures are plain data passed in through
    /// <see cref="WorldGenerationOptions.Cultures"/>. A mod loader (planned: the player-supplied
    /// "real names" packs the user asked for) deserialises its own set from JSON and hands it to the
    /// generator — nothing here is static or hard-wired.
    /// </summary>
    public sealed class NameCulture
    {
        /// <summary>Stable key referenced by <see cref="NationProfile.CultureId"/>.</summary>
        public string Id { get; set; } = string.Empty;

        public string[] FirstNames { get; set; } = new string[0];
        public string[] LastNames { get; set; } = new string[0];

        /// <summary>Invented town names clubs are named after.</summary>
        public string[] Towns { get; set; } = new string[0];

        /// <summary>Club name prefixes ("FC", "Real", ...). Combined with a town to build a club name.</summary>
        public string[] ClubPrefixes { get; set; } = new string[0];

        public bool IsUsable =>
            FirstNames.Length > 0 && LastNames.Length > 0 && Towns.Length > 0 && ClubPrefixes.Length > 0;
    }
}
