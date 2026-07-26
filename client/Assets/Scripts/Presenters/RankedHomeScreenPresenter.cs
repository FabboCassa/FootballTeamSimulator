using System.Text;
using Cysharp.Threading.Tasks;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// The ranked home (Phase 9.2): shows the caller's ladder state and lets them join the ladder, jump to
    /// their season, and toggle auto re-enrolment. Reachable from the Main Menu only when signed in. Talks to
    /// <see cref="RankedApiService"/>; maps outcomes to localized status lines.
    /// </summary>
    public sealed class RankedHomeScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly RankedHomeView _view;

        private bool _busy;
        private RankedStateDto _state;

        public VisualElement View => _view.Root;

        public RankedHomeScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
            _view = new RankedHomeView(loc.Tr);
        }

        public void Enter()
        {
            _view.EnrolClicked += OnEnrol;
            _view.SeasonClicked += OnSeason;
            _view.LeaderboardClicked += OnLeaderboard;
            _view.AutoEnrolClicked += OnAutoEnrol;
            _view.FillDevClicked += OnFillDev;
            _view.BackClicked += OnBack;
            _view.SetDevToolsVisible(DevFlags.OnlineTestTools);
            LoadAsync().Forget();
        }

        public void Exit()
        {
            _view.EnrolClicked -= OnEnrol;
            _view.SeasonClicked -= OnSeason;
            _view.LeaderboardClicked -= OnLeaderboard;
            _view.AutoEnrolClicked -= OnAutoEnrol;
            _view.FillDevClicked -= OnFillDev;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => LoadAsync().Forget();

        private async UniTask LoadAsync()
        {
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.loading"), isError: false);

            var result = await _ranked.GetMineAsync();

            _view.SetBusy(false);
            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)), isError: true);
                return;
            }

            _view.ClearStatus();
            Render(result.Value);
        }

        private void Render(RankedStateDto s)
        {
            _state = s;
            if (s == null || !s.enrolled)
            {
                _view.SetInfo(_loc.Tr("ranked.not_enrolled"));
                _view.SetEnrolVisible(true);
                _view.SetSeasonVisible(false);
                _view.SetLeaderboardVisible(false);
                _view.SetAutoEnrol(false, string.Empty);
                return;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(s.worldName)) sb.AppendLine(_loc.Tr("ranked.world", s.worldName));
            if (!string.IsNullOrEmpty(s.groupName))
                sb.AppendLine(_loc.Tr("ranked.group", s.groupName, s.tier ?? 0));
            if (!string.IsNullOrEmpty(s.clubName)) sb.AppendLine(_loc.Tr("ranked.club", s.clubName));
            sb.AppendLine(_loc.Tr("ranked.rating", s.rating));
            sb.AppendLine(_loc.Tr("ranked.state", _loc.Tr(StatusKey(s.status))));
            if (s.placementPosition is int pos) sb.AppendLine(_loc.Tr("ranked.placement_pos", pos));
            _view.SetInfo(sb.ToString().TrimEnd());

            _view.SetEnrolVisible(false);
            _view.SetSeasonVisible(true);
            _view.SetLeaderboardVisible(true);
            _view.SetAutoEnrol(true, _loc.Tr(s.autoEnrol ? "ranked.auto_enrol_on" : "ranked.auto_enrol_off"));
        }

        private static string StatusKey(int status) => status switch
        {
            (int)RankedCoachStatus.Placed => "ranked.status.placed",
            (int)RankedCoachStatus.Retired => "ranked.status.retired",
            _ => "ranked.status.placement",
        };

        private void OnEnrol() => EnrolAsync().Forget();

        private async UniTaskVoid EnrolAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.enrolling"), isError: false);

            var result = await _ranked.EnrolAsync();

            _busy = false;
            _view.SetBusy(false);
            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)), isError: true);
                return;
            }
            _view.ClearStatus();
            Render(result.Value);
        }

        private void OnSeason() => _navigator.Push<RankedSeasonScreenPresenter>();

        // The global ladder + this coach's palmarès (Phase 9.3).
        private void OnLeaderboard() => _navigator.Push<RankedLeaderboardScreenPresenter>();

        private void OnAutoEnrol() => AutoEnrolAsync().Forget();

        private async UniTaskVoid AutoEnrolAsync()
        {
            if (_busy || _state == null) return;
            _busy = true;
            _view.SetBusy(true);

            var result = await _ranked.SetAutoEnrolAsync(!_state.autoEnrol);

            _busy = false;
            _view.SetBusy(false);
            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)), isError: true);
                return;
            }
            Render(result.Value);
        }

        // Dev-only: fill the placement group with bot coaches, then advance the calendar so the season
        // starts — so a solo human can test the ranked flow end-to-end.
        private void OnFillDev() => FillDevAsync().Forget();

        private async UniTaskVoid FillDevAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.dev_filling"), isError: false);

            bool filled = await _ranked.FillDevAsync();
            if (filled) await _ranked.TickDevAsync(); // start the now-full placement season

            _busy = false;
            _view.SetBusy(false);
            if (!filled)
            {
                _view.ShowStatus(_loc.Tr("ranked.error.server"), isError: true);
                return;
            }
            await LoadAsync();
        }

        private void OnBack() => _navigator.Pop();
    }
}
