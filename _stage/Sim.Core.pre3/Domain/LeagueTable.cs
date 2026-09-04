using System.Collections.Generic;
using Sim.Core.Config;

namespace Sim.Core.Domain
{
    public sealed class LeagueTableRow
    {
        public int ClubId { get; set; }
        public int Played { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public int Points { get; set; }

        public int GoalDifference => GoalsFor - GoalsAgainst;
    }

    /// <summary>
    /// Standings from played fixtures. Tiebreakers: points, goal difference,
    /// goals for, club id (last one keeps the order total and deterministic).
    /// </summary>
    public static class LeagueTable
    {
        public static List<LeagueTableRow> Compute(League league, Season season, SeasonBalance? config = null)
        {
            SeasonBalance cfg = config ?? new SeasonBalance();

            var rowsById = new Dictionary<int, LeagueTableRow>(league.Clubs.Count);
            foreach (Club club in league.Clubs)
                rowsById[club.Id] = new LeagueTableRow { ClubId = club.Id };

            foreach (Fixture fixture in season.Fixtures)
            {
                if (!fixture.Played) continue;

                // The season holds every division's fixtures; count only this league's.
                if (!rowsById.TryGetValue(fixture.HomeClubId, out LeagueTableRow home) ||
                    !rowsById.TryGetValue(fixture.AwayClubId, out LeagueTableRow away))
                    continue;

                home.Played++;
                away.Played++;
                home.GoalsFor += fixture.HomeGoals;
                home.GoalsAgainst += fixture.AwayGoals;
                away.GoalsFor += fixture.AwayGoals;
                away.GoalsAgainst += fixture.HomeGoals;

                if (fixture.HomeGoals > fixture.AwayGoals)
                {
                    home.Wins++;
                    away.Losses++;
                    home.Points += cfg.PointsForWin;
                }
                else if (fixture.HomeGoals < fixture.AwayGoals)
                {
                    away.Wins++;
                    home.Losses++;
                    away.Points += cfg.PointsForWin;
                }
                else
                {
                    home.Draws++;
                    away.Draws++;
                    home.Points += cfg.PointsForDraw;
                    away.Points += cfg.PointsForDraw;
                }
            }

            var rows = new List<LeagueTableRow>(rowsById.Values);
            rows.Sort(CompareRows);
            return rows;
        }

        private static int CompareRows(LeagueTableRow a, LeagueTableRow b)
        {
            if (a.Points != b.Points) return b.Points.CompareTo(a.Points);
            if (a.GoalDifference != b.GoalDifference) return b.GoalDifference.CompareTo(a.GoalDifference);
            if (a.GoalsFor != b.GoalsFor) return b.GoalsFor.CompareTo(a.GoalsFor);
            return a.ClubId.CompareTo(b.ClubId);
        }
    }
}
