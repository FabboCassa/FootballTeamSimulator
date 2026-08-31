using Cysharp.Threading.Tasks;
using Fts.MatchView;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using Sim.Core.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Online replay viewer (task 8.3b): downloads the stored full MatchReport for a league fixture and
    /// renders it with the existing <see cref="MatchRenderer"/> — the same top-down playback as the SP
    /// watched match, but read-only (no pause/intervention: the server already resolved the match, and
    /// every member is served byte-identical bytes so the replay is the same for everyone). Two fixed,
    /// contrasting kit colours (real club colours would need the world identity, deferred). Lives in the
    /// App scope, opened from the Season screen via <see cref="SeasonReplayTarget"/>.
    /// </summary>
    public sealed class OnlineReplayScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly SeasonReplayTarget _target;
        private readonly OnlineReplayView _view;

        private static readonly Color HomeColor = UiKit.Accent;
        private static readonly Color AwayColor = UiKit.Danger;

        private MatchRenderer _renderer;
        private int _homeClubId;
        private int _homeGoals;
        private int _awayGoals;
        private float _speed = 1f;

        public VisualElement View => _view.Root;

        public OnlineReplayScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues, SeasonReplayTarget target)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _target = target;
            _view = new OnlineReplayView(loc.Tr);
        }

        public void Enter()
        {
            _view.SpeedClicked += OnSpeed;
            _view.SkipClicked += OnSkip;
            _view.CloseClicked += OnClose;

            UpdateScore();
            _view.SetClock(_loc.Tr("match.clock", 0));
            LoadAsync().Forget();
        }

        public void Exit()
        {
            DetachRenderer();
            _view.SpeedClicked -= OnSpeed;
            _view.SkipClicked -= OnSkip;
            _view.CloseClicked -= OnClose;
        }

        private async UniTaskVoid LoadAsync()
        {
            if (string.IsNullOrEmpty(_target.LeagueId) || string.IsNullOrEmpty(_target.FixtureId))
            {
                _view.ShowStatus(_loc.Tr("leagues.error.not_found"));
                return;
            }

            _view.ShowStatus(_loc.Tr("leagues.loading"));
            var result = await _leagues.GetReplayReportAsync(_target.LeagueId, _target.FixtureId);
            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)));
                return;
            }

            _view.ShowStatus(string.Empty);
            BuildRenderer(result.Value);
        }

        private void BuildRenderer(MatchReport report)
        {
            DetachRenderer();

            _homeClubId = report.HomeClubId;
            _homeGoals = 0;
            _awayGoals = 0;
            UpdateScore();

            _renderer = new MatchRenderer(report, HomeColor, AwayColor);
            _renderer.MinuteChanged += OnMinuteChanged;
            _renderer.EventReached += OnEventReached;
            _renderer.Finished += OnFinished;
            _renderer.ActionReached += OnActionReached;
            _view.PitchContainer.Insert(0, _renderer);
            _view.ClearActions();

            _renderer.SetSpeed(_speed);
            _view.SetActiveSpeed(_speed);
            _renderer.Play();
        }

        private void DetachRenderer()
        {
            if (_renderer == null) return;
            _renderer.Stop();
            _renderer.MinuteChanged -= OnMinuteChanged;
            _renderer.EventReached -= OnEventReached;
            _renderer.Finished -= OnFinished;
            _renderer.ActionReached -= OnActionReached;
            if (_renderer.parent != null) _renderer.RemoveFromHierarchy();
            _renderer = null;
        }

        /// <summary>
        /// Running commentary (task 13.1). A replay arrives as a bare MatchReport with no
        /// squads attached, so players are named by the shirt numbers the stream carries.
        /// </summary>
        private void OnActionReached(BallAction action)
        {
            if (_speed > 1f && !MatchCommentary.IsMajor(action.Kind))
                return; // at 2x/4x a line per touch is a blur; keep the moments that matter

            _view.PushAction(MatchCommentary.Describe(action, _loc.Tr, NameOfSlot));
        }

        private string NameOfSlot(bool home, int slot)
        {
            if (_renderer == null) return string.Empty;
            int[] shirts = home ? _renderer.HomeShirts : _renderer.AwayShirts;
            return slot >= 0 && slot < shirts.Length ? "#" + shirts[slot] : string.Empty;
        }

        private void OnSpeed(float speed)
        {
            _speed = speed;
            _renderer?.SetSpeed(speed);
            _view.SetActiveSpeed(speed);
        }

        private void OnSkip() => _renderer?.Skip();

        private void OnClose() => _navigator.Pop();

        private void OnMinuteChanged(int minute) => _view.SetClock(_loc.Tr("match.clock", minute));

        private void OnEventReached(MatchEvent e)
        {
            if (e.Type == MatchEventType.Goal)
            {
                if (e.ClubId == _homeClubId) _homeGoals++; else _awayGoals++;
                UpdateScore();
                string scorer = e.ClubId == _homeClubId ? _target.HomeName : _target.AwayName;
                _view.ShowToast(_loc.Tr("replay.goal", e.Minute, scorer));
            }
        }

        private void OnFinished() => _view.SetClock(_loc.Tr("match.clock", 90));

        private void UpdateScore() =>
            _view.SetScore(_loc.Tr("match.score", _target.HomeName ?? "?", _homeGoals, _awayGoals, _target.AwayName ?? "?"));
    }
}
