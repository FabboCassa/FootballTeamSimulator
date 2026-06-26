using System.Collections.Generic;
using Fts.Services.Persistence;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Fts.Services
{
    /// <summary>
    /// Drives the user's coaching career (task 5.6): the board's season objective, board confidence
    /// (the sacking meter), reputation, the season-end evaluation (prize + Sim.Core whole-world
    /// carousel), and committing the user's decision — staying, or accepting an offer that moves him
    /// to a new club with its existing squad and budget. Lives in the Game scope.
    ///
    /// The heavy lifting is pure Sim.Core (<see cref="CoachCareerProgressor"/>); this host just owns
    /// the cadence, persistence and the user-facing decision. The season-end evaluation is idempotent
    /// (guarded by <see cref="CareerState.SeasonEvaluated"/>) so quitting on the decision screen and
    /// reloading rebuilds a read-only report rather than re-applying the deltas.
    /// </summary>
    public sealed class CareerService
    {
        private readonly CareerState _career;
        private readonly ISaveRepository _save;
        private readonly SeasonService _season;
        private readonly BalanceConfig _config = new BalanceConfig();
        private readonly CoachCareerProgressor _progressor;
        private readonly BoardModel _board;
        private readonly JobMarket _jobs;

        private CareerSeasonReport _lastReport;

        public CareerService(CareerState career, ISaveRepository save, SeasonService season)
        {
            _career = career;
            _save = save;
            _season = season;
            _progressor = new CoachCareerProgressor(_config);
            _board = new BoardModel(_config);
            _jobs = new JobMarket(_config);
        }

        // ------------------------------------------------------------------ read side (Career screen)

        public Coach UserCoach => _career.GetUserClub()?.Coach;
        public int Reputation => UserCoach?.Reputation ?? 0;
        public int Confidence => UserCoach?.BoardConfidence ?? 0;
        public int ObjectivePosition => UserCoach?.ObjectiveExpectedPosition ?? 0;
        public int ClubCount => _career.GetUserLeague()?.Clubs.Count ?? 0;
        public ObjectiveTier Objective => BoardObjective.TierFor(ObjectivePosition, ClubCount);
        public bool SeatIsWarned => _board.IsWarned(Confidence);
        public IReadOnlyList<CareerHistoryEntry> History => _career.CareerHistory;

        /// <summary>Confidence colour band: 0 = sacking range, 1 = warned, 2 = safe.</summary>
        public int ConfidenceBand
        {
            get
            {
                int c = Confidence;
                if (c < _config.Career.ConfidenceSackThreshold) return 0;
                if (c <= _config.Career.ConfidenceWarningThreshold) return 1;
                return 2;
            }
        }

        /// <summary>The user club's CURRENT 1-based position in its division (during or at the end of a season).</summary>
        public int CurrentPosition()
        {
            League league = _career.GetUserLeague();
            if (league == null) return 0;

            List<LeagueTableRow> table = LeagueTable.Compute(league, _career.Season);
            for (int i = 0; i < table.Count; i++)
                if (table[i].ClubId == _career.UserClubId)
                    return i + 1;
            return table.Count;
        }

        // ------------------------------------------------------------------ season-end flow

        /// <summary>
        /// Evaluates the finished season ONCE: awards prize money, runs the whole-world coach carousel
        /// (confidence/reputation moves, AI sackings &amp; hires) and produces the user report (warned /
        /// sacked + offers). On a reload after it already ran, rebuilds the same report read-only
        /// (no double-apply). Call only when the season is complete.
        /// </summary>
        public CareerSeasonReport EvaluateSeason()
        {
            if (!_career.SeasonEvaluated)
            {
                _season.AwardPrize();
                _lastReport = _progressor.EvolveSeasonEnd(_career.Leagues, _career.Season, _career.UserClubId);
                _career.SeasonEvaluated = true;
                _save.Save(_career);
            }
            else
            {
                _lastReport = BuildReadonlyReport();
            }

            return _lastReport;
        }

        public CareerSeasonReport LastReport => _lastReport ?? BuildReadonlyReport();

        /// <summary>
        /// Commits the user's season-end decision: <paramref name="chosenClubId"/> equal to his current
        /// club = stay; a different club = accept that offer and take it over (its squad and budget
        /// come with it). Records the finished season in the career history, performs the move, then
        /// runs the rollover and sets next season's objectives.
        /// </summary>
        public void Commit(int chosenClubId)
        {
            CareerSeasonReport report = _lastReport ?? BuildReadonlyReport();
            RecordHistory(report);

            if (chosenClubId != _career.UserClubId && _career.FindClub(chosenClubId) != null)
                MoveUser(chosenClubId);
            else if (report.UserSacked)
                UserCoach.BoardConfidence = _config.Career.NeutralConfidence; // reprieve: don't insta-sack next season

            _season.Rollover();                            // P/R + aging + new fixtures + counters + save
            _progressor.AssignObjectives(_career.Leagues); // objectives for the new division composition
            _career.SeasonEvaluated = false;
            _lastReport = null;
            _save.Save(_career);
        }

        // ------------------------------------------------------------------ internals

        private CareerSeasonReport BuildReadonlyReport()
        {
            var report = new CareerSeasonReport { Year = _career.Season.Year, UserClubId = _career.UserClubId };
            Coach coach = UserCoach;
            if (coach == null) return report;

            int actual = CurrentPosition();
            int expected = coach.ObjectiveExpectedPosition >= 1 ? coach.ObjectiveExpectedPosition : actual;

            report.UserFinishPosition = actual;
            report.UserExpectedPosition = expected;
            report.UserOutcome = BoardObjective.Classify(actual, expected, _config.Career);
            report.UserConfidence = coach.BoardConfidence;
            report.UserReputation = coach.Reputation;
            report.UserSacked = _board.ShouldSack(coach.BoardConfidence);
            report.UserWarned = !report.UserSacked && _board.IsWarned(coach.BoardConfidence);

            int offerFrom = report.UserSacked ? 0 : _career.UserClubId;
            report.UserOffers = _jobs.GenerateUserOffers(coach, offerFrom, _career.Leagues);
            return report;
        }

        private void RecordHistory(CareerSeasonReport report)
        {
            Club club = _career.GetUserClub();
            League league = _career.GetUserLeague();

            _career.CareerHistory.Add(new CareerHistoryEntry
            {
                Year = _career.Season.Year,
                ClubName = club?.Name ?? string.Empty,
                Division = league?.Division ?? 1,
                FinishPosition = report.UserFinishPosition,
                ExpectedPosition = report.UserExpectedPosition,
                Outcome = (int)report.UserOutcome,
                Reputation = report.UserReputation != 0 ? report.UserReputation : Reputation,
                Champion = report.UserFinishPosition == 1,
                Sacked = report.UserSacked
            });
        }

        private void MoveUser(int newClubId)
        {
            Club oldClub = _career.GetUserClub();
            League oldLeague = _career.GetUserLeague();
            Club newClub = _career.FindClub(newClubId);
            if (oldClub == null || newClub == null) return;

            Coach userCoach = oldClub.Coach;

            // The club he leaves gets fresh blood; the one he joins loses its AI coach.
            oldClub.Coach = _jobs.MintCoach(oldClub, oldLeague?.Division ?? 1, _career.Season.Year);

            userCoach.IsHuman = true;
            userCoach.BoardConfidence = _config.Career.NeutralConfidence; // fresh start, keeps reputation
            newClub.Coach = userCoach;
            _career.UserClubId = newClubId;

            // New squad → drop squad-specific user preferences (the coach's tactic knowledge and
            // his world watch lists travel with him; lineup/plan/training/listings do not).
            _career.UserLineup = null;
            _career.UserPlan = null;
            _career.UserTraining = null;
            _career.StartsSinceTraining.Clear();
            _career.UserMatchesSinceTraining = 0;
            _career.RestedSinceTraining.Clear();
            _career.TransferList.Clear();
            _career.IncomingOffers.Clear();

            // Objective for the new club is set by AssignObjectives after the rollover.
        }
    }
}
