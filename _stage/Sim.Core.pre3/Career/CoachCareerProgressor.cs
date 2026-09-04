using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Career
{
    /// <summary>
    /// The whole-world coach carousel (task 5.6) — the career twin of the condition / development /
    /// finance progressors. Every club has a coach with a reputation and a board confidence; at
    /// season end the board judges each coach against his objective, confidence and reputation move,
    /// failing AI coaches are sacked and the freed benches reassigned, and the user is told whether
    /// his seat is warned or lost and which clubs are courting him.
    ///
    /// PURE and deterministic — integer math, NO RNG (carousel order is a total order on reputation
    /// then id). Opt-in: nothing in the match engine or SeasonProgressor calls it → golden masters
    /// and replays are unaffected.
    ///
    /// ORDERING CONTRACT (host):
    ///   1. play the season to completion;
    ///   2. call <see cref="EvolveSeasonEnd"/> on the FINISHED season (reads the final tables) — this
    ///      updates confidence/reputation, records each coach's finish, sacks &amp; rehires AI coaches,
    ///      and returns the user report (warned/sacked + offers);
    ///   3. run the normal <see cref="SeasonRollover"/> (promotion/relegation, aging, new fixtures);
    ///   4. call <see cref="AssignObjectives"/> on the new division composition to set every coach's
    ///      objective for the season about to start.
    /// </summary>
    public sealed class CoachCareerProgressor
    {
        private readonly BalanceConfig _cfg;
        private readonly CareerBalance _career;
        private readonly BoardModel _board;
        private readonly ReputationModel _rep;
        private readonly JobMarket _jobs;

        public CoachCareerProgressor(BalanceConfig? config = null)
        {
            _cfg = config ?? new BalanceConfig();
            _career = _cfg.Career;
            _board = new BoardModel(_cfg);
            _rep = new ReputationModel(_cfg);
            _jobs = new JobMarket(_cfg);
        }

        /// <summary>
        /// Seeds a fresh world at career creation: every coach's reputation from his club's stature,
        /// neutral board confidence, and an opening objective from squad strength (no prior season).
        /// Idempotent and deterministic. Does NOT touch <see cref="Coach.IsHuman"/> (the host marks
        /// the user's coach).
        /// </summary>
        public void SeedWorld(IReadOnlyList<League> leagues)
        {
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    club.Coach.Reputation = _rep.SeedReputation(club, league.Division);
                    club.Coach.BoardConfidence = _career.NeutralConfidence;
                    club.Coach.LastFinishPosition = 0;
                    _board.AssignObjective(club, league, prevFinishPosition: 0);
                }
            }
        }

        /// <summary>
        /// Sets every coach's objective for the season about to start from the CURRENT division
        /// composition and his last finish. Call after <see cref="SeasonRollover"/> so promoted /
        /// relegated clubs are judged against their new peers.
        /// </summary>
        public void AssignObjectives(IReadOnlyList<League> leagues)
        {
            foreach (League league in leagues)
                foreach (Club club in league.Clubs)
                    _board.AssignObjective(club, league, club.Coach.LastFinishPosition);
        }

        /// <summary>
        /// A mid-season running check: nudges every coach's confidence from his club's CURRENT table
        /// position vs his objective (no sackings here — the seat only heats up). Returns the user's
        /// confidence so the host can surface a warning. Optional; safe to call repeatedly.
        /// </summary>
        public int RunningCheck(IReadOnlyList<League> leagues, Season season, int userClubId)
        {
            int userConfidence = _career.NeutralConfidence;
            foreach (League league in leagues)
            {
                Dictionary<int, int> positions = Positions(league, season);
                foreach (Club club in league.Clubs)
                {
                    int pos = positions.TryGetValue(club.Id, out int p) ? p : league.Clubs.Count;
                    int expected = ExpectedOrFallback(club, league);
                    _board.ApplyConfidenceDelta(club.Coach, _board.RunningConfidenceDelta(pos, expected));
                    if (club.Id == userClubId) userConfidence = club.Coach.BoardConfidence;
                }
            }
            return userConfidence;
        }

        /// <summary>
        /// The season-end evaluation (call on the finished season, BEFORE rollover). Updates every
        /// coach's confidence + reputation + recorded finish, sacks failing AI coaches and reassigns
        /// the freed benches, and builds the user report (warned/sacked + job offers).
        /// </summary>
        public CareerSeasonReport EvolveSeasonEnd(IReadOnlyList<League> leagues, Season season, int userClubId)
        {
            var report = new CareerSeasonReport { Year = season.Year, UserClubId = userClubId };

            // 1) Judge every coach against his objective; move confidence + reputation; record the finish.
            foreach (League league in leagues)
            {
                Dictionary<int, int> positions = Positions(league, season);
                foreach (Club club in league.Clubs)
                {
                    int actual = positions.TryGetValue(club.Id, out int p) ? p : league.Clubs.Count;
                    int expected = ExpectedOrFallback(club, league);

                    _board.ApplyConfidenceDelta(club.Coach, _board.SeasonEndConfidenceDelta(actual, expected));
                    _rep.ApplySeasonEnd(club.Coach, actual, expected, league.Division);
                    club.Coach.LastFinishPosition = actual;

                    if (club.Id == userClubId)
                    {
                        report.UserFinishPosition = actual;
                        report.UserExpectedPosition = expected;
                        report.UserOutcome = BoardObjective.Classify(actual, expected, _career);
                    }
                }
            }

            // 2) AI sackings → vacancies + freed-coach pool (the user is handled separately).
            var vacancies = new List<(Club club, int division)>();
            var pool = new List<(Coach coach, int formerClubId)>();
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    if (club.Id == userClubId) continue;
                    if (club.Coach.IsHuman) continue; // safety: never auto-sack a human here
                    if (_board.ShouldSack(club.Coach.BoardConfidence))
                    {
                        report.SackedAiClubIds.Add(club.Id);
                        pool.Add((club.Coach, club.Id));
                        vacancies.Add((club, league.Division));
                    }
                }
            }

            // 3) Reassign the freed coaches (best to best); fresh coach where the pool ran dry.
            report.Hires = _jobs.FillVacancies(vacancies, pool, season.Year);

            // 4) The user: warned / sacked + offers.
            Coach? user = FindCoach(leagues, userClubId);
            if (user != null)
            {
                report.UserConfidence = user.BoardConfidence;
                report.UserReputation = user.Reputation;
                report.UserSacked = _board.ShouldSack(user.BoardConfidence);
                report.UserWarned = !report.UserSacked && _board.IsWarned(user.BoardConfidence);

                // Sacked → court him as a free agent (any qualifying club); employed → only better jobs.
                int offerFromClubId = report.UserSacked ? 0 : userClubId;
                report.UserOffers = _jobs.GenerateUserOffers(user, offerFromClubId, leagues);
            }

            return report;
        }

        // ----------------------------------------------------------------- internals

        private int ExpectedOrFallback(Club club, League league)
        {
            int expected = club.Coach.ObjectiveExpectedPosition;
            if (expected >= 1) return expected;
            return _board.ExpectedPosition(club, league, prevFinishPosition: 0);
        }

        private static Dictionary<int, int> Positions(League league, Season season)
        {
            List<LeagueTableRow> table = LeagueTable.Compute(league, season);
            var positions = new Dictionary<int, int>(table.Count);
            for (int i = 0; i < table.Count; i++)
                positions[table[i].ClubId] = i + 1;
            return positions;
        }

        private static Coach? FindCoach(IReadOnlyList<League> leagues, int clubId)
        {
            foreach (League league in leagues)
            {
                Club? club = league.FindClub(clubId);
                if (club != null) return club.Coach;
            }
            return null;
        }
    }
}
