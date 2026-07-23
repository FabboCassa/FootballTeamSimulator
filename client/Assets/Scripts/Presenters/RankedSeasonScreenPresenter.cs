using System;
using System.Collections.Generic;
using System.Threading;
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
    /// The ranked season screen (Phase 9.2): the caller's schedule + standings + matchday/market banner,
    /// refreshed on a light poll while open (the ladder runs on a server clock, so a matchday can resolve
    /// while the screen is up). Read-only in this increment; submitting a lineup + watching a replay land in
    /// the next client increment.
    /// </summary>
    public sealed class RankedSeasonScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly RankedSeasonView _view;

        private CancellationTokenSource _cts;
        private bool _busy;

        public VisualElement View => _view.Root;

        public RankedSeasonScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
            _view = new RankedSeasonView(loc.Tr);
        }

        public void Enter()
        {
            _view.RefreshClicked += OnRefresh;
            _view.AdvanceDevClicked += OnAdvanceDev;
            _view.BackClicked += OnBack;
            _view.SetDevToolsVisible(DevFlags.OnlineTestTools);
            _cts = new CancellationTokenSource();
            PollAsync(_cts.Token).Forget();
        }

        public void Exit()
        {
            _view.RefreshClicked -= OnRefresh;
            _view.AdvanceDevClicked -= OnAdvanceDev;
            _view.BackClicked -= OnBack;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public void Reveal() => LoadAsync().Forget();

        private async UniTaskVoid PollAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await LoadAsync();
                await UniTask.Delay(TimeSpan.FromSeconds(3), cancellationToken: ct).SuppressCancellationThrow();
            }
        }

        private void OnRefresh() => LoadAsync().Forget();

        private async UniTask LoadAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);

            var result = await _ranked.GetSeasonAsync();

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

        private void Render(RankedSeasonDto season)
        {
            if (season == null || !season.inSeason || season.state == null)
            {
                _view.SetTitle(_loc.Tr("ranked.title"));
                _view.SetBanner(_loc.Tr("ranked.not_in_season"));
                _view.SetWindowBanner(string.Empty, false);
                _view.SetStandings(null);
                _view.SetSchedule(null);
                return;
            }

            var st = season.state;
            _view.SetTitle(st.groupName ?? _loc.Tr("ranked.title"));

            string banner = _loc.Tr("ranked.matchday", st.roundsPlayed, st.totalRounds);
            if (st.seasonComplete)
                banner = _loc.Tr("ranked.season_complete");
            else if (st.nextRound.HasValue && !string.IsNullOrEmpty(st.nextKickoffUtc))
                banner += "  ·  " + _loc.Tr("ranked.next_kickoff", FormatTime(st.nextKickoffUtc));
            _view.SetBanner(banner);

            bool windowOpen = st.currentWindow != null && st.currentWindow.isOpen;
            _view.SetWindowBanner(_loc.Tr("ranked.window_open"), windowOpen);

            RenderStandings(season.standings);
            RenderSchedule(season.fixtures);
        }

        private void RenderStandings(List<RankedStandingDto> standings)
        {
            var rows = new List<RankedSeasonView.StandingRow>();
            if (standings != null)
            {
                for (int i = 0; i < standings.Count; i++)
                {
                    var s = standings[i];
                    string text = _loc.Tr("ranked.standing_row",
                        i + 1, s.clubName, s.played, s.won, s.drawn, s.lost, Signed(s.goalDifference), s.points);
                    rows.Add(new RankedSeasonView.StandingRow(text, s.isYou));
                }
            }
            _view.SetStandings(rows);
        }

        private void RenderSchedule(List<RankedFixtureDto> fixtures)
        {
            var lines = new List<RankedSeasonView.ScheduleLine>();
            if (fixtures != null)
            {
                int lastRound = -1;
                foreach (var f in fixtures)
                {
                    if (f.round != lastRound)
                    {
                        lastRound = f.round;
                        lines.Add(new RankedSeasonView.ScheduleLine(_loc.Tr("ranked.round", f.round), isHeader: true));
                    }
                    string text = f.played
                        ? _loc.Tr("ranked.fixture_played", f.homeClubName, f.homeGoals, f.awayGoals, f.awayClubName)
                        : _loc.Tr("ranked.fixture_scheduled", f.homeClubName, f.awayClubName);
                    lines.Add(new RankedSeasonView.ScheduleLine(text, isHeader: false));
                }
            }
            _view.SetSchedule(lines);
        }

        // Dev-only: fill the placement cohort with bots FIRST (a no-op once the season is under way), then
        // advance the ranked calendar one tick (start seasons / resolve matchdays / settle), then refresh —
        // so a solo human can start + watch the season without a second account or waiting on the real clock.
        private void OnAdvanceDev() => AdvanceDevAsync().Forget();

        private async UniTaskVoid AdvanceDevAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.dev_filling"), isError: false);
            await _ranked.FillDevAsync();   // fills the forming placement group (0 bots if already full/started)
            await _ranked.TickDevAsync();   // then advance the calendar
            _busy = false;
            _view.SetBusy(false);
            _view.ClearStatus();
            await LoadAsync();
        }

        private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();

        /// <summary>Best-effort local time from the server's ISO instant; falls back to the raw string.</summary>
        private static string FormatTime(string isoUtc)
        {
            return DateTime.TryParse(
                isoUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("g")
                : isoUtc;
        }

        private void OnBack() => _navigator.Pop();
    }
}
