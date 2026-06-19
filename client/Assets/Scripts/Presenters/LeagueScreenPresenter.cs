using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Domain;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// League screen (task 2.5): table, fixtures/results by round, top scorers.
    /// Pure read view over CareerState; data updates on every Enter/Reveal.
    /// </summary>
    public sealed class LeagueScreenPresenter : IScreenPresenter
    {
        private const int TopScorersShown = 20;

        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ILocalizationService _loc;
        private readonly LeagueView _view;

        private int _selectedRound = 1;
        private int _maxRound = 1;

        public VisualElement View => _view.Root;

        public LeagueScreenPresenter(ScreenNavigator navigator, CareerState career, ILocalizationService loc)
        {
            _navigator = navigator;
            _career = career;
            _loc = loc;
            _view = new LeagueView(loc.Tr);
        }

        public void Enter()
        {
            _view.TabClicked += OnTab;
            _view.PrevRoundClicked += OnPrevRound;
            _view.NextRoundClicked += OnNextRound;
            _view.BackClicked += OnBack;

            _selectedRound = DefaultRound();
            RefreshAll();
        }

        public void Exit()
        {
            _view.TabClicked -= OnTab;
            _view.PrevRoundClicked -= OnPrevRound;
            _view.NextRoundClicked -= OnNextRound;
            _view.BackClicked -= OnBack;
        }

        public void Reveal()
        {
            _selectedRound = DefaultRound();
            RefreshAll();
        }

        private void OnTab(int index) => _view.ShowTab(index);

        private void OnPrevRound()
        {
            if (_selectedRound > 1)
            {
                _selectedRound--;
                RefreshFixtures();
            }
        }

        private void OnNextRound()
        {
            if (_selectedRound < _maxRound)
            {
                _selectedRound++;
                RefreshFixtures();
            }
        }

        private void OnBack() => _navigator.Pop();

        private void RefreshAll()
        {
            _view.SetLeagueName(_career.GetUserLeague().Name);
            RefreshTable();
            RefreshFixtures();
            RefreshScorers();
        }

        private void RefreshTable()
        {
            League userLeague = _career.GetUserLeague();
            List<LeagueTableRow> table = LeagueTable.Compute(userLeague, _career.Season);

            var rows = new List<TableRowVm>(table.Count);
            for (int i = 0; i < table.Count; i++)
            {
                LeagueTableRow row = table[i];
                rows.Add(new TableRowVm
                {
                    Position = i + 1,
                    Club = userLeague.FindClub(row.ClubId)?.Name ?? $"Club {row.ClubId}",
                    Played = row.Played,
                    Wins = row.Wins,
                    Draws = row.Draws,
                    Losses = row.Losses,
                    GoalsFor = row.GoalsFor,
                    GoalsAgainst = row.GoalsAgainst,
                    GoalDifference = row.GoalDifference,
                    Points = row.Points,
                    IsUser = row.ClubId == _career.UserClubId
                });
            }

            _view.SetTable(rows);
        }

        private void RefreshFixtures()
        {
            League userLeague = _career.GetUserLeague();
            var rows = new List<FixtureRowVm>();
            int day = 0;

            foreach (Fixture f in _career.Season.Fixtures)
            {
                // Same round numbering across divisions: show the user's league only.
                if (f.Round != _selectedRound || userLeague.FindClub(f.HomeClubId) == null)
                    continue;

                day = f.Day;
                string home = ClubName(f.HomeClubId);
                string away = ClubName(f.AwayClubId);

                rows.Add(new FixtureRowVm
                {
                    Label = f.Played
                        ? _loc.Tr("league.fixture.result", home, f.HomeGoals, f.AwayGoals, away)
                        : _loc.Tr("league.fixture.vs", home, away),
                    IsUser = f.Involves(_career.UserClubId)
                });
            }

            _view.SetFixtures(_loc.Tr("league.round_label", _selectedRound, day), rows);
        }

        private void RefreshScorers()
        {
            League userLeague = _career.GetUserLeague();

            var tallies = new List<ScorerTally>();
            foreach (ScorerTally tally in _career.Season.Scorers)
            {
                if (userLeague.FindClub(tally.ClubId) != null)
                    tallies.Add(tally);
            }
            tallies.Sort((a, b) =>
            {
                if (a.Goals != b.Goals) return b.Goals.CompareTo(a.Goals);
                return a.PlayerId.CompareTo(b.PlayerId);
            });

            var rows = new List<ScorerRowVm>();
            for (int i = 0; i < tallies.Count && i < TopScorersShown; i++)
            {
                ScorerTally tally = tallies[i];
                string player = _career.FindPlayer(tally.PlayerId)?.FullName ?? $"Player {tally.PlayerId}";

                rows.Add(new ScorerRowVm
                {
                    Label = _loc.Tr("league.scorer_row", i + 1, player, ClubName(tally.ClubId), tally.Goals),
                    IsUser = tally.ClubId == _career.UserClubId
                });
            }

            _view.SetScorers(rows);
        }

        private int DefaultRound()
        {
            _maxRound = 1;
            int firstUnplayed = int.MaxValue;

            foreach (Fixture f in _career.Season.Fixtures)
            {
                if (f.Round > _maxRound) _maxRound = f.Round;
                if (!f.Played && f.Round < firstUnplayed) firstUnplayed = f.Round;
            }

            return firstUnplayed == int.MaxValue ? _maxRound : firstUnplayed;
        }

        private string ClubName(int clubId) => _career.FindClub(clubId)?.Name ?? $"Club {clubId}";
    }
}
