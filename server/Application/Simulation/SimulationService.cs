using System.Collections.Generic;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Fts.Application.Simulation;

/// <summary>Thin wrapper over Sim.Core (Phase 7.3). Every method builds its own transient objects
/// from the request, so the service holds no mutable state and is safe as a singleton. The logic
/// is intentionally identical to <see cref="DeterminismCheck"/> and the client's match path — that
/// sameness is exactly what makes the reports byte-identical across runtimes.</summary>
public sealed class SimulationService : ISimulationService
{
    /// <summary>Combined hash of the default <see cref="DeterminismCheck.Run()"/> — the value the
    /// client logs (golden master 0x3421951276465473, engine v3 / task 13.1 — v2 was 0xCDEA5A2F7B9E5CF6).
    /// Pinned by <c>SimulationDeterminismTests</c>.</summary>
    public ulong GoldenCombinedHash => 0x3421951276465473UL;

    public SimulateMatchResponse SimulateMatch(SimulateMatchRequest request)
    {
        League league = new LeagueGenerator().Generate(new Pcg32(request.WorldSeed));
        int clubCount = league.Clubs.Count;

        int home = Wrap(request.HomeClubIndex, clubCount);
        int away = Wrap(request.AwayClubIndex, clubCount);
        if (away == home)
            away = Wrap(away + 1, clubCount);

        ulong matchSeed = request.MatchSeed ?? (request.WorldSeed + 1000UL);

        var engine = new MatchEngine();
        MatchReport report = engine.Simulate(
            LineupSelector.BestEleven(league.Clubs[home]),
            LineupSelector.BestEleven(league.Clubs[away]),
            new Pcg32(matchSeed));

        // Hash the FULL report (incl. positions) so it matches the client's hash exactly,
        // regardless of whether we then strip positions from the response payload.
        ulong hash = MatchReportHasher.Hash(report);
        int frameCount = report.Positions?.TickCount ?? 0;

        var events = new List<MatchEventDto>(report.Events.Count);
        foreach (MatchEvent e in report.Events)
            events.Add(new MatchEventDto(e.Minute, e.Type.ToString(), e.ClubId, e.PlayerId));

        return new SimulateMatchResponse
        {
            EngineVersion = report.EngineVersion,
            WorldSeed = request.WorldSeed,
            MatchSeed = matchSeed,
            HomeClubId = report.HomeClubId,
            AwayClubId = report.AwayClubId,
            HomeGoals = report.HomeGoals,
            AwayGoals = report.AwayGoals,
            Events = events,
            PositionFrameCount = request.IncludePositions ? frameCount : 0,
            ReportHash = hash,
            ReportHashHex = ToHex(hash),
        };
    }

    public DeterminismCheckResponse RunDeterminismCheck(ulong? worldSeed = null, int? matches = null)
    {
        ulong seed = worldSeed ?? DeterminismCheck.DefaultWorldSeed;
        int count = matches ?? DeterminismCheck.DefaultMatches;

        DeterminismCheck.Result result = DeterminismCheck.Run(seed, count);

        bool isDefaultRun = seed == DeterminismCheck.DefaultWorldSeed
                            && count == DeterminismCheck.DefaultMatches;

        var perMatch = new List<string>(result.MatchHashes.Count);
        foreach (ulong h in result.MatchHashes)
            perMatch.Add(ToHex(h));

        return new DeterminismCheckResponse
        {
            WorldSeed = result.WorldSeed,
            MatchCount = result.MatchHashes.Count,
            CombinedHash = result.CombinedHash,
            CombinedHashHex = result.CombinedHashHex,
            GoldenHashHex = ToHex(GoldenCombinedHash),
            MatchesGolden = isDefaultRun && result.CombinedHash == GoldenCombinedHash,
            MatchHashesHex = perMatch,
        };
    }

    private static int Wrap(int index, int count)
    {
        int m = index % count;
        return m < 0 ? m + count : m;
    }

    private static string ToHex(ulong value) => "0x" + value.ToString("X16");
}
