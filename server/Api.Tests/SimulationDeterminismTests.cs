using Fts.Application.Simulation;
using NUnit.Framework;
using Sim.Core.Match;

namespace Fts.Api.Tests;

/// <summary>
/// Server half of the cross-runtime determinism check (Phase 7.3, extends Sim.Core test 1.6).
/// The server runs the SAME Sim.Core as the client, so the fixed determinism run must produce the
/// exact combined hash the client logs (golden master 0xCDEA5A2F7B9E5CF6). Pure unit tests — no
/// WebApplicationFactory, no DB, no Redis.
/// </summary>
[TestFixture]
public class SimulationDeterminismTests
{
    // Engine v3 (task 13.1 — the possession movement model). The stream is part of the
    // report hash, so replacing the movement layer necessarily moved this value; the
    // score/event model is untouched. Every change to the movement model moves it, because the stream is part of the
    // report hash. Earlier values: v2 0xCDEA5A2F7B9E5CF6, first v3 0x3421951276465473.
    private const ulong GoldenCombinedHash = 0xBD336A9B5F155792UL;

    [Test]
    public void ServerRuntime_DeterminismCheck_MatchesTheClientGoldenHash()
    {
        // Raw Sim.Core on the server .NET runtime: this is the byte-identity guarantee vs the
        // client's Mono/IL2CPP/WebGL DeterminismProbe, which logs the same constant.
        DeterminismCheck.Result result = DeterminismCheck.Run();

        Assert.That(result.MatchHashes.Count, Is.EqualTo(DeterminismCheck.DefaultMatches));
        Assert.That(result.CombinedHash, Is.EqualTo(GoldenCombinedHash),
            $"Server combined hash {result.CombinedHashHex} != golden 0x{GoldenCombinedHash:X16} — " +
            "Sim.Core drifted or the runtime broke byte-identity.");

        TestContext.Out.WriteLine(
            $"[server-determinism] seed={result.WorldSeed} matches={result.MatchHashes.Count} " +
            $"combined={result.CombinedHashHex}");
    }

    [Test]
    public void SimulationService_DefaultDeterminismRun_ReportsMatchesGolden()
    {
        var sim = new SimulationService();

        DeterminismCheckResponse response = sim.RunDeterminismCheck();

        Assert.That(response.MatchCount, Is.EqualTo(DeterminismCheck.DefaultMatches));
        Assert.That(response.CombinedHash, Is.EqualTo(GoldenCombinedHash));
        Assert.That(response.CombinedHashHex, Is.EqualTo("0xBD336A9B5F155792"));
        Assert.That(response.MatchesGolden, Is.True);
        Assert.That(response.GoldenHashHex, Is.EqualTo(response.CombinedHashHex));
        Assert.That(sim.GoldenCombinedHash, Is.EqualTo(GoldenCombinedHash));
    }

    [Test]
    public void SimulationService_IsDeterministic_ForTheSameSeedAndInputs()
    {
        var sim = new SimulationService();
        var request = new SimulateMatchRequest
        {
            WorldSeed = 20260611UL,
            HomeClubIndex = 0,
            AwayClubIndex = 5,
            MatchSeed = 424242UL,
            IncludePositions = true,
        };

        SimulateMatchResponse a = sim.SimulateMatch(request);
        SimulateMatchResponse b = sim.SimulateMatch(request);

        Assert.That(b.ReportHash, Is.EqualTo(a.ReportHash), "Same seed + inputs must hash identically.");
        Assert.That(b.HomeGoals, Is.EqualTo(a.HomeGoals));
        Assert.That(b.AwayGoals, Is.EqualTo(a.AwayGoals));
        Assert.That(a.PositionFrameCount, Is.GreaterThan(0), "A watched match should carry a position stream.");
        Assert.That(a.MatchSeed, Is.EqualTo(424242UL));

        // A different match seed must change the report (guards against a constant hash bug).
        SimulateMatchResponse c = sim.SimulateMatch(request with { MatchSeed = 424243UL });
        Assert.That(c.ReportHash, Is.Not.EqualTo(a.ReportHash));
    }

    [Test]
    public void SimulationService_UnseededMatch_DefaultsToAReproducibleSeed()
    {
        var sim = new SimulationService();
        var request = new SimulateMatchRequest { WorldSeed = 777UL };

        SimulateMatchResponse first = sim.SimulateMatch(request);
        SimulateMatchResponse second = sim.SimulateMatch(request);

        Assert.That(first.MatchSeed, Is.EqualTo(777UL + 1000UL));
        Assert.That(second.ReportHash, Is.EqualTo(first.ReportHash));
    }
}
