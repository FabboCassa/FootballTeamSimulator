using System.Collections.Generic;

namespace Sim.Core.Domain
{
    /// <summary>
    /// The whole generated football world (task 11.1): every nation loaded for this save, at the
    /// level of detail the chosen scope asked for.
    ///
    /// Layout rules that the rest of the game depends on:
    ///   • Playable leagues' fixtures live in the CAREER season (the host's <see cref="Season"/>),
    ///     exactly as before 11.1, so the existing progressor/rollover path is untouched.
    ///   • Background leagues' fixtures live here, in <see cref="BackgroundSeason"/>, and are
    ///     resolved by <see cref="Career.QuickResultResolver"/> — they never reach the match engine.
    ///   • DataOnly leagues have no fixtures at all; their clubs exist only as scouting targets.
    ///
    /// The world is a pure data graph: same seed + same scope ⇒ byte-identical world.
    /// </summary>
    public sealed class World
    {
        /// <summary>
        /// Next id for a player created AFTER generation (a promoted club filling its squad up to
        /// full size). Lives above every generated block so it can never collide with one.
        /// </summary>
        public int NextGeneratedPlayerId { get; set; }

        /// <summary>Nations in registry order (never re-sorted: ids are derived from this order).</summary>
        public List<Nation> Nations { get; set; } = new List<Nation>();

        /// <summary>The scope this world was generated with. Immutable for the save.</summary>
        public WorldScope Scope { get; set; } = new WorldScope();

        /// <summary>
        /// Fixtures and results of every <see cref="LeagueDetailLevel.Background"/> league. Kept
        /// apart from the career season so the daily playable loop never walks tens of thousands
        /// of background fixtures.
        /// </summary>
        public Season BackgroundSeason { get; set; } = new Season();

        // Lazily-built lookup indexes. Private fields: neither Newtonsoft nor System.Text.Json
        // serialises them, so they never reach a save file.
        private Dictionary<int, Club>? _clubIndex;
        private Dictionary<int, League>? _clubLeagueIndex;
        private List<League>? _playableCache;
        private Dictionary<int, Player>? _playerIndex;
        private Dictionary<int, Club>? _playerClubIndex;

        /// <summary>Every league of every nation, nations in registry order and tiers top-first.</summary>
        public List<League> AllLeagues()
        {
            var leagues = new List<League>();
            foreach (Nation nation in Nations)
            {
                foreach (League league in nation.Leagues)
                    leagues.Add(league);
            }

            return leagues;
        }

        public List<League> LeaguesAt(LeagueDetailLevel level)
        {
            var leagues = new List<League>();
            foreach (Nation nation in Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    if (league.DetailLevel == level)
                        leagues.Add(league);
                }
            }

            return leagues;
        }

        /// <summary>
        /// The leagues the career actually plays — what the host feeds to SeasonProgressor, and what
        /// <c>CareerState.Leagues</c> projects. Cached: detail levels are fixed for the life of a save
        /// (the scope is immutable), and this is called on every day tick, so rebuilding the list each
        /// time would be pure waste. The cached list is the SAME instance every call, so callers may
        /// hold it; <see cref="InvalidateIndex"/> drops it.
        /// </summary>
        public List<League> PlayableLeagues() => _playableCache ??= LeaguesAt(LeagueDetailLevel.Playable);

        public Nation? FindNation(string code)
        {
            foreach (Nation nation in Nations)
            {
                if (nation.Code == code)
                    return nation;
            }

            return null;
        }

        public League? FindLeague(int leagueId)
        {
            foreach (Nation nation in Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    if (league.Id == leagueId)
                        return league;
                }
            }

            return null;
        }

        /// <summary>
        /// Indexed club lookup. A large world holds tens of thousands of clubs, so the linear
        /// scan the two-division code could afford is no longer acceptable (task 11.3 depends
        /// on this). Call <see cref="InvalidateIndex"/> after moving clubs between leagues.
        /// </summary>
        public Club? FindClub(int clubId)
        {
            BuildIndex();
            return _clubIndex!.TryGetValue(clubId, out Club? club) ? club : null;
        }

        /// <summary>
        /// Indexed player lookup across the WHOLE world, background and data-only leagues included.
        /// The index is built on first use and costs one dictionary entry per player, so it is only
        /// paid for by the screens that actually search the world (task 11.3's scouting search);
        /// nothing in the daily career loop touches it.
        /// </summary>
        public Player? FindPlayer(int playerId)
        {
            BuildPlayerIndex();
            return _playerIndex!.TryGetValue(playerId, out Player? found) ? found : null;
        }

        /// <summary>
        /// The club a player currently belongs to, anywhere in the world (task 11.2). Rides the same
        /// index as <see cref="FindPlayer"/> and is resolved LIVE rather than stored, so a scouting
        /// report on a player who has since been sold shows his new club rather than a stale one.
        /// </summary>
        public Club? ClubOfPlayer(int playerId)
        {
            BuildPlayerIndex();
            return _playerClubIndex!.TryGetValue(playerId, out Club? club) ? club : null;
        }

        private void BuildPlayerIndex()
        {
            if (_playerIndex != null && _playerClubIndex != null)
                return;

            var players = new Dictionary<int, Player>();
            var clubs = new Dictionary<int, Club>();
            foreach (Nation nation in Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    foreach (Club club in league.Clubs)
                    {
                        foreach (Player player in club.Squad.Players)
                        {
                            players[player.Id] = player;
                            clubs[player.Id] = club;
                        }
                    }
                }
            }

            _playerIndex = players;
            _playerClubIndex = clubs;
        }

        /// <summary>The league a club currently plays in (null if the club is unknown).</summary>
        public League? LeagueOf(int clubId)
        {
            BuildIndex();
            return _clubLeagueIndex!.TryGetValue(clubId, out League? league) ? league : null;
        }

        /// <summary>
        /// The nation a club belongs to (null if the club is unknown). Walked rather than indexed:
        /// it is used once per season by the rollover, never in a loop.
        /// </summary>
        public Nation? NationOf(int clubId)
        {
            foreach (Nation nation in Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    if (league.FindClub(clubId) != null)
                        return nation;
                }
            }

            return null;
        }

        /// <summary>Drops the cached lookup tables. Cheap; call it after promotion/relegation.</summary>
        public void InvalidateIndex()
        {
            _clubIndex = null;
            _clubLeagueIndex = null;
            _playableCache = null;
            _playerIndex = null;
            _playerClubIndex = null;
        }

        public int ClubCount()
        {
            int total = 0;
            foreach (Nation nation in Nations)
            {
                foreach (League league in nation.Leagues)
                    total += league.Clubs.Count;
            }

            return total;
        }

        public int PlayerCount()
        {
            int total = 0;
            foreach (Nation nation in Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    foreach (Club club in league.Clubs)
                        total += club.Squad.Players.Count;
                }
            }

            return total;
        }

        private void BuildIndex()
        {
            if (_clubIndex != null && _clubLeagueIndex != null)
                return;

            var clubs = new Dictionary<int, Club>();
            var leagues = new Dictionary<int, League>();

            foreach (Nation nation in Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    foreach (Club club in league.Clubs)
                    {
                        clubs[club.Id] = club;
                        leagues[club.Id] = league;
                    }
                }
            }

            _clubIndex = clubs;
            _clubLeagueIndex = leagues;
        }
    }
}
