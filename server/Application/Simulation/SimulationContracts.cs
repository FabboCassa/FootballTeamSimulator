namespace Fts.Application.Simulation;

/// <summary>Request/response DTOs for the internal match-simulation use cases (Phase 7.3).
/// Plain records so the Api layer binds them from JSON and the client can mirror them. The
/// service (<see cref="ISimulationService"/>) and its implementation both live in Application —
/// it only needs Sim.Core (which Application references) and has no DB/Redis dependency, unlike
/// the auth service which lives in Infrastructure.</summary>
public sealed record SimulateMatchRequest
{
    /// <summary>Seed for the procedural world the two clubs are drawn from.</summary>
    public ulong WorldSeed { get; init; } = 20260611UL;

    /// <summary>Index of the home club in the generated league (wrapped into range).</summary>
    public int HomeClubIndex { get; init; }

    /// <summary>Index of the away club in the generated league (wrapped into range;
    /// bumped by one if it collides with the home index).</summary>
    public int AwayClubIndex { get; init; } = 1;

    /// <summary>Seed for the match RNG. When omitted, defaults deterministically to
    /// <c>WorldSeed + 1000</c> so an un-seeded request is still fully reproducible.</summary>
    public ulong? MatchSeed { get; init; }

    /// <summary>Include the (regenerable) top-down position stream in the response. Off by default
    /// to keep the payload small — the report hash always covers the full report either way.</summary>
    public bool IncludePositions { get; init; }
}

/// <summary>One timeline entry, flattened for JSON (event type as its name).</summary>
public sealed record MatchEventDto(int Minute, string Type, int ClubId, int PlayerId);

/// <summary>Outcome of a single simulated match. The hash is the order-sensitive FNV-1a over the
/// FULL report (incl. positions) — the same value the client computes, so it is the unit of
/// byte-identity verification.</summary>
public sealed record SimulateMatchResponse
{
    public int EngineVersion { get; init; }
    public ulong WorldSeed { get; init; }
    public ulong MatchSeed { get; init; }
    public int HomeClubId { get; init; }
    public int AwayClubId { get; init; }
    public int HomeGoals { get; init; }
    public int AwayGoals { get; init; }
    public IReadOnlyList<MatchEventDto> Events { get; init; } = System.Array.Empty<MatchEventDto>();

    /// <summary>Number of position frames the sim produced (0 when positions were stripped).</summary>
    public int PositionFrameCount { get; init; }

    public ulong ReportHash { get; init; }
    public string ReportHashHex { get; init; } = string.Empty;
}

/// <summary>Result of the fixed determinism self-check (mirrors Sim.Core test 1.6). When run with
/// the default seed/count, <see cref="MatchesGolden"/> tells you the server produced the same
/// combined hash the client logs — i.e. byte-identical reports across runtimes.</summary>
public sealed record DeterminismCheckResponse
{
    public ulong WorldSeed { get; init; }
    public int MatchCount { get; init; }
    public ulong CombinedHash { get; init; }
    public string CombinedHashHex { get; init; } = string.Empty;
    public string GoldenHashHex { get; init; } = string.Empty;

    /// <summary>True only for a default-parameter run whose combined hash equals the golden value.</summary>
    public bool MatchesGolden { get; init; }

    public IReadOnlyList<string> MatchHashesHex { get; init; } = System.Array.Empty<string>();
}
