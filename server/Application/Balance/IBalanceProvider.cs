using Sim.Core.Config;

namespace Fts.Application.Balance;

/// <summary>
/// The server's ACTIVE <see cref="BalanceConfig"/> (Phase 10.3, "balance push").
///
/// Until 10.3 every server-side system newed its own <c>BalanceConfig</c>, so the only way to retune a
/// live ladder was a redeploy. This holds one instance, swapped atomically when an admin pushes a
/// revision, and every gameplay system reads <see cref="Current"/> instead of constructing its own.
///
/// Three properties worth stating plainly, because they are the whole safety story:
/// <list type="bullet">
/// <item><b>The default is the build.</b> With nothing pushed, <see cref="Current"/> is
/// <c>new BalanceConfig()</c> — byte-identical to what the client ships and to what every existing test
/// asserts against. A server that has never had a push behaves exactly as it did before 10.3.</item>
/// <item><b>Sim.Core is untouched.</b> This is a server-side holder; Sim.Core still never reads a file or
/// a database, it only receives the object. The 216 Sim.Core tests and the golden master do not see
/// this class at all.</item>
/// <item><b>A push applies to what happens NEXT.</b> Matches already resolved keep their stored report,
/// so replays are unaffected. A live match being re-simmed from its seed (8.6) will use the new config
/// for the whole 90' from the next input onwards — an accepted, documented edge, and the reason a push
/// during a live window is a bad idea rather than an impossible one.</item>
/// </list>
///
/// Registered as a SINGLETON: the instance is process-wide, and the reference swap is atomic. Reads take
/// no lock — a caller either sees the old object or the new one, never a half-written one.
/// </summary>
public interface IBalanceProvider
{
    /// <summary>The config every gameplay system should read. Never null.</summary>
    BalanceConfig Current { get; }

    /// <summary>The revision <see cref="Current"/> came from; 0 means "the balance embedded in this
    /// build", i.e. nothing has ever been pushed (or the active revision failed to parse).</summary>
    int Revision { get; }

    /// <summary>When this process last swapped its config in. Null while still on the baseline.</summary>
    DateTime? LoadedUtc { get; }

    /// <summary>Swap the active config. Called by the loader at startup, by the reload job, and by a
    /// push. A <paramref name="revision"/> not greater than the current one is ignored, so a slow reload
    /// can never overwrite a newer push.</summary>
    void Set(BalanceConfig config, int revision);

    /// <summary>Back to the balance embedded in the build (revision 0). Used by tests and by the
    /// emergency path when a pushed revision turns out to be unreadable.</summary>
    void ResetToBaseline();
}
