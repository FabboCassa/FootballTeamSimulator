using System.Collections.Generic;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Analysis
{
    /// <summary>A named tactic (formation + instruction set) entered in the R8 tournament.</summary>
    public readonly struct TacticPreset
    {
        public string Name { get; }
        public Tactic Tactic { get; }

        public TacticPreset(string name, Tactic tactic)
        {
            Name = name;
            Tactic = tactic;
        }
    }

    /// <summary>
    /// The field of the R8 tactic tournament. The game has no preset list of its own, so this is
    /// one recognisable style per formation — every shape is entered, and every instruction axis
    /// is pulled to both ends by at least one preset.
    /// </summary>
    public static class TacticPresets
    {
        public static readonly IReadOnlyList<TacticPreset> All = new[]
        {
            Preset("4-3-3 balanced", Formation.F433, Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Normal),
            Preset("4-4-2 counter", Formation.F442, Mentality.Defensive, Pressing.Low, Tempo.Fast, Width.Normal),
            Preset("3-5-2 possession", Formation.F352, Mentality.Balanced, Pressing.Medium, Tempo.Slow, Width.Narrow),
            Preset("4-2-3-1 high press", Formation.F4231, Mentality.Balanced, Pressing.High, Tempo.Fast, Width.Normal),
            Preset("5-3-2 low block", Formation.F532, Mentality.Defensive, Pressing.Low, Tempo.Slow, Width.Narrow),
            Preset("3-4-3 all-out attack", Formation.F343, Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Wide)
        };

        private static TacticPreset Preset(string name, Formation f, Mentality m, Pressing p, Tempo t, Width w) =>
            new TacticPreset(name, new Tactic(f, new TacticInstructions(m, p, t, w)));
    }
}
