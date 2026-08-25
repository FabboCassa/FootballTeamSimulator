using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Scouting;

namespace Fts.Services
{
    /// <summary>What one database preset costs ON THIS DEVICE (task 11.3).</summary>
    public sealed class WorldBenchResult
    {
        public DatabaseSize Size;
        public int Nations;
        public int Clubs;
        public int Players;

        /// <summary>Wall-clock time to generate the whole world.</summary>
        public double GenerationMs;

        /// <summary>Managed heap the generated world holds (measured across a forced collection).</summary>
        public long HeapBytes;

        /// <summary>The world as raw JSON, and as the gzip the save file actually stores.</summary>
        public long JsonBytes;
        public long GzipBytes;

        /// <summary>Building the world search index, and one whole-world query through it.</summary>
        public double IndexMs;
        public double SearchMs;
        public int SearchTotal;
    }

    /// <summary>
    /// THE MEASUREMENT THAT DECIDES THE SHIPPED DEFAULT PRESET — inherited by task 11.3 from 11.1,
    /// where it was the one thing left open.
    ///
    /// The desktop half already exists (tools/BalanceHarness, scenario "world"), and it says a Large
    /// world is 26,000 players, a 1.34MB gzip save and 15ms of generation. None of that decides
    /// anything, because the binding constraint is the WEAKEST target: WebGL, and a mid-range
    /// Android phone. This runs the same measurement THERE — same generator, same serializer, same
    /// gzip the save uses — so the default in <see cref="Sim.Core.Generation.DatabaseSizePreset"/>
    /// can stop being a judgement call and become a number.
    ///
    /// Deliberately one preset per call: generating Small, Medium and Large back to back would
    /// freeze a phone for seconds and tell you nothing you could not learn one tap at a time.
    /// </summary>
    public static class WorldBench
    {
        private static readonly BalanceConfig Config = new BalanceConfig();

        /// <summary>The seed every bench run uses, so two devices measure the same world.</summary>
        public const ulong BenchSeed = 20260803UL;

        public static WorldBenchResult Run(CareerFactory factory, DatabaseSize size)
            => Run(factory, size, BenchSeed);

        public static WorldBenchResult Run(CareerFactory factory, DatabaseSize size, ulong seed)
        {
            var result = new WorldBenchResult { Size = size };
            if (factory == null)
                return result;

            var scope = new WorldScope { Size = size };
            scope.Playable.Add(new PlayableNation
            {
                Code = CareerFactory.DefaultNation,
                PlayableTiers = CareerFactory.DefaultTiers
            });

            long baseline = GC.GetTotalMemory(true);
            var clock = Stopwatch.StartNew();
            World world = factory.GenerateWorld(seed, scope);
            clock.Stop();

            result.GenerationMs = clock.Elapsed.TotalMilliseconds;
            result.HeapBytes = Math.Max(0, GC.GetTotalMemory(true) - baseline);
            result.Nations = world.Nations.Count;
            result.Clubs = world.ClubCount();
            result.Players = world.PlayerCount();

            // The save path, exactly as LocalJsonSaveRepository writes it: Newtonsoft JSON, gzipped.
            byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(world));
            result.JsonBytes = json.Length;
            result.GzipBytes = GzipSize(json);

            clock.Restart();
            WorldPlayerIndex index = WorldPlayerIndex.Build(world, Config.Scouting);
            clock.Stop();
            result.IndexMs = clock.Elapsed.TotalMilliseconds;

            var query = new PlayerSearchQuery { PageSize = 20, ExcludeOwnClub = false };
            index.Search(query, null, seed, ScoutQuality.Neutral); // warm the code path

            clock.Restart();
            PlayerSearchPage page = index.Search(query, null, seed, ScoutQuality.Neutral);
            clock.Stop();
            result.SearchMs = clock.Elapsed.TotalMilliseconds;
            result.SearchTotal = page.Total;

            return result;
        }

        private static long GzipSize(byte[] payload)
        {
            using (var buffer = new MemoryStream())
            {
                using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, true))
                    gzip.Write(payload, 0, payload.Length);

                return buffer.Length;
            }
        }
    }
}
