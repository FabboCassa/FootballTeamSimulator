namespace Sim.Core
{
    /// <summary>
    /// Library metadata. Also used as the smoke test for the
    /// Sim.Core -> Unity DLL pipeline (Roadmap task 0.3).
    /// </summary>
    public static class SimCoreInfo
    {
        public const string Version = "0.1.0";

        public static string Describe() => $"Sim.Core v{Version}";
    }
}
