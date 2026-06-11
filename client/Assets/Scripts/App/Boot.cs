using Sim.Core;
using UnityEngine;

namespace Fts.App
{
    /// <summary>
    /// Entry point placed in the Boot scene.
    /// Logging SimCoreInfo proves the Sim.Core DLL pipeline works (Roadmap 0.3).
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log($"App started. {SimCoreInfo.Describe()}");
        }
    }
}
