namespace Fts.Services
{
    /// <summary>
    /// Dev-only switches. <see cref="OnlineTestTools"/> gates the in-client buttons that hit the server's
    /// dev-only <c>/internal/dev/*</c> seeding endpoints (create a ready test league, make the bots bid) —
    /// so a single developer can exercise online leagues without hand-creating accounts. Defaults on in the
    /// Editor and development builds, off in release; flip it by hand if you need to force it.
    /// </summary>
    public static class DevFlags
    {
        public static bool OnlineTestTools =
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            true;
#else
            false;
#endif
    }
}
