using System;
using System.Text;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// The client string-table keys of the tactics vocabulary (R21), derived from the enum names
    /// so a new style, formation or level cannot ship without its strings (the loc tests walk them).
    /// </summary>
    public static class TacticsLocKeys
    {
        public static string Formation(Formation formation) =>
            "tactics.formation." + formation.ToString().ToLowerInvariant();

        public static string StyleName(PlayingStyle style) => "tactics.style." + Snake(style.ToString());

        /// <summary>The style's one-line description.</summary>
        public static string StyleDescription(PlayingStyle style) => StyleName(style) + ".desc";

        public static string Phase(InstructionPhase phase) => "tactics.phase." + Snake(phase.ToString());

        public static string AxisName(InstructionAxis axis) => "tactics.axis." + Snake(axis.ToString());

        /// <summary>The instruction's one-line description.</summary>
        public static string AxisDescription(InstructionAxis axis) => AxisName(axis) + ".desc";

        /// <summary>A level of an axis, e.g. "tactics.pressing.high" (the v11 keys keep their names).</summary>
        public static string Level(InstructionAxis axis, int level)
        {
            string name = Enum.GetName(TacticEditor.LevelType(axis), level)
                          ?? throw new ArgumentOutOfRangeException(nameof(level));
            return "tactics." + Snake(axis.ToString()) + "." + Snake(name);
        }

        private static string Snake(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
