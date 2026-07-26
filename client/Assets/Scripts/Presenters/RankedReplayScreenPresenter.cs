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
    /// Ranked replay viewer (Phase 9.2): downloads the stored full MatchReport for a ranked fixture and
    /// renders it with the existing <see cref="MatchRenderer"/> — read-only (the server already resolved the
    /// match and serves byte-identical bytes to everyone). Mirrors <see cref="OnlineReplayScreenPresenter"/>
    /// (the private-league one) but talks to <see cref="RankedApiService"/> and is addressed by fixture id
    /// via <see cref="RankedReplayTarget"/>. Reuses the same dumb <see cref="OnlineReplayView"/> chrome + two
    /// fixed contrasting kit colours.
    /// </summary>
    public sealed class RankedReplayScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly RankedReplayTarget _target;
        private readonly OnlineReplayView _view;

        private static readonly Color HomeColor = UiKit.Accent;
        private static readonly Color AwayColor = UiKit.Danger;

        private MatchRenderer _renderer;
        private int _homeClubId;
        private int _homeGoals;
        private int _awayGoals;
        private float _speed = 1f;

        public VisualElement View => _view.Root;

        public RankedReplayScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked, RankedReplayTarget target)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
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
            if (string.IsNullOrEmpty(_target.FixtureId))
            {
                _view.ShowStatus(_loc.Tr("ranked.error.not_found"));
                return;
            }

            _view.ShowStatus(_loc.Tr("ranked.loading"));
            var result = await _ranked.GetReplayReportAsync(_target.FixtureId);
            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)));
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
            _view.PitchContainer.Insert(0, _renderer);

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
            if (_renderer.parent != null) _renderer.RemoveFromHierarchy();
            _renderer = null;
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
