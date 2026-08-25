using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// The world's players, flattened once into an INDEX (task 11.3).
    ///
    /// Since task 11.1 a career can hold 26,000 players across 1,400 clubs, and both things that
    /// read them all — the scouts' weekly discovery scan and, new in 11.3, the manager's own search
    /// — used to walk every nation, every league, every club and every squad, chasing pointers
    /// through five levels of object graph to test "is he a striker?". That is what this replaces.
    ///
    /// The shape is deliberately boring, because boring is what is fast on a phone:
    ///   • ONE array of players in world order (nations in registry order, tiers top-first, clubs in
    ///     league order) — the same order everything else in Sim.Core enumerates in, so results are
    ///     deterministic and index-driven code returns exactly what the old walk returned;
    ///   • the filter-hot fields (role, age, fame) as parallel PRIMITIVE arrays, so the common
    ///     rejections never touch a Player object at all;
    ///   • contiguous SLICES per club and per nation, so "everyone in Brazil" is a start and a
    ///     count rather than a search, and a continent is a handful of such slices;
    ///   • the public-knowledge floor (<see cref="PublicKnowledge"/>) precomputed per player, since
    ///     it is derived from things that only change when the world does.
    ///
    /// It is a SNAPSHOT: it holds references into the live world, so attributes and knowledge it
    /// reads are always current, but a transfer, a promotion or a new player needs a rebuild. Build
    /// is one pass and no sorting; the host rebuilds it when the world moves (the client does it on
    /// a day change) rather than trying to patch it.
    ///
    /// Pure and deterministic: no RNG, no dictionary ITERATION (dictionaries are only ever probed),
    /// no culture-sensitive comparison.
    /// </summary>
    public sealed class WorldPlayerIndex
    {
        private readonly World _world;
        private readonly ScoutingBalance _cfg;

        private readonly Player[] _players;
        private readonly int[] _clubIds;
        private readonly byte[] _roles;
        private readonly byte[] _ages;
        private readonly int[] _fame;
        private readonly int[] _floor;

        private readonly Dictionary<int, int> _slotOfPlayer;
        private readonly Dictionary<int, int> _clubStart;
        private readonly Dictionary<int, int> _clubCount;
        private readonly Dictionary<string, int> _nationStart;
        private readonly Dictionary<string, int> _nationCount;
        private readonly List<string> _nationCodes;
        private readonly List<Continent> _nationContinents;

        private WorldPlayerIndex(World world, ScoutingBalance cfg, int size)
        {
            _world = world;
            _cfg = cfg;

            _players = new Player[size];
            _clubIds = new int[size];
            _roles = new byte[size];
            _ages = new byte[size];
            _fame = new int[size];
            _floor = new int[size];

            _slotOfPlayer = new Dictionary<int, int>(size);
            _clubStart = new Dictionary<int, int>();
            _clubCount = new Dictionary<int, int>();
            _nationStart = new Dictionary<string, int>();
            _nationCount = new Dictionary<string, int>();
            _nationCodes = new List<string>();
            _nationContinents = new List<Continent>();
        }

        /// <summary>How many players the index holds.</summary>
        public int Count { get; private set; }

        /// <summary>The world this index was built from (a rebuild is the only way to follow it).</summary>
        public World World => _world;

        /// <summary>
        /// Flattens the world. One pass, no sort: O(players), and the only allocation that scales is
        /// the six arrays plus the player-id lookup.
        /// </summary>
        public static WorldPlayerIndex Build(World world, ScoutingBalance cfg)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (cfg == null)
                throw new ArgumentNullException(nameof(cfg));

            var index = new WorldPlayerIndex(world, cfg, world.PlayerCount());
            index.Fill();
            return index;
        }

        private void Fill()
        {
            int slot = 0;

            foreach (Nation nation in _world.Nations)
            {
                int nationStart = slot;
                _nationCodes.Add(nation.Code);
                _nationContinents.Add(nation.Continent);

                foreach (League league in nation.Leagues)
                {
                    foreach (Club club in league.Clubs)
                    {
                        int clubStart = slot;
                        int stature = PublicKnowledge.Stature(club);

                        foreach (Player player in club.Squad.Players)
                        {
                            if (slot >= _players.Length)
                                break; // the world grew under us; the host rebuilds, it never crashes

                            int fame = PublicKnowledge.Fame(
                                PlayerRating.Overall(player), player.Age, stature,
                                league.Division, nation.Reputation, _cfg);

                            _players[slot] = player;
                            _clubIds[slot] = club.Id;
                            _roles[slot] = (byte)(int)player.Role;
                            _ages[slot] = player.Age < 0 ? (byte)0 : player.Age > 255 ? (byte)255 : (byte)player.Age;
                            _fame[slot] = fame;
                            _floor[slot] = PublicKnowledge.Floor(fame, _cfg);
                            _slotOfPlayer[player.Id] = slot;
                            slot++;
                        }

                        _clubStart[club.Id] = clubStart;
                        _clubCount[club.Id] = slot - clubStart;
                    }
                }

                // A world may hold the same nation code only once, but be defensive: the LAST slice
                // wins rather than throwing, since an index is a convenience, never a source of truth.
                _nationStart[nation.Code] = nationStart;
                _nationCount[nation.Code] = slot - nationStart;
            }

            Count = slot;
        }

        // ------------------------------------------------------------------ single-player reads

        /// <summary>How famous this player is, 0..100; 0 when he is not in the index.</summary>
        public int FameOf(int playerId)
            => _slotOfPlayer.TryGetValue(playerId, out int slot) ? _fame[slot] : 0;

        /// <summary>The free, public knowledge of this player (see <see cref="PublicKnowledge.Floor"/>).</summary>
        public int FloorOf(int playerId)
            => _slotOfPlayer.TryGetValue(playerId, out int slot) ? _floor[slot] : 0;

        /// <summary>True when the index holds this player.</summary>
        public bool Holds(int playerId) => _slotOfPlayer.ContainsKey(playerId);

        /// <summary>How many players an area holds at all, before any filter.</summary>
        public int AreaPlayerCount(ScoutingArea? area)
        {
            int total = 0;
            List<int> bounds = Bounds(area);
            for (int i = 0; i < bounds.Count; i += 2)
                total += bounds[i + 1] - bounds[i];

            return total;
        }

        // ------------------------------------------------------------------ the search

        /// <summary>
        /// Runs one paged query. The pass is: cheap primitive rejections (area, own club, role, age,
        /// fame) → the shared public-fact filter → the two estimate bands, built at the knowledge the
        /// club actually reads him at (its own scouting, or public fame, whichever is higher) → the
        /// estimate filters. Only survivors are kept, only the requested page is turned into objects.
        /// </summary>
        public PlayerSearchPage Search(PlayerSearchQuery query, KnowledgeStore? knowledge,
                                       ulong worldSeed, ScoutQuality quality)
        {
            var page = new PlayerSearchPage();
            if (query == null)
                return page;

            ScoutingFilters filters = query.Filters ?? new ScoutingFilters();
            string text = query.Text ?? string.Empty;
            bool hasText = text.Length > 0;
            int observer = query.ObserverClubId;

            var matches = new List<Match>();
            List<int> bounds = Bounds(query.Area);
            int scanned = 0;

            for (int b = 0; b < bounds.Count; b += 2)
            {
                int start = bounds[b];
                int end = bounds[b + 1];

                for (int slot = start; slot < end; slot++)
                {
                    scanned++;

                    if (query.ExcludeOwnClub && _clubIds[slot] == observer)
                        continue;

                    // Primitive rejections first: the same tests ScoutingFilters.MatchesFacts makes,
                    // but without touching the Player object. On a 26,000-player sweep this is what
                    // keeps a keystroke under a frame.
                    if (filters.Role >= 0 && _roles[slot] != filters.Role)
                        continue;

                    int age = _ages[slot];
                    if (filters.MinAge > 0 && age < filters.MinAge)
                        continue;
                    if (filters.MaxAge > 0 && age > filters.MaxAge)
                        continue;

                    if (query.MinFame > 0 && _fame[slot] < query.MinFame)
                        continue;

                    Player player = _players[slot];
                    if (player == null)
                        continue;

                    if (hasText && !NameMatches(player, text))
                        continue;

                    if (!filters.MatchesFacts(player, _cfg))
                        continue;

                    int scouted = knowledge != null ? knowledge.Get(observer, player.Id) : 0;
                    if (query.ScoutedOnly && scouted <= 0)
                        continue;

                    int effective = PublicKnowledge.Effective(scouted, _floor[slot]);

                    ScoutedRange overall = ScoutingModel.OverallOf(player, effective, worldSeed, observer, _cfg, quality);
                    ScoutedRange potential = ScoutingModel.PotentialOf(player, effective, worldSeed, observer, _cfg, quality);

                    if (!filters.MatchesEstimates(overall, potential))
                        continue;

                    matches.Add(new Match(slot, effective, scouted, overall.Estimate, potential.Estimate));
                }
            }

            page.Scanned = scanned;
            page.Total = matches.Count;

            Sort(matches, query.Sort);

            int pageSize = query.PageSize > 0 ? query.PageSize : 20;
            int pageCount = (matches.Count + pageSize - 1) / pageSize;
            int index = query.Page < 0 ? 0 : query.Page;
            if (index >= pageCount) index = pageCount > 0 ? pageCount - 1 : 0;

            page.PageCount = pageCount;
            page.Page = index;

            int from = index * pageSize;
            int to = from + pageSize;
            if (to > matches.Count) to = matches.Count;

            for (int i = from; i < to; i++)
            {
                Match match = matches[i];
                Player player = _players[match.Slot];
                int clubId = _clubIds[match.Slot];
                Club? club = _world.FindClub(clubId);

                page.Hits.Add(new PlayerSearchHit
                {
                    Player = player,
                    Club = club,
                    League = club != null ? _world.LeagueOf(clubId) : null,
                    PlayerId = player.Id,
                    ClubId = clubId,
                    Fame = _fame[match.Slot],
                    FameTier = PublicKnowledge.Tier(_fame[match.Slot], _cfg),
                    Knowledge = match.Knowledge,
                    ScoutedKnowledge = match.Scouted,
                    Overall = ScoutingModel.OverallOf(player, match.Knowledge, worldSeed, observer, _cfg, quality),
                    Potential = ScoutingModel.PotentialOf(player, match.Knowledge, worldSeed, observer, _cfg, quality)
                });
            }

            return page;
        }

        /// <summary>
        /// The area's players, in world order, as index slots — what a discovery scan walks instead
        /// of the club-by-club object graph (see <see cref="ScoutingDiscovery"/>).
        /// </summary>
        public List<int> Bounds(ScoutingArea? area)
        {
            var bounds = new List<int>(4);

            if (area == null)
            {
                if (Count > 0)
                {
                    bounds.Add(0);
                    bounds.Add(Count);
                }

                return bounds;
            }

            switch (area.Kind)
            {
                case ScoutingAreaKind.Player:
                {
                    if (_slotOfPlayer.TryGetValue(area.PlayerId, out int slot))
                    {
                        bounds.Add(slot);
                        bounds.Add(slot + 1);
                    }

                    break;
                }

                case ScoutingAreaKind.Club:
                {
                    if (_clubStart.TryGetValue(area.ClubId, out int start)
                        && _clubCount.TryGetValue(area.ClubId, out int count) && count > 0)
                    {
                        bounds.Add(start);
                        bounds.Add(start + count);
                    }

                    break;
                }

                case ScoutingAreaKind.Nation:
                {
                    string code = area.NationCode ?? string.Empty;
                    if (_nationStart.TryGetValue(code, out int start)
                        && _nationCount.TryGetValue(code, out int count) && count > 0)
                    {
                        bounds.Add(start);
                        bounds.Add(start + count);
                    }

                    break;
                }

                case ScoutingAreaKind.Continent:
                {
                    // Nations are contiguous but a continent's nations need not be adjacent, so a
                    // continent is a handful of slices — still start/count arithmetic, never a search.
                    for (int i = 0; i < _nationCodes.Count; i++)
                    {
                        if (_nationContinents[i] != area.Continent)
                            continue;

                        string code = _nationCodes[i];
                        if (!_nationStart.TryGetValue(code, out int start)
                            || !_nationCount.TryGetValue(code, out int count) || count <= 0)
                            continue;

                        bounds.Add(start);
                        bounds.Add(start + count);
                    }

                    break;
                }
            }

            return bounds;
        }

        /// <summary>The player sitting in a slot (used by the discovery scan; null when out of range).</summary>
        public Player? PlayerAt(int slot)
            => slot >= 0 && slot < Count ? _players[slot] : null;

        /// <summary>The club id of a slot.</summary>
        public int ClubAt(int slot) => slot >= 0 && slot < Count ? _clubIds[slot] : 0;

        /// <summary>The role of a slot, as the raw <see cref="PositionRole"/> value.</summary>
        public int RoleAt(int slot) => slot >= 0 && slot < Count ? _roles[slot] : -1;

        /// <summary>The age of a slot.</summary>
        public int AgeAt(int slot) => slot >= 0 && slot < Count ? _ages[slot] : 0;

        /// <summary>The public-knowledge floor of a slot.</summary>
        public int FloorAt(int slot) => slot >= 0 && slot < Count ? _floor[slot] : 0;

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Ordinal, case-insensitive match on either name. Deliberately NOT on the concatenated full
        /// name: <see cref="Player.FullName"/> allocates, and allocating 26,000 strings per keystroke
        /// is precisely the kind of thing this class exists to avoid.
        /// </summary>
        private static bool NameMatches(Player player, string text)
        {
            if (player.LastName != null
                && player.LastName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return player.FirstName != null
                   && player.FirstName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Sort(List<Match> matches, PlayerSearchSort sort)
        {
            switch (sort)
            {
                case PlayerSearchSort.Ability:
                    matches.Sort((a, b) => Then(b.Overall.CompareTo(a.Overall), a, b));
                    break;
                case PlayerSearchSort.Potential:
                    matches.Sort((a, b) => Then(b.Potential.CompareTo(a.Potential), a, b));
                    break;
                case PlayerSearchSort.Age:
                    matches.Sort((a, b) => Then(_ages[a.Slot].CompareTo(_ages[b.Slot]), a, b));
                    break;
                case PlayerSearchSort.Value:
                    matches.Sort((a, b) => Then(
                        _players[b.Slot].MarketValue.CompareTo(_players[a.Slot].MarketValue), a, b));
                    break;
                case PlayerSearchSort.Name:
                    matches.Sort((a, b) => Then(
                        string.CompareOrdinal(_players[a.Slot].LastName, _players[b.Slot].LastName), a, b));
                    break;
                default:
                    matches.Sort((a, b) => Then(_fame[b.Slot].CompareTo(_fame[a.Slot]), a, b));
                    break;
            }
        }

        /// <summary>Makes any comparison TOTAL: ties fall back to the estimate, then to the player id.</summary>
        private int Then(int primary, Match a, Match b)
        {
            if (primary != 0)
                return primary;

            int byEstimate = b.Overall.CompareTo(a.Overall);
            if (byEstimate != 0)
                return byEstimate;

            return _players[a.Slot].Id.CompareTo(_players[b.Slot].Id);
        }

        private readonly struct Match
        {
            public readonly int Slot;
            public readonly int Knowledge;
            public readonly int Scouted;
            public readonly int Overall;
            public readonly int Potential;

            public Match(int slot, int knowledge, int scouted, int overall, int potential)
            {
                Slot = slot;
                Knowledge = knowledge;
                Scouted = scouted;
                Overall = overall;
                Potential = potential;
            }
        }
    }
}
