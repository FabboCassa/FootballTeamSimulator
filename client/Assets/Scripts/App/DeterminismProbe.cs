using Sim.Core.Match;
using UnityEngine;

namespace Fts.App
{
    /// <summary>
    /// Unity half of the cross-runtime determinism check (task 1.6).
    /// Add to the Boot scene, then run:
    ///   1. in the Editor (Mono),
    ///   2. in an IL2CPP player build.
    /// The hash shown on screen / in the log must equal the one printed by
    /// the .NET test DeterminismCheckTests.Check_IsStable_WithinRuntime_AndPrintsHash.
    /// Can be removed from the scene (or disabled) once 1.6 is verified.
    /// </summary>
    public sealed class DeterminismProbe : MonoBehaviour
    {
        private string _summary = "DeterminismCheck: running...";

        private void Start()
        {
            DeterminismCheck.Result result = DeterminismCheck.Run();

            _summary = $"[DeterminismCheck] seed={result.WorldSeed} " +
                       $"matches={result.MatchHashes.Count} combined={result.CombinedHashHex}";
            Debug.Log(_summary);
        }

        // On-screen output so the hash is readable in a player build
        // without hunting for the log file.
        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, Screen.width - 20, 60), _summary);
        }
    }
}
