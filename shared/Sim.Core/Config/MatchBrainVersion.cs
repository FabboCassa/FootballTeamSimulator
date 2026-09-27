namespace Sim.Core.Config
{
    /// <summary>
    /// The decision layer of the watched match (see <see cref="MatchBalance.Brain"/>). V10 is zero
    /// so that a balance document written before the setting existed still plays the engine the
    /// golden master pins.
    /// </summary>
    public enum MatchBrainVersion
    {
        V10 = 0,
        V11 = 1
    }
}
