using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Fts.Services.Persistence
{
    /// <summary>
    /// Flushes Application.persistentDataPath to the browser's IndexedDB on WebGL
    /// (Roadmap 6.3). Off WebGL persistentDataPath is a real on-disk folder, so
    /// Flush() is a no-op and the save is already durable once the file is closed.
    /// </summary>
    internal static class WebGLSaveSync
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void FtsSyncFilesystem();

        public static void Flush()
        {
            try
            {
                FtsSyncFilesystem();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Save] IndexedDB flush failed: {e.Message}");
            }
        }
#else
        public static void Flush()
        {
            // No-op outside the WebGL player: the file is already on disk.
        }
#endif
    }
}
