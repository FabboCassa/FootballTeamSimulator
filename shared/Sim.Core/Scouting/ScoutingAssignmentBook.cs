using System.Collections.Generic;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// Where each club's scouts currently are (tasks 5.4 and 11.2). Distinct from
    /// <see cref="KnowledgeStore"/>: an assignment is the INTENT to keep scouting (it drives the
    /// weekly gain), whereas knowledge is the accumulated result, which persists even after the
    /// scout is called home. In-memory and host-owned (Export/Import).
    ///
    /// Task 11.2 turned the book from "a set of watched player ids per club" into "a list of
    /// <see cref="ScoutingAssignment"/> per club", because a scout can now be sent to a club, a
    /// nation or a continent instead of pointed at one man. The old per-player API is kept, exactly
    /// as it behaved, on top of the new one: <see cref="Assign(int,int)"/> files a
    /// <see cref="ScoutingAreaKind.Player"/> assignment, <see cref="For"/> returns those players'
    /// ids, and the whole-world weekly tick (which only knows about direct watches and the AI
    /// policy) is untouched. That is what lets a v16 save load, and the task 5.4 tests still pass,
    /// with no migration of the watch list.
    ///
    /// The user club's assignments are set explicitly by the player; AI clubs that have no explicit
    /// assignments fall back to <see cref="ScoutingPolicy"/> when the progressor runs.
    /// </summary>
    public sealed class ScoutingAssignmentBook
    {
        private readonly Dictionary<int, List<ScoutingAssignment>> _byClub
            = new Dictionary<int, List<ScoutingAssignment>>();

        // ------------------------------------------------------ per-player API (task 5.4b)

        /// <summary>Starts watching a named player (idempotent). Files a Player-kind assignment held by the department.</summary>
        public void Assign(int clubId, int playerId)
        {
            if (IsWatching(clubId, playerId))
                return;

            List<ScoutingAssignment> list = ListFor(clubId);
            list.Add(new ScoutingAssignment
            {
                ScoutId = 0,
                Area = ScoutingArea.ForPlayer(playerId),
                Filters = new ScoutingFilters()
            });
        }

        /// <summary>Stops watching a player (knowledge already gained is kept in the store).</summary>
        public void Unassign(int clubId, int playerId)
        {
            if (!_byClub.TryGetValue(clubId, out List<ScoutingAssignment>? list))
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                ScoutingAssignment a = list[i];
                if (a.Area.Kind == ScoutingAreaKind.Player && a.Area.PlayerId == playerId)
                    list.RemoveAt(i);
            }

            if (list.Count == 0)
                _byClub.Remove(clubId);
        }

        /// <summary>True if the club has at least one active assignment of any kind.</summary>
        public bool HasAny(int clubId)
            => _byClub.TryGetValue(clubId, out List<ScoutingAssignment>? list) && list.Count > 0;

        /// <summary>True if the club is actively watching this named player.</summary>
        public bool IsWatching(int clubId, int playerId)
        {
            if (!_byClub.TryGetValue(clubId, out List<ScoutingAssignment>? list))
                return false;

            foreach (ScoutingAssignment a in list)
            {
                if (a.Area.Kind == ScoutingAreaKind.Player && a.Area.PlayerId == playerId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The players a club is watching by NAME (empty if none). This is what the whole-world
        /// weekly tick advances; area briefs are advanced separately by
        /// <see cref="ScoutingProgressor.EvolveAreaWeek"/>, so a club running only area briefs
        /// correctly returns nothing here while still reporting <see cref="HasAny"/> true.
        /// </summary>
        public IReadOnlyCollection<int> For(int clubId)
        {
            if (!_byClub.TryGetValue(clubId, out List<ScoutingAssignment>? list))
                return System.Array.Empty<int>();

            var players = new List<int>(list.Count);
            foreach (ScoutingAssignment a in list)
            {
                if (a.Area.Kind == ScoutingAreaKind.Player)
                    players.Add(a.Area.PlayerId);
            }

            return players;
        }

        /// <summary>Snapshot of the NAMED watches for the host to persist (one row per watched player).</summary>
        public IEnumerable<(int ClubId, int PlayerId)> Export()
        {
            foreach (var kv in _byClub)
            {
                foreach (ScoutingAssignment a in kv.Value)
                {
                    if (a.Area.Kind == ScoutingAreaKind.Player)
                        yield return (kv.Key, a.Area.PlayerId);
                }
            }
        }

        /// <summary>Rehydrates one stored named watch (host load).</summary>
        public void Import(int clubId, int playerId) => Assign(clubId, playerId);

        // ------------------------------------------------------ area API (task 11.2)

        /// <summary>Every assignment a club is running, in the order they were filed.</summary>
        public IReadOnlyList<ScoutingAssignment> AssignmentsFor(int clubId)
            => _byClub.TryGetValue(clubId, out List<ScoutingAssignment>? list)
                ? (IReadOnlyList<ScoutingAssignment>)list
                : System.Array.Empty<ScoutingAssignment>();

        /// <summary>How many assignments the club is running (its scouts in the field).</summary>
        public int CountFor(int clubId) => AssignmentsFor(clubId).Count;

        /// <summary>What a given scout is currently doing, or null if he is at home.</summary>
        public ScoutingAssignment? AssignmentOfScout(int clubId, int scoutId)
        {
            if (scoutId <= 0 || !_byClub.TryGetValue(clubId, out List<ScoutingAssignment>? list))
                return null;

            foreach (ScoutingAssignment a in list)
            {
                if (a.ScoutId == scoutId)
                    return a;
            }

            return null;
        }

        /// <summary>
        /// Sends a scout out. A named scout can only hold ONE brief at a time — that is the whole
        /// limiter the user chose for 11.2 — so filing a new one for a scout who is already out
        /// REPLACES his brief (he is recalled and sent somewhere else) rather than failing silently.
        /// Returns true when this replaced an existing brief.
        /// </summary>
        public bool AddAssignment(int clubId, ScoutingAssignment assignment)
        {
            if (assignment == null)
                return false;

            bool replaced = false;
            if (assignment.ScoutId > 0)
                replaced = RemoveAssignment(clubId, assignment.ScoutId);

            ListFor(clubId).Add(assignment);
            return replaced;
        }

        /// <summary>Calls a scout home. Returns true if he was out. Knowledge already gained is kept.</summary>
        public bool RemoveAssignment(int clubId, int scoutId)
        {
            if (scoutId <= 0 || !_byClub.TryGetValue(clubId, out List<ScoutingAssignment>? list))
                return false;

            bool removed = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].ScoutId == scoutId)
                {
                    list.RemoveAt(i);
                    removed = true;
                }
            }

            if (list.Count == 0)
                _byClub.Remove(clubId);

            return removed;
        }

        /// <summary>Snapshot of EVERY assignment (named watches included) for the host to persist.</summary>
        public IEnumerable<(int ClubId, ScoutingAssignment Assignment)> ExportAll()
        {
            foreach (var kv in _byClub)
            {
                foreach (ScoutingAssignment a in kv.Value)
                    yield return (kv.Key, a);
            }
        }

        /// <summary>Rehydrates one stored assignment of any kind (host load).</summary>
        public void ImportAssignment(int clubId, ScoutingAssignment assignment)
        {
            if (assignment != null)
                ListFor(clubId).Add(assignment);
        }

        /// <summary>Drops everything a club has out in the field (used when a career changes club).</summary>
        public void ClearClub(int clubId) => _byClub.Remove(clubId);

        private List<ScoutingAssignment> ListFor(int clubId)
        {
            if (!_byClub.TryGetValue(clubId, out List<ScoutingAssignment>? list))
                _byClub[clubId] = list = new List<ScoutingAssignment>();

            return list;
        }
    }
}
