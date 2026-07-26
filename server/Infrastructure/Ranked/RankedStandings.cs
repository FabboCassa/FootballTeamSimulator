using Fts.Application.Ranked;
using Fts.Infrastructure.Persistence.Entities;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// The ranked league table (pure, no I/O) — shared by the season engine (which serves it to the client) and
/// the season end (which needs the same final order to decide titles, promotions and relegations). One
/// implementation so the table a coach sees and the table the ladder rewards can never disagree.
///
/// Ordering: points, then goal difference, then goals scored, then club name (ordinal) as a stable,
/// locale-independent tie-break.
/// </summary>
public static class RankedStandings
{
    public static IReadOnlyList<RankedStandingDto> Compute(
        IReadOnlyList<Club> clubs,
        IReadOnlyList<RankedFixture> fixtures,
        int pointsForWin,
        int pointsForDraw,
        int? youExternalId = null)
    {
        var table = clubs.ToDictionary(c => c.Id, c => new Row { ExternalId = c.ExternalId, Name = c.Name });

        foreach (var f in fixtures)
        {
            if (!f.IsPlayed) continue;
            if (!table.TryGetValue(f.HomeClubId, out var home) || !table.TryGetValue(f.AwayClubId, out var away))
                continue;

            home.Played++; away.Played++;
            home.GoalsFor += f.HomeGoals; home.GoalsAgainst += f.AwayGoals;
            away.GoalsFor += f.AwayGoals; away.GoalsAgainst += f.HomeGoals;

            if (f.HomeGoals > f.AwayGoals) { home.Won++; away.Lost++; home.Points += pointsForWin; }
            else if (f.HomeGoals < f.AwayGoals) { away.Won++; home.Lost++; away.Points += pointsForWin; }
            else { home.Drawn++; away.Drawn++; home.Points += pointsForDraw; away.Points += pointsForDraw; }
        }

        return table.Values
            .OrderByDescending(r => r.Points)
            .ThenByDescending(r => r.GoalsFor - r.GoalsAgainst)
            .ThenByDescending(r => r.GoalsFor)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new RankedStandingDto(
                ClubExternalId: r.ExternalId,
                ClubName: r.Name,
                Played: r.Played,
                Won: r.Won,
                Drawn: r.Drawn,
                Lost: r.Lost,
                GoalsFor: r.GoalsFor,
                GoalsAgainst: r.GoalsAgainst,
                GoalDifference: r.GoalsFor - r.GoalsAgainst,
                Points: r.Points,
                IsYou: youExternalId == r.ExternalId))
            .ToList();
    }

    private sealed class Row
    {
        public int ExternalId;
        public string Name = string.Empty;
        public int Played, Won, Drawn, Lost, GoalsFor, GoalsAgainst, Points;
    }
}
