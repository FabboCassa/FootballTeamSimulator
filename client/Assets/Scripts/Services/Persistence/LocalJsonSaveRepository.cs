using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Fts.Services.Persistence
{
    /// <summary>
    /// Gzip-JSON save in Application.persistentDataPath. Writes go to a temp
    /// file first and replace the old save only when complete, so a crash
    /// mid-write cannot corrupt an existing save.
    /// Logs an FNV-1a state hash on save and load: identical hashes across
    /// a quit/relaunch prove the state round-tripped identically (task 2.2 check).
    /// </summary>
    public sealed class LocalJsonSaveRepository : ISaveRepository
    {
        private const int CurrentSaveVersion = 7;
        private const string FileName = "career.sav";

        private static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        public bool HasSave => File.Exists(SavePath);

        public void Save(CareerState state)
        {
            state.SaveVersion = CurrentSaveVersion;
            string json = JsonConvert.SerializeObject(state);
            string tempPath = SavePath + ".tmp";

            using (var file = File.Create(tempPath))
            using (var gzip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                gzip.Write(bytes, 0, bytes.Length);
            }

            if (File.Exists(SavePath))
                File.Delete(SavePath);
            File.Move(tempPath, SavePath);

            Debug.Log($"[Save] Saved career (state hash 0x{Fnv1a(json):X8}).");
        }

        public SaveLoadStatus TryLoad(out CareerState state)
        {
            state = null;

            if (!HasSave)
                return SaveLoadStatus.NotFound;

            try
            {
                string json;
                using (var file = File.OpenRead(SavePath))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                var loaded = JsonConvert.DeserializeObject<CareerState>(json);
                if (loaded == null || loaded.Leagues == null || loaded.Leagues.Count == 0 || loaded.GetUserClub() == null)
                    return SaveLoadStatus.Corrupted;

                if (loaded.SaveVersion > CurrentSaveVersion)
                    return SaveLoadStatus.IncompatibleVersion;

                Migrate(loaded);

                // Re-serialize: the hash must match the one logged at save time.
                Debug.Log($"[Save] Loaded career (state hash 0x{Fnv1a(JsonConvert.SerializeObject(loaded)):X8}).");
                state = loaded;
                return SaveLoadStatus.Success;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Failed to load save: {e.Message}");
                return SaveLoadStatus.Corrupted;
            }
        }

        public void Delete()
        {
            if (HasSave)
                File.Delete(SavePath);
        }

        private static void Migrate(CareerState state)
        {
            var config = new Sim.Core.Config.BalanceConfig();

            // v1 -> v2 (task 2.3): saves had no Season. Fixtures derive
            // deterministically from the world seed, so regenerating them gives
            // exactly what a fresh career with the same seed would have.
            if (state.SaveVersion < 2)
            {
                state.Season = new Sim.Core.Domain.Season
                {
                    Fixtures = new Sim.Core.Generation.FixtureGenerator(config.Season)
                        .Generate(state.Leagues[0], new Sim.Core.Random.Pcg32(state.Seed, CareerFactory.FixtureSequence))
                };
                state.SaveVersion = 2;
                Debug.Log("[Save] Migrated save v1 -> v2 (season fixtures generated).");
            }

            // v2 -> v3 (task 2.7): single league became a two-division world.
            // Division 2 is generated from the same seed streams a fresh career
            // would use; its overdue fixtures get simulated on the next advance.
            if (state.SaveVersion < 3)
            {
                var div2 = new Sim.Core.Generation.LeagueGenerator(new Sim.Core.Generation.LeagueGenerationOptions
                {
                    LeagueId = 2,
                    Division = 2,
                    LeagueName = state.Leagues[0].Name + " 2",
                    FirstClubId = CareerFactory.Div2FirstClubId,
                    FirstPlayerId = CareerFactory.Div2FirstPlayerId
                }, config).Generate(new Sim.Core.Random.Pcg32(state.Seed, CareerFactory.Div2GenSequence));

                state.Leagues.Add(div2);

                int maxFixtureId = 0;
                foreach (Sim.Core.Domain.Fixture f in state.Season.Fixtures)
                {
                    if (f.Id > maxFixtureId)
                        maxFixtureId = f.Id;
                }

                state.Season.Fixtures.AddRange(new Sim.Core.Generation.FixtureGenerator(config.Season)
                    .Generate(div2, new Sim.Core.Random.Pcg32(state.Seed, CareerFactory.Div2FixtureSequence), maxFixtureId + 1));

                state.SaveVersion = 3;
                Debug.Log("[Save] Migrated save v2 -> v3 (second division added).");
            }

            // v3 -> v4 (task 3.3): user tactic + per-tactic familiarity added.
            // Both are additive (null tactic = neutral, empty familiarity map),
            // so an upgraded save behaves exactly as before until the user picks
            // a tactic; nothing to regenerate.
            if (state.SaveVersion < 4)
            {
                // UserTactic stays null (= neutral); guarantee a non-null map.
                state.TacticFamiliarity ??= new System.Collections.Generic.Dictionary<string, int>();
                state.SaveVersion = 4;
                Debug.Log("[Save] Migrated save v3 -> v4 (tactic + familiarity added).");
            }

            // v4 -> v5 (task 4.3): training added. UserTraining stays null (= balanced
            // default), and LastTrainingWeek is seeded from the current calendar week so
            // the world is NOT retroactively trained for weeks the save already lived
            // through — training simply begins from the next week boundary onward.
            if (state.SaveVersion < 5)
            {
                int period = config.Season.DaysBetweenRounds;
                state.LastTrainingWeek = period > 0 ? state.Season.CurrentDay / period : 0;
                state.SaveVersion = 5;
                Debug.Log("[Save] Migrated save v4 -> v5 (training added; no retroactive development).");
            }

            // v5 -> v6 (task 4.4): development & ageing went live with per-player minutes
            // tracking. The accumulators are additive — an upgraded save simply begins a
            // fresh minutes window (no retroactive minutes), so no data needs regenerating.
            if (state.SaveVersion < 6)
            {
                state.StartsSinceTraining ??= new System.Collections.Generic.Dictionary<int, int>();
                state.UserMatchesSinceTraining = 0;
                state.SaveVersion = 6;
                Debug.Log("[Save] Migrated save v5 -> v6 (development & ageing live; minutes tracking).");
            }

            // v6 -> v7 (task 4.5): player support actions. Both collections are additive —
            // an upgraded save simply starts with no cooldowns and no pending rests, so there
            // is nothing to regenerate; just guarantee the maps are non-null.
            if (state.SaveVersion < 7)
            {
                state.SupportCooldowns ??= new System.Collections.Generic.Dictionary<string, int>();
                state.RestedSinceTraining ??= new System.Collections.Generic.List<int>();
                state.SaveVersion = 7;
                Debug.Log("[Save] Migrated save v6 -> v7 (player support actions added).");
            }
        }

        private static uint Fnv1a(string text)
        {
            uint hash = 2166136261u;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return hash;
        }
    }
}
