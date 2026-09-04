namespace Sim.Core.Identity
{
    /// <summary>
    /// A club's full generated visual identity (task 6.1): its colour <see cref="Colors"/>
    /// palette plus a render-ready <see cref="Crest"/>. Produced deterministically from
    /// (clubId, worldSeed) by <see cref="ClubIdentityGenerator"/>, so it needs no storage —
    /// it is regenerated identically on demand for display, costs nothing at save time and is
    /// stable across reloads and replays. The club's initials for the crest come from the
    /// host's <see cref="Domain.Club.ShortName"/> (Sim.Core does not own display strings).
    /// Pure value type — no behaviour.
    /// </summary>
    public readonly struct ClubIdentity
    {
        public int ClubId { get; }
        public ClubColors Colors { get; }
        public CrestDesign Crest { get; }

        public ClubIdentity(int clubId, ClubColors colors, CrestDesign crest)
        {
            ClubId = clubId;
            Colors = colors;
            Crest = crest;
        }
    }
}
