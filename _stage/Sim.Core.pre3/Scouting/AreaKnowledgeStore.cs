using System.Collections.Generic;
using Sim.Core.Config;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// How well a club knows an AREA (task 11.2) — the FM-style per-region meter, the twin of
    /// <see cref="KnowledgeStore"/> one level up. Keyed by (observer club, <see cref="ScoutingArea.Key"/>)
    /// and sparse: only areas a club has actually worked carry an entry.
    ///
    /// What it buys: an area assignment can only push per-player knowledge up to a ceiling that
    /// falls off with how wide the brief is (see <see cref="ScoutingModel.KnowledgeCap"/>) — a
    /// continental scout is structurally vague. Area knowledge LIFTS that ceiling. So the reward
    /// for leaving a scout in Brazil for three seasons is not just more names: every report from
    /// Brazil, by any scout, gets sharper. Hopping him around every month buys nothing.
    ///
    /// Like every other knowledge store this is in-memory and host-owned (Export/Import), it
    /// persists across seasons, and nothing in the match engine or SeasonProgressor reads it.
    /// </summary>
    public sealed class AreaKnowledgeStore
    {
        private readonly Dictionary<(int ClubId, string AreaKey), int> _knowledge
            = new Dictionary<(int, string), int>();

        /// <summary>How well a club knows an area, in [0, <see cref="ScoutingBalance.MaxAreaKnowledge"/>]. 0 = never been.</summary>
        public int Get(int clubId, string areaKey)
            => areaKey != null && _knowledge.TryGetValue((clubId, areaKey), out int k) ? k : 0;

        /// <summary>Sets a club's knowledge of an area (clamped to the scale).</summary>
        public void Set(int clubId, string areaKey, int knowledge, ScoutingBalance cfg)
        {
            if (string.IsNullOrEmpty(areaKey))
                return;

            int max = cfg.MaxAreaKnowledge > 0 ? cfg.MaxAreaKnowledge : 1;
            int k = knowledge < 0 ? 0 : knowledge > max ? max : knowledge;
            _knowledge[(clubId, areaKey)] = k;
        }

        /// <summary>
        /// Advances an area meter by one week of a scout of the given level and returns the new value.
        /// Deliberately slow (<see cref="ScoutingBalance.AreaKnowledgePerScoutLevelPerWeek"/> is a
        /// fraction of the per-player rate): learning a country is the work of seasons, not weeks.
        /// </summary>
        public int Accrue(int clubId, string areaKey, int scoutLevel, ScoutingBalance cfg)
        {
            if (string.IsNullOrEmpty(areaKey))
                return 0;

            int max = cfg.MaxAreaKnowledge > 0 ? cfg.MaxAreaKnowledge : 1;
            int gained = scoutLevel > 0 ? scoutLevel * cfg.AreaKnowledgePerScoutLevelPerWeek : 0;
            int k = Get(clubId, areaKey) + gained;
            if (k > max) k = max;
            _knowledge[(clubId, areaKey)] = k;
            return k;
        }

        /// <summary>Snapshot for the host to persist.</summary>
        public IEnumerable<(int ClubId, string AreaKey, int Knowledge)> Export()
        {
            foreach (var kv in _knowledge)
                yield return (kv.Key.ClubId, kv.Key.AreaKey, kv.Value);
        }

        /// <summary>Rehydrates one stored entry (host load).</summary>
        public void Import(int clubId, string areaKey, int knowledge)
        {
            if (!string.IsNullOrEmpty(areaKey))
                _knowledge[(clubId, areaKey)] = knowledge;
        }
    }
}
