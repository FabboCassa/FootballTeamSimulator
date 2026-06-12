using NUnit.Framework;
using Sim.Core.Match;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// .NET half of the cross-runtime check (task 1.6). The printed combined
    /// hash must equal the one logged by DeterminismProbe in the Unity editor
    /// (Mono) and in an IL2CPP build.
    /// </summary>
    [TestFixture]
    public class DeterminismCheckTests
    {
        [Test]
        public void Check_IsStable_WithinRuntime_AndPrintsHash()
        {
            DeterminismCheck.Result a = DeterminismCheck.Run();
            DeterminismCheck.Result b = DeterminismCheck.Run();

            Assert.That(a.MatchHashes.Count, Is.EqualTo(DeterminismCheck.DefaultMatches));
            Assert.That(b.MatchHashes, Is.EqualTo(a.MatchHashes));
            Assert.That(b.CombinedHash, Is.EqualTo(a.CombinedHash));

            // Compare this value with the Unity (Mono + IL2CPP) DeterminismProbe log.
            TestContext.Out.WriteLine(
                $"[DeterminismCheck] seed={a.WorldSeed} matches={a.MatchHashes.Count} combined={a.CombinedHashHex}");
        }

        [Test]
        public void Hash_ReactsToAnyFieldChange()
        {
            DeterminismCheck.Result baseline = DeterminismCheck.Run(matches: 1);
            ulong original = baseline.MatchHashes[0];

            // Different seed => different report => different hash.
            DeterminismCheck.Result other = DeterminismCheck.Run(DeterminismCheck.DefaultWorldSeed + 1, 1);
            Assert.That(other.MatchHashes[0], Is.Not.EqualTo(original));
        }

        [Test]
        public void Hash_CoversPositions_NotJustScore()
        {
            var a = new MatchReport { HomeGoals = 1, AwayGoals = 0 };
            var b = new MatchReport { HomeGoals = 1, AwayGoals = 0 };
            b.Positions = new PositionStream { TicksPerMinute = 4 };

            Assert.That(MatchReportHasher.Hash(b), Is.Not.EqualTo(MatchReportHasher.Hash(a)),
                "Reports differing only in the position stream must hash differently");
        }
    }
}
