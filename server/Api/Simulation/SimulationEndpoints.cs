using Fts.Application.Simulation;

namespace Fts.Api.Simulation;

/// <summary>
/// The internal match-simulation HTTP surface (Phase 7.3). NOT a gameplay API — it exists to prove
/// the server runs the same Sim.Core as the client (byte-identical reports) and to drive diagnostics.
/// Mapped only in non-Production environments and behind the <c>Simulation:ExposeInternalEndpoints</c>
/// flag (see Program.cs), so it is off in prod. Thin — each endpoint binds, calls
/// <see cref="ISimulationService"/> and returns the DTO.
/// </summary>
public static class SimulationEndpoints
{
    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/sim");

        // Fixed determinism self-check (mirrors Sim.Core test 1.6). Optional ?seed= & ?matches=
        // override the defaults; the default run should return matchesGolden=true on any correct
        // runtime. GET so it is trivially curl-able.
        group.MapGet("/determinism", (ISimulationService sim, ulong? seed, int? matches) =>
            Results.Ok(sim.RunDeterminismCheck(seed, matches)));

        // Parametric single seeded match between two clubs of a generated world.
        group.MapPost("/match", (SimulateMatchRequest req, ISimulationService sim) =>
            Results.Ok(sim.SimulateMatch(req)));

        return app;
    }
}
