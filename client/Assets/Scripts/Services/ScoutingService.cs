using System.Globalization;
using Fts.Services.Persistence;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Scouting;

namespace Fts.Services
{
    /// <summary>
    /// Host driver for the scouting / knowledge layer (task 5.4b). Owns the live Sim.Core
    /// <see cref="KnowledgeStore"/> and <see cref="ScoutingAssignmentBook"/>, rehydrated from
    /// the saved career and written back so they persist. The whole world scouts each week
    /// (<see cref="EvolveWeek"/>, driven by LocalClock on the weekly cadence): the user club
    /// follows its explicit assignments, every other club the default policy. The UI reads
    /// scouted reports from here (wide ranges for the unscouted, narrowing toward the truth).
    /// Lives in the Game scope. Never touches the engine — golden masters stay safe.
    /// </summary>
    public sealed class ScoutingService
    {
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly ScoutingBalance _cfg = new BalanceConfig().Scouting;
        private readonly ScoutingProgressor _progressor;
        private readonly KnowledgeStore _knowledge = new KnowledgeStore();
        private readonly ScoutingAssignmentBook _assignments = new ScoutingAssignmentBook();

        public ScoutingService(CareerState career, ISaveRepository saveRepository)
        {
            _career = career;
            _saveRepository = saveRepository;
            _progressor = new ScoutingProgressor(_cfg);
            Rehydrate();
        }

        /// <summary>The user club's current knowledge of a player (0 = unscouted).</summary>
        public int KnowledgeOf(int playerId) => _knowledge.Get(_career.UserClubId, playerId);

        /// <summary>True if the user's scouts are actively watching this player.</summary>
        public bool IsWatching(int playerId) => _assignments.IsWatching(_career.UserClubId, playerId);

        /// <summary>A full scouted read of the player as the user club currently knows him.</summary>
        public PlayerScoutReport Report(Player player)
            => ScoutingModel.Report(player, KnowledgeOf(player.Id), _career.Seed, _career.UserClubId, _cfg);

        /// <summary>The user club's effective scout level (best scout, or the base level).</summary>
        public int ScoutLevel()
        {
            Club userClub = _career.GetUserClub();
            return userClub != null ? _progressor.ClubScoutLevel(userClub) : _cfg.BaseClubScoutLevel;
        }

        /// <summary>How many players the user can watch at once (one per scout, at least one).</summary>
        public int WatchCapacity()
        {
            Club userClub = _career.GetUserClub();
            int scouts = userClub?.Scouts?.Count ?? 0;
            return scouts > 0 ? scouts : 1;
        }

        /// <summary>How many players the user is currently watching.</summary>
        public int WatchCount() => _assignments.For(_career.UserClubId).Count;

        /// <summary>Whether there is room to watch another player.</summary>
        public bool HasFreeSlot() => WatchCount() < WatchCapacity();

        /// <summary>The full knowledge scale (for percentages in the UI).</summary>
        public int MaxKnowledge => _cfg.MaxKnowledge;

        /// <summary>Starts watching a player if there is a free scout slot. Saves. Returns true on success.</summary>
        public bool Watch(int playerId)
        {
            if (IsWatching(playerId) || !HasFreeSlot())
                return false;

            _assignments.Assign(_career.UserClubId, playerId);
            if (!_career.ScoutAssignments.Contains(playerId))
                _career.ScoutAssignments.Add(playerId);
            _saveRepository.Save(_career);
            return true;
        }

        /// <summary>Stops watching a player (knowledge already gained is kept). Saves.</summary>
        public void Unwatch(int playerId)
        {
            _assignments.Unassign(_career.UserClubId, playerId);
            _career.ScoutAssignments.Remove(playerId);
            _saveRepository.Save(_career);
        }

        /// <summary>
        /// Advances the whole world's scouting knowledge by one week and writes it back onto the
        /// career so the next save persists it. Called by LocalClock once per training/scouting
        /// week (same cadence). Caller is responsible for saving the career afterwards.
        /// </summary>
        public void EvolveWeek()
        {
            _progressor.EvolveWeek(_career.Leagues, _knowledge, _assignments);
            WriteBack();
        }

        private void Rehydrate()
        {
            if (_career.ScoutKnowledge != null)
            {
                foreach (var kv in _career.ScoutKnowledge)
                {
                    string[] parts = kv.Key.Split(':');
                    if (parts.Length == 2
                        && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clubId)
                        && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerId))
                    {
                        _knowledge.Import(clubId, playerId, kv.Value);
                    }
                }
            }

            if (_career.ScoutAssignments != null)
                foreach (int playerId in _career.ScoutAssignments)
                    _assignments.Assign(_career.UserClubId, playerId);
        }

        private void WriteBack()
        {
            _career.ScoutKnowledge.Clear();
            foreach ((int clubId, int playerId, int knowledge) in _knowledge.Export())
            {
                string key = clubId.ToString(CultureInfo.InvariantCulture) + ":" +
                             playerId.ToString(CultureInfo.InvariantCulture);
                _career.ScoutKnowledge[key] = knowledge;
            }
        }
    }
}
