using System.Collections.Generic;
using Sim.Core.Config;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// Sparse per-(observer club, player) scouting knowledge level (task 5.4). Only pairs a
    /// club has actually scouted carry an entry, so a whole-world store stays small (bounded
    /// by assignments × weeks, not clubs × players). In-memory and host-owned — like the
    /// support-action cooldown log and the tactic familiarity log, Sim.Core keeps it pure and
    /// the host (client save / server store) persists it via <see cref="Export"/>/<see cref="Import"/>.
    ///
    /// Knowledge persists even when a player is no longer actively watched: it freezes at its
    /// last level (you don't forget what you scouted) until observation resumes.
    /// </summary>
    public sealed class KnowledgeStore
    {
        /// <summary>Knowledge level keyed by (observer club, player). Absent ⇒ 0 (unscouted).</summary>
        private readonly Dictionary<(int ClubId, int PlayerId), int> _knowledge
            = new Dictionary<(int, int), int>();

        /// <summary>Current knowledge a club has of a player (0 if never scouted).</summary>
        public int Get(int clubId, int playerId)
            => _knowledge.TryGetValue((clubId, playerId), out int k) ? k : 0;

        /// <summary>Sets a club's knowledge of a player (clamped to [0, MaxKnowledge]).</summary>
        public void Set(int clubId, int playerId, int knowledge, ScoutingBalance cfg)
        {
            int k = knowledge < 0 ? 0 : knowledge > cfg.MaxKnowledge ? cfg.MaxKnowledge : knowledge;
            _knowledge[(clubId, playerId)] = k;
        }

        /// <summary>Advances a club's knowledge of a player by one week of a scout of the given level.</summary>
        public void Accrue(int clubId, int playerId, int scoutLevel, ScoutingBalance cfg)
            => _knowledge[(clubId, playerId)] = ScoutingModel.Accrue(Get(clubId, playerId), scoutLevel, cfg);

        /// <summary>Snapshot for the host to persist (club, player, knowledge level).</summary>
        public IEnumerable<(int ClubId, int PlayerId, int Knowledge)> Export()
        {
            foreach (var kv in _knowledge)
                yield return (kv.Key.ClubId, kv.Key.PlayerId, kv.Value);
        }

        /// <summary>Rehydrates one stored entry (host load).</summary>
        public void Import(int clubId, int playerId, int knowledge)
            => _knowledge[(clubId, playerId)] = knowledge;
    }
}
