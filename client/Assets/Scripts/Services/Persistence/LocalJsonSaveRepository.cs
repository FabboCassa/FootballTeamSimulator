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
        private const int CurrentSaveVersion = 18;
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

            // On WebGL the move only touches the in-memory FS; commit it to IndexedDB.
            WebGLSaveSync.Flush();

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
                if (loaded == null)
                    return SaveLoadStatus.Corrupted;

                if (loaded.SaveVersion != CurrentSaveVersion)
                    return SaveLoadStatus.IncompatibleVersion;

                // Task 11.1b: a pre-v16 save stored a flat "Leagues" array. Lift it into a world
                // BEFORE anything else looks at it — every migration step below reads state.Leagues,
                // which is now a projection of the world rather than a stored field.
                loaded.EnsureWorld();

                if (loaded.Leagues.Count == 0 || loaded.GetUserClub() == null)
                    return SaveLoadStatus.Corrupted;

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
            {
                File.Delete(SavePath);
                // Commit the deletion to IndexedDB on WebGL.
                WebGLSaveSync.Flush();
            }
        }

        private static void Migrate(CareerState state)
        {
            // Migrations for pre-v18 saves dropped (R13): older saves are refused with IncompatibleVersion.
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
