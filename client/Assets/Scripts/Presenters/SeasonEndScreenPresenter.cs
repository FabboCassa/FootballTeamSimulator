using System.Collections.Generic;
using System.Text;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Career;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Season-end summary (task 2.7), pushed right after the rollover ran.
    /// Continue pops back to the Hub, already on day 1 of the new season.
    /// </summary>
    public sealed class SeasonEndScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly SeasonService _seasonService;
        private readonly ILocalizationService _loc;
        private readonly SeasonEndView _view;

        public VisualElement View => _view.Root;

        public SeasonEndScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            SeasonService seasonService,
            ILocalizationService loc)
        {
            _navigator = navigator;
            _career = career;
            _seasonService = seasonService;
            _loc = loc;
            _view = new SeasonEndView(loc.Tr);
        }

        public void Enter()
        {
            _view.ContinueClicked += OnContinue;

            RolloverResult result = _seasonService.LastRollover;
            if (result == null)
                return;

            _view.SetTitle(_loc.Tr("season_end.title", result.EndedYear));
            _view.SetChampion(_loc.Tr("season_end.champion", ClubName(result.ChampionClubId)));
            _view.SetPromoted(_loc.Tr("season_end.promoted", JoinNames(result.PromotedClubIds)));
            _view.SetRelegated(_loc.Tr("season_end.relegated", JoinNames(result.RelegatedClubIds)));
        }

        public void Exit()
        {
            _view.ContinueClicked -= OnContinue;
        }

        private void OnContinue() => _navigator.Pop();

        private string ClubName(int clubId) => _career.FindClub(clubId)?.Name ?? $"Club {clubId}";

        private string JoinNames(List<int> clubIds)
        {
            var sb = new StringBuilder();
            foreach (int id in clubIds)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(ClubName(id));
            }

            return sb.ToString();
        }
    }
}
