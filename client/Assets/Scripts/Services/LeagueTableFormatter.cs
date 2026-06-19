using System.Text;
using Sim.Core.Domain;

namespace Fts.Services
{
    /// <summary>
    /// Console-friendly league table dump, used to verify standings until the
    /// League screen arrives (task 2.5).
    /// </summary>
    public static class LeagueTableFormatter
    {
        public static string Format(League league, Season season)
        {
            var rows = LeagueTable.Compute(league, season);
            var sb = new StringBuilder();

            sb.AppendLine($"{league.Name} — day {season.CurrentDay}");
            sb.AppendLine("Pos Club                     P   W  D  L   GF  GA  GD  Pts");

            int position = 1;
            foreach (LeagueTableRow row in rows)
            {
                Club club = league.FindClub(row.ClubId);
                string name = club != null ? club.Name : $"Club {row.ClubId}";
                if (name.Length > 24) name = name.Substring(0, 24);

                sb.AppendLine(
                    $"{position,2}. {name,-24} {row.Played,2}  {row.Wins,2} {row.Draws,2} {row.Losses,2}  " +
                    $"{row.GoalsFor,3} {row.GoalsAgainst,3} {row.GoalDifference,3}  {row.Points,3}");
                position++;
            }

            return sb.ToString();
        }
    }
}
