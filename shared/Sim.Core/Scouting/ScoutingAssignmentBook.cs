using System.Collections.Generic;
using System.Linq;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// Which players each club is ACTIVELY watching (task 5.4). Distinct from
    /// <see cref="KnowledgeStore"/>: an assignment is the intent to keep scouting (it drives
    /// weekly knowledge gain), whereas knowledge is the accumulated result that persists even
    /// after a player is dropped from the watch list. In-memory and host-owned (Export/Import).
    ///
    /// The user club's assignments are set explicitly by the player; AI clubs that have no
    /// explicit assignments fall back to <see cref="ScoutingPolicy"/> when the progressor runs.
    /// </summary>
    public sealed class ScoutingAssignmentBook
    {
        private readonly Dictionary<int, HashSet<int>> _watched = new Dictionary<int, HashSet<int>>();

        /// <summary>Starts watching a player (idempotent).</summary>
        public void Assign(int clubId, int playerId)
        {
            if (!_watched.TryGetValue(clubId, out var set))
                _watched[clubId] = set = new HashSet<int>();
            set.Add(playerId);
        }

        /// <summary>Stops watching a player (knowledge already gained is kept in the store).</summary>
        public void Unassign(int clubId, int playerId)
        {
            if (_watched.TryGetValue(clubId, out var set))
            {
                set.Remove(playerId);
                if (set.Count == 0) _watched.Remove(clubId);
            }
        }

        /// <summary>True if the club has at least one active assignment.</summary>
        public bool HasAny(int clubId)
            => _watched.TryGetValue(clubId, out var set) && set.Count > 0;

        /// <summary>True if the club is actively watching this player.</summary>
        public bool IsWatching(int clubId, int playerId)
            => _watched.TryGetValue(clubId, out var set) && set.Contains(playerId);

        /// <summary>The players a club is actively watching (empty if none).</summary>
        public IReadOnlyCollection<int> For(int clubId)
            => _watched.TryGetValue(clubId, out var set)
                ? (IReadOnlyCollection<int>)set
                : System.Array.Empty<int>();

        /// <summary>Snapshot for the host to persist (one row per watched player).</summary>
        public IEnumerable<(int ClubId, int PlayerId)> Export()
            => _watched.SelectMany(kv => kv.Value.Select(p => (kv.Key, p)));

        /// <summary>Rehydrates one stored assignment (host load).</summary>
        public void Import(int clubId, int playerId) => Assign(clubId, playerId);
    }
}
