using System;
using System.Collections.Generic;
using Sim.Core.Match;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// Save v18 -> v19 (spec R23): a stored v11 tactic (formation + the four v11 axes) becomes a
    /// catalogue formation played as Balanced with the v11 axes as overrides. Lossless: the
    /// migrated plan plays exactly the v11 tactic, its familiarity stays under its key, and the
    /// prematch plan (instruction changes, substitutions, shouts) is carried over unchanged.
    /// Pure functions; the host applies them to its save.
    /// </summary>
    public static class TacticsSaveMigration
    {
        public const int FromSaveVersion = 18;
        public const int ToSaveVersion = 19;

        /// <summary>The migrated plan, or null when no tactic was stored (the neutral path).</summary>
        public static TacticPlan? MigrateTactic(TacticPlan? stored)
        {
            if (stored == null)
                return null;

            var instructions = TacticInstructions.FromV11(stored.Mentality, stored.Pressing, stored.Tempo, stored.Width);
            return TacticPlan.FromStyle(CatalogueFormation(stored.Formation),
                StylePlan.FromInstructions(PlayingStyle.Balanced, instructions));
        }

        /// <summary>
        /// Re-keys every v11 familiarity entry with the migrated tactic's <see cref="TacticPlan.Key"/>.
        /// Keys that merge onto one tactic keep the higher level; a key that is not a v11 key is kept as is.
        /// </summary>
        public static Dictionary<string, int> MigrateFamiliarity(IReadOnlyDictionary<string, int> stored)
        {
            var migrated = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> entry in stored)
            {
                TacticPlan? v11 = ParseV11Key(entry.Key);
                string key = v11 != null ? MigrateTactic(v11)!.Key() : entry.Key;
                migrated[key] = migrated.TryGetValue(key, out int level) ? Math.Max(level, entry.Value) : entry.Value;
            }

            return migrated;
        }

        /// <summary>A copy of the prematch plan: its rules hold v11 instruction sets, which v19 still reads.</summary>
        public static PrematchPlan? MigratePlan(PrematchPlan? stored) => stored?.Copy();

        // Every v11 formation is in the catalogue under the same value; anything else falls back to 4-3-3.
        private static Formation CatalogueFormation(Formation formation) =>
            Array.IndexOf(Formations.Catalogue, formation) >= 0 ? formation : Formation.F433;

        // A v11 key is "<formation>:<mentality><pressing><tempo><width>", one digit per axis.
        private static TacticPlan? ParseV11Key(string key)
        {
            int colon = key.IndexOf(':');
            if (colon <= 0 || key.Length != colon + 5 || !int.TryParse(key.Substring(0, colon), out int formation))
                return null;

            var axes = new int[4];
            for (int i = 0; i < 4; i++)
            {
                int digit = key[colon + 1 + i] - '0';
                if (digit < 0 || digit > 2) return null;
                axes[i] = digit;
            }

            return new TacticPlan
            {
                Formation = (Formation)formation,
                Mentality = (Mentality)axes[0],
                Pressing = (Pressing)axes[1],
                Tempo = (Tempo)axes[2],
                Width = (Width)axes[3]
            };
        }
    }
}
