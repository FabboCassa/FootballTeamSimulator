using Fts.Infrastructure.Leagues;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Fts.Api.Tests;

/// <summary>
/// R13 (issue #84): the 5 fps stream must stay affordable on the wire. The stored replay that
/// <c>GetReplayAsync</c> and the live-match state hand to clients is <see cref="ReplayStore.Write"/>'s
/// string, so that is what is measured, against the same match written at the v11 cadence.
/// </summary>
[TestFixture]
public class ReplayPayloadBudgetTests
{
    private const int V11StreamTicksPerFrame = 5;
    private const double MaxGrowth = 2.6;
    private const ulong FirstSeed = 8400;
    private const int Matches = 3;

    [Test]
    public void FullReport_AtFiveFramesPerSecond_StaysWithinGrowthAndPayloadLimit()
    {
        League league = new LeagueGenerator().Generate(new Pcg32(DeterminismCheck.DefaultWorldSeed));
        Lineup home = LineupSelector.BestEleven(league.Clubs[2]);
        Lineup away = LineupSelector.BestEleven(league.Clubs[5]);

        var v11Cfg = new BalanceConfig();
        v11Cfg.Match.StreamTicksPerFrame = V11StreamTicksPerFrame;
        var current = new MatchEngine(new BalanceConfig());
        var v11 = new MatchEngine(v11Cfg);

        // No limit is configured anywhere in the stack (Program.cs, appsettings, Caddyfile), so the
        // binding one is Kestrel's built-in body cap — the largest transport limit the API has.
        long limit = new KestrelServerOptions().Limits.MaxRequestBodySize!.Value;

        long nowBytes = 0, v11Bytes = 0, largest = 0;
        for (ulong seed = FirstSeed; seed < FirstSeed + Matches; seed++)
        {
            MatchReport report = current.Simulate(home, away, new Pcg32(seed));
            Assert.That(report.Positions!.TicksPerMinute, Is.EqualTo(5 * 60), "five frames per match second");

            long size = ReplayStore.Write(report).Length;
            nowBytes += size;
            largest = Math.Max(largest, size);
            v11Bytes += ReplayStore.Write(v11.Simulate(home, away, new Pcg32(seed))).Length;
        }

        double ratio = (double)nowBytes / v11Bytes;
        TestContext.Out.WriteLine(
            $"[replay-size] v11 {v11Bytes / Matches / 1024} KB · now {nowBytes / Matches / 1024} KB · " +
            $"ratio {ratio:F2} · largest {largest / 1024} KB · limit {limit / 1024} KB");

        Assert.That(ratio, Is.LessThanOrEqualTo(MaxGrowth), "the denser stream may cost at most 2.6x the v11 report");
        Assert.That(largest, Is.LessThanOrEqualTo(limit), "a full report must fit the online payload limit");
    }
}
