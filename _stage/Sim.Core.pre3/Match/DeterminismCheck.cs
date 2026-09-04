using System.Collections.Generic;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Match
{
    /// <summary>
    /// Cross-runtime determinism check (task 1.6): generates a league and plays
    /// a fixed schedule of seeded matches, hashing every full MatchReport.
    /// The exact same code runs under plain .NET (NUnit) and inside Unity
    /// (Mono editor and IL2CPP player); the combined hash must be identical
    /// everywhere. Exercises generation, the result model, the position stream
    /// and the RNG in one pass.
    /// </summary>
    public static class DeterminismCheck
    {
        public const ulong DefaultWorldSeed = 20260611UL;
        public const int DefaultMatches = 50;

        public sealed class Result
        {
            public ulong WorldSeed { get; set; }
            public ulong CombinedHash { get; set; }
            public List<ulong> MatchHashes { get; set; } = new List<ulong>();

            public string CombinedHashHex => "0x" + CombinedHash.ToString("X16");
        }

        public static Result Run(ulong worldSeed = DefaultWorldSeed, int matches = DefaultMatches)
        {
            League league = new LeagueGenerator().Generate(new Pcg32(worldSeed));
            var engine = new MatchEngine();
            var result = new Result { WorldSeed = worldSeed };

            int clubCount = league.Clubs.Count;
            ulong combined = 14695981039346656037UL; // FNV-1a offset basis
            const ulong prime = 1099511628211UL;

            for (int i = 0; i < matches; i++)
            {
                // Deterministic pairing covering many different club pairs.
                int homeIndex = i % clubCount;
                int awayIndex = (i * 7 + 3) % clubCount;
                if (awayIndex == homeIndex) awayIndex = (awayIndex + 1) % clubCount;

                MatchReport report = engine.Simulate(
                    LineupSelector.BestEleven(league.Clubs[homeIndex]),
                    LineupSelector.BestEleven(league.Clubs[awayIndex]),
                    new Pcg32(worldSeed + 1000UL + (ulong)i));

                ulong hash = MatchReportHasher.Hash(report);
                result.MatchHashes.Add(hash);

                unchecked
                {
                    for (int b = 0; b < 8; b++)
                        combined = (combined ^ ((hash >> (b * 8)) & 0xFF)) * prime;
                }
            }

            result.CombinedHash = combined;
            return result;
        }
    }
}
