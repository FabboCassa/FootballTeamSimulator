using System.Collections.Generic;
using System.Globalization;
using Fts.Services.Persistence;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Scouting;

namespace Fts.Services
{
    /// <summary>
    /// Host driver for the scouting network (tasks 5.4b and 11.2). Owns the live Sim.Core stores —
    /// <see cref="KnowledgeStore"/>, <see cref="ScoutingAssignmentBook"/>,
    /// <see cref="AreaKnowledgeStore"/> and <see cref="ScoutingReportBook"/> — rehydrated from the
    /// saved career and written back so they persist.
    ///
    /// What happens each week (driven by LocalClock on the training cadence):
    ///   • the whole world scouts as it always did — the user club follows its NAMED targets, every
    ///     other club the default policy — so knowledge keeps growing everywhere, and
    ///   • the user club's AREA briefs run: each scout deepens what he has already found, up to the
    ///     ceiling his brief's breadth allows, and files a few new names that match his filters.
    ///
    /// The manager never browses the world's database: he chooses WHO goes WHERE and WHAT to look
    /// for, and reads what comes back. Lives in the Game scope. Never touches the engine — golden
    /// masters stay safe.
    /// </summary>
    public sealed class ScoutingService
    {
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly ScoutingBalance _cfg = new BalanceConfig().Scouting;
        private readonly ScoutingProgressor _progressor;
        private readonly KnowledgeStore _knowledge = new KnowledgeStore();
        private readonly ScoutingAssignmentBook _assignments = new ScoutingAssignmentBook();
        private readonly AreaKnowledgeStore _areaKnowledge = new AreaKnowledgeStore();
        private readonly ScoutingReportBook _reports = new ScoutingReportBook();

        public ScoutingService(CareerState career, ISaveRepository saveRepository)
        {
            _career = career;
            _saveRepository = saveRepository;
            _progressor = new ScoutingProgressor(_cfg);
            Rehydrate();
        }

        // ------------------------------------------------------------------ reading a player

        /// <summary>The user club's current knowledge of a player (0 = unscouted).</summary>
        public int KnowledgeOf(int playerId) => _knowledge.Get(_career.UserClubId, playerId);

        /// <summary>True if the user's scouts are watching this player BY NAME (a direct assignment).</summary>
        public bool IsWatching(int playerId) => _assignments.IsWatching(_career.UserClubId, playerId);

        /// <summary>
        /// A full scouted read of the player as the user club currently knows him — through the eyes
        /// of the scout who is actually responsible for him, so his judging attributes show up in how
        /// tight the bands are. A player nobody is on reads at the neutral department quality, which
        /// is exactly the pre-11.2 number.
        /// </summary>
        public PlayerScoutReport Report(Player player)
            => ScoutingModel.Report(player, KnowledgeOf(player.Id), _career.Seed, _career.UserClubId,
                                    _cfg, QualityFor(player.Id));

        /// <summary>The full knowledge scale (for percentages in the UI).</summary>
        public int MaxKnowledge => _cfg.MaxKnowledge;

        /// <summary>Knowledge of a player as a 0..100 percentage.</summary>
        public int KnowledgePercentOf(int playerId)
            => _cfg.MaxKnowledge > 0 ? KnowledgeOf(playerId) * 100 / _cfg.MaxKnowledge : 0;

        // ------------------------------------------------------------------ the department

        /// <summary>The club's scouts (empty only if the club somehow has no scouting facility).</summary>
        public IReadOnlyList<Scout> Scouts()
        {
            Club club = _career.GetUserClub();
            return club?.Scouts ?? (IReadOnlyList<Scout>)System.Array.Empty<Scout>();
        }

        /// <summary>The user club's effective scout level (best scout, or the base level).</summary>
        public int ScoutLevel()
        {
            Club userClub = _career.GetUserClub();
            return userClub != null ? _progressor.ClubScoutLevel(userClub) : _cfg.BaseClubScoutLevel;
        }

        /// <summary>How many briefs the user can run at once — one per scout, at least one.</summary>
        public int WatchCapacity()
        {
            int scouts = Scouts().Count;
            return scouts > 0 ? scouts : 1;
        }

        /// <summary>How many scouts are currently out in the field.</summary>
        public int WatchCount() => _assignments.CountFor(_career.UserClubId);

        /// <summary>Whether any scout is free to take a new brief.</summary>
        public bool HasFreeSlot() => WatchCount() < WatchCapacity();

        /// <summary>What a given scout is doing, or null if he is at home.</summary>
        public ScoutingAssignment BriefOf(int scoutId) => _assignments.AssignmentOfScout(_career.UserClubId, scoutId);

        /// <summary>Every brief the department is running.</summary>
        public IReadOnlyList<ScoutingAssignment> Briefs() => _assignments.AssignmentsFor(_career.UserClubId);

        /// <summary>The first scout with nothing to do, or 0 when they are all out.</summary>
        public int FirstFreeScoutId()
        {
            foreach (Scout scout in Scouts())
            {
                if (_assignments.AssignmentOfScout(_career.UserClubId, scout.Id) == null)
                    return scout.Id;
            }

            return 0;
        }

        /// <summary>
        /// Sends a scout somewhere with a brief. Sending a scout who is already out recalls him
        /// first (one man, one place). Saves. Returns false only when the scout does not exist.
        /// </summary>
        public bool SendScout(int scoutId, ScoutingArea area, ScoutingFilters filters)
        {
            if (scoutId <= 0 || area == null || FindScout(scoutId) == null)
                return false;

            _assignments.AddAssignment(_career.UserClubId, new ScoutingAssignment
            {
                ScoutId = scoutId,
                Area = area.Clone(),
                Filters = filters != null ? filters.Clone() : new ScoutingFilters()
            });

            Persist();
            return true;
        }

        /// <summary>Calls a scout home. His reports and everything he learned are kept. Saves.</summary>
        public void RecallScout(int scoutId)
        {
            if (_assignments.RemoveAssignment(_career.UserClubId, scoutId))
                Persist();
        }

        // ------------------------------------------------------------------ named targets (task 5.4b)

        /// <summary>
        /// Puts a named player under observation — the precise, expensive option kept from 5.4b.
        /// Takes the first free scout. Saves. Returns true on success.
        /// </summary>
        public bool Watch(int playerId)
        {
            if (IsWatching(playerId) || !HasFreeSlot())
                return false;

            int scoutId = FirstFreeScoutId();
            _assignments.AddAssignment(_career.UserClubId, new ScoutingAssignment
            {
                ScoutId = scoutId,
                Area = ScoutingArea.ForPlayer(playerId),
                Filters = new ScoutingFilters()
            });

            Persist();
            return true;
        }

        /// <summary>Stops watching a named player (knowledge already gained is kept). Saves.</summary>
        public void Unwatch(int playerId)
        {
            _assignments.Unassign(_career.UserClubId, playerId);
            Persist();
        }

        // ------------------------------------------------------------------ the reports

        /// <summary>The shortlist the scouts have brought back, oldest first.</summary>
        public IReadOnlyList<ScoutReportEntry> Reports() => _reports.For(_career.UserClubId);

        /// <summary>How many names one brief has produced.</summary>
        public int ReportCountOfArea(string areaKey) => _reports.CountOfArea(_career.UserClubId, areaKey);

        /// <summary>Room left on one brief's shortlist before it stops taking new names.</summary>
        public int ReportRoomOfArea(string areaKey)
        {
            int room = _cfg.MaxReportsPerArea - ReportCountOfArea(areaKey);
            return room > 0 ? room : 0;
        }

        /// <summary>Drops a name from the shortlist, freeing a slot for the scout to fill. Saves.</summary>
        public void DismissReport(int playerId)
        {
            _reports.Remove(_career.UserClubId, playerId);
            Persist();
        }

        /// <summary>How well the club knows an area, as a 0..100 percentage.</summary>
        public int AreaKnowledgePercent(string areaKey)
        {
            int max = _cfg.MaxAreaKnowledge > 0 ? _cfg.MaxAreaKnowledge : 1;
            return _areaKnowledge.Get(_career.UserClubId, areaKey) * 100 / max;
        }

        /// <summary>The ceiling this brief can currently read a player to, as a 0..100 percentage.</summary>
        public int PrecisionCeilingPercent(ScoutingArea area)
        {
            if (area == null || _cfg.MaxKnowledge <= 0)
                return 0;

            int cap = ScoutingModel.KnowledgeCap(
                area.Kind, _areaKnowledge.Get(_career.UserClubId, area.Key), _cfg);
            return cap * 100 / _cfg.MaxKnowledge;
        }

        /// <summary>How many players an area holds at all — so an empty result reads as "nobody here matches".</summary>
        public int AreaPlayerCount(ScoutingArea area) => ScoutingDiscovery.AreaPlayerCount(_career.World, area);

        // ------------------------------------------------------------------ the weekly tick

        /// <summary>
        /// Advances the whole world's scouting by one week and the user's area briefs with it, then
        /// writes everything back onto the career so the next save persists it. Called by LocalClock
        /// once per training/scouting week. Returns how many NEW names were filed, so the caller can
        /// tell the player something arrived. Caller is responsible for saving the career afterwards.
        /// </summary>
        public int EvolveWeek(int careerWeek)
        {
            _progressor.EvolveWeek(_career.Leagues, _knowledge, _assignments);

            int filed = 0;
            Club userClub = _career.GetUserClub();
            if (userClub != null)
            {
                filed = _progressor.EvolveAreaWeek(
                    _career.World, userClub, _assignments, _knowledge, _areaKnowledge, _reports,
                    _career.Seed, careerWeek);
            }

            WriteBack();
            return filed;
        }

        // ------------------------------------------------------------------ persistence

        private Scout FindScout(int scoutId)
        {
            foreach (Scout scout in Scouts())
            {
                if (scout.Id == scoutId)
                    return scout;
            }

            return null;
        }

        /// <summary>
        /// The scout responsible for a player: the one watching him by name, or the one whose brief
        /// found him. Nobody ⇒ the neutral department, i.e. exactly the pre-11.2 read.
        /// </summary>
        private ScoutQuality QualityFor(int playerId)
        {
            foreach (ScoutingAssignment brief in _assignments.AssignmentsFor(_career.UserClubId))
            {
                if (brief.Area.Kind == ScoutingAreaKind.Player && brief.Area.PlayerId == playerId)
                    return ScoutQuality.Of(FindScout(brief.ScoutId), _cfg);
            }

            foreach (ScoutReportEntry entry in _reports.For(_career.UserClubId))
            {
                if (entry.PlayerId == playerId)
                    return ScoutQuality.Of(FindScout(entry.ScoutId), _cfg);
            }

            return ScoutQuality.Neutral;
        }

        private void Persist()
        {
            WriteBack();
            _saveRepository.Save(_career);
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

            // A manager who has changed club inherits his NEW employer's department, not the briefs
            // and shortlist of the club he left. Per-player knowledge is keyed by club and stays.
            bool sameDepartment = _career.ScoutDepartmentClubId == 0
                                  || _career.ScoutDepartmentClubId == _career.UserClubId;

            if (sameDepartment && _career.ScoutBriefs != null && _career.ScoutBriefs.Count > 0)
            {
                foreach (ScoutingAssignment brief in _career.ScoutBriefs)
                {
                    if (brief != null && brief.Area != null)
                        _assignments.ImportAssignment(_career.UserClubId, brief);
                }
            }
            else if (sameDepartment && _career.ScoutAssignments != null)
            {
                // A pre-11.2 save: its flat watch list becomes one Player brief per name, each given
                // a scout of the department so the new screen can show who is on whom.
                int slot = 0;
                IReadOnlyList<Scout> scouts = Scouts();
                foreach (int playerId in _career.ScoutAssignments)
                {
                    int scoutId = slot < scouts.Count ? scouts[slot].Id : 0;
                    _assignments.ImportAssignment(_career.UserClubId, new ScoutingAssignment
                    {
                        ScoutId = scoutId,
                        Area = ScoutingArea.ForPlayer(playerId),
                        Filters = new ScoutingFilters()
                    });
                    slot++;
                }
            }

            if (sameDepartment && _career.ScoutAreaKnowledge != null)
            {
                foreach (var kv in _career.ScoutAreaKnowledge)
                    _areaKnowledge.Import(_career.UserClubId, kv.Key, kv.Value);
            }

            if (sameDepartment && _career.ScoutReports != null)
            {
                foreach (ScoutReportEntry entry in _career.ScoutReports)
                {
                    if (entry != null)
                        _reports.Import(_career.UserClubId, entry);
                }
            }

            WriteBack();
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

            _career.ScoutDepartmentClubId = _career.UserClubId;

            _career.ScoutBriefs.Clear();
            _career.ScoutAssignments.Clear();
            foreach (ScoutingAssignment brief in _assignments.AssignmentsFor(_career.UserClubId))
            {
                _career.ScoutBriefs.Add(brief);
                if (brief.Area.Kind == ScoutingAreaKind.Player)
                    _career.ScoutAssignments.Add(brief.Area.PlayerId);
            }

            _career.ScoutAreaKnowledge.Clear();
            foreach ((int clubId, string areaKey, int knowledge) in _areaKnowledge.Export())
            {
                if (clubId == _career.UserClubId)
                    _career.ScoutAreaKnowledge[areaKey] = knowledge;
            }

            _career.ScoutReports.Clear();
            foreach (ScoutReportEntry entry in _reports.For(_career.UserClubId))
                _career.ScoutReports.Add(entry);
        }
    }
}
