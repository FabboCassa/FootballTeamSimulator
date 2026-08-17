namespace Fts.Application.Admin;

/// <summary>
/// The three facts about a running instance that live ops needs but that Infrastructure has no way to
/// discover for itself: which build it is, which Sim.Core it carries, and which environment it thinks it
/// is in. The Api already computes all three at startup (they are what <c>GET /health</c> reports), so it
/// registers this as a singleton rather than making a class library reach for the hosting abstractions.
///
/// <see cref="StartedUtc"/> is stamped once at composition time, which makes the uptime in the metrics an
/// honest "since this process came up" — the number that tells an operator whether a container has been
/// quietly restarting.
/// </summary>
public sealed record AdminRuntimeInfo(
    string Version,
    string SimCoreVersion,
    string Environment,
    DateTime StartedUtc);
