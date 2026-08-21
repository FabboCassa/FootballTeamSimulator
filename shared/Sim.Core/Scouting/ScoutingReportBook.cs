using System.Collections.Generic;
using Sim.Core.Config;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// One name a scout brought back (task 11.2): who he found, on which brief, and when. The
    /// numbers are NOT stored — they are re-read from <see cref="KnowledgeStore"/> every time the
    /// screen opens, so a shortlist sharpens as the scout keeps working instead of freezing at the
    /// moment of discovery.
    /// </summary>
    public sealed class ScoutReportEntry
    {
        /// <summary>The player the scout put in front of us.</summary>
        public int PlayerId { get; set; }

        /// <summary>The <see cref="ScoutingArea.Key"/> of the brief that found him.</summary>
        public string AreaKey { get; set; } = string.Empty;

        /// <summary>The scout who found him (0 = the department).</summary>
        public int ScoutId { get; set; }

        /// <summary>Career week he was first reported — the UI sorts newest first on this.</summary>
        public int FoundWeek { get; set; }
    }

    /// <summary>
    /// The club's incoming scouting reports (task 11.2): the shortlist an area brief builds up week
    /// after week, which is what the manager actually reads instead of browsing the world's database
    /// by hand.
    ///
    /// Bounded twice, and the two bounds mean different things:
    ///   • PER BRIEF (<see cref="ScoutingBalance.MaxReportsPerArea"/>) — a full shortlist STOPS
    ///     taking new names rather than rolling. That is deliberate: a rolling list would quietly
    ///     drop the gem the scout found in week 3 to make room for week 40's journeyman, and the
    ///     manager would never know. A full list is a prompt to go and dismiss what you don't want.
    ///     It also stops a prolific continental brief from crowding out a patient one on a club.
    ///   • PER CLUB (<see cref="ScoutingBalance.MaxReportsPerClub"/>) — a last-resort ceiling on the
    ///     save, evicting oldest-first, so no combination of briefs can grow it without limit.
    ///
    /// Cancelling an assignment does NOT drop its reports — the names you were given are yours; only
    /// the deepening stops. In-memory and host-owned (Export/Import), like every other scouting store.
    /// </summary>
    public sealed class ScoutingReportBook
    {
        private readonly Dictionary<int, List<ScoutReportEntry>> _byClub
            = new Dictionary<int, List<ScoutReportEntry>>();

        private readonly Dictionary<int, HashSet<int>> _seen = new Dictionary<int, HashSet<int>>();

        /// <summary>True when this player is already on the club's shortlist (from any brief).</summary>
        public bool Has(int clubId, int playerId)
            => _seen.TryGetValue(clubId, out HashSet<int>? set) && set.Contains(playerId);

        /// <summary>The club's shortlist in discovery order, oldest first (empty if none).</summary>
        public IReadOnlyList<ScoutReportEntry> For(int clubId)
            => _byClub.TryGetValue(clubId, out List<ScoutReportEntry>? list)
                ? (IReadOnlyList<ScoutReportEntry>)list
                : System.Array.Empty<ScoutReportEntry>();

        /// <summary>How many names the club is holding in total.</summary>
        public int Count(int clubId) => For(clubId).Count;

        /// <summary>How many names one brief has produced.</summary>
        public int CountOfArea(int clubId, string areaKey)
        {
            int n = 0;
            foreach (ScoutReportEntry entry in For(clubId))
            {
                if (entry.AreaKey == areaKey)
                    n++;
            }

            return n;
        }

        /// <summary>Player ids reported under one brief, in discovery order.</summary>
        public List<int> PlayersOfArea(int clubId, string areaKey)
        {
            var result = new List<int>();
            foreach (ScoutReportEntry entry in For(clubId))
            {
                if (entry.AreaKey == areaKey)
                    result.Add(entry.PlayerId);
            }

            return result;
        }

        /// <summary>
        /// Files a new report. Refused (returns false) when the player is already on the list or when
        /// his brief has hit <paramref name="maxPerArea"/>. Oldest-first eviction only ever kicks in
        /// at the club-wide ceiling <paramref name="maxPerClub"/>.
        /// </summary>
        public bool Add(int clubId, ScoutReportEntry entry, int maxPerArea, int maxPerClub)
        {
            if (entry == null || Has(clubId, entry.PlayerId))
                return false;

            if (maxPerArea > 0 && CountOfArea(clubId, entry.AreaKey) >= maxPerArea)
                return false;

            if (!_byClub.TryGetValue(clubId, out List<ScoutReportEntry>? list))
                _byClub[clubId] = list = new List<ScoutReportEntry>();
            if (!_seen.TryGetValue(clubId, out HashSet<int>? set))
                _seen[clubId] = set = new HashSet<int>();

            list.Add(entry);
            set.Add(entry.PlayerId);

            int cap = maxPerClub > 0 ? maxPerClub : int.MaxValue;
            while (list.Count > cap)
            {
                set.Remove(list[0].PlayerId);
                list.RemoveAt(0);
            }

            return true;
        }

        /// <summary>Removes one name from the shortlist (the manager dismissing a report).</summary>
        public void Remove(int clubId, int playerId)
        {
            if (!_byClub.TryGetValue(clubId, out List<ScoutReportEntry>? list))
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].PlayerId == playerId)
                    list.RemoveAt(i);
            }

            if (_seen.TryGetValue(clubId, out HashSet<int>? set))
                set.Remove(playerId);

            if (list.Count == 0)
            {
                _byClub.Remove(clubId);
                _seen.Remove(clubId);
            }
        }

        /// <summary>Drops everything a club holds (used when a career changes club).</summary>
        public void ClearClub(int clubId)
        {
            _byClub.Remove(clubId);
            _seen.Remove(clubId);
        }

        /// <summary>Snapshot for the host to persist.</summary>
        public IEnumerable<(int ClubId, ScoutReportEntry Entry)> Export()
        {
            foreach (var kv in _byClub)
            {
                foreach (ScoutReportEntry entry in kv.Value)
                    yield return (kv.Key, entry);
            }
        }

        /// <summary>Rehydrates one stored report (host load); call order is the stored order.</summary>
        public void Import(int clubId, ScoutReportEntry entry) => Add(clubId, entry, 0, 0);
    }
}
