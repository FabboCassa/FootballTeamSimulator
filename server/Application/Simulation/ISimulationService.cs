namespace Fts.Application.Simulation;

/// <summary>The server's match-simulation surface (Phase 7.3): the server runs the SAME Sim.Core
/// as the client, so a report produced here is byte-identical to the one the client re-renders
/// from (seed + inputs). Pure and synchronous — no I/O, no DB — so it is a stateless singleton.</summary>
public interface ISimulationService
{
    /// <summary>Simulate one seeded match between two clubs of a generated world.</summary>
    SimulateMatchResponse SimulateMatch(SimulateMatchRequest request);

    /// <summary>Run the fixed cross-runtime determinism check (task 1.6): a generated league,
    /// a deterministic schedule of seeded matches, every full report hashed and folded into a
    /// combined hash. Defaults reproduce the golden value the client logs.</summary>
    DeterminismCheckResponse RunDeterminismCheck(ulong? worldSeed = null, int? matches = null);

    /// <summary>The expected combined hash of the default determinism run — the value the client
    /// (Mono editor, IL2CPP and WebGL) logs. A mismatch means Sim.Core drifted or a runtime broke
    /// byte-identity.</summary>
    ulong GoldenCombinedHash { get; }
}
