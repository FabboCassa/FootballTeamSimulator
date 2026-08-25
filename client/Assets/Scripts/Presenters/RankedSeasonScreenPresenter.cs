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
        private readonly RankedReplayTarget _replayTarget;
        private readonly RankedSeasonView _view;

        private CancellationTokenSource _cts;
        private bool _busy;

        /// <summary>The group whose season-end card is already on screen — so the 3s poll does not re-fetch
        /// the palmarès every tick while the group sits in its between-seasons break (Phase 9.3).</summary>
        private string _seasonEndGroup;
        private readonly Dictionary<string, (string home, string away)> _fixtureNames =
            new Dictionary<string, (string home, string away)>();

        public VisualElement View => _view.Root;

        public RankedSeasonScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked, RankedReplayTarget replayTarget)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
            _replayTarget = replayTarget;
            _view = new RankedSeasonView(loc.Tr);
        }

        public void Enter()
        {
            _view.RefreshClicked += OnRefresh;
            _view.LineupClicked += OnLineup;
            _view.MarketClicked += OnMarket;
            _view.AdvanceDevClicked += OnAdvanceDev;
            _view.FixtureSelected += OnFixtureSelected;
            _view.BackClicked += OnBack;
            _view.SetDevToolsVisible(DevFlags.OnlineTestTools);
            _cts = new CancellationTokenSource();
            PollAsync(_cts.Token).Forget();
        }

        public void Exit()
        {
            _view.RefreshClicked -= OnRefresh;
            _view.LineupClicked -= OnLineup;
            _view.MarketClicked -= OnMarket;
            _view.AdvanceDevClicked -= OnAdvanceDev;
            _view.FixtureSelected -= OnFixtureSelected;
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
                _view.SetSeasonEnd(string.Empty, string.Empty, false, false, false);
                _seasonEndGroup = null;
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

            // Season over → the group is in its between-seasons break: show what the season was worth.
            if (st.seasonComplete)
            {
                LoadSeasonEndAsync(st.groupName).Forget();
            }
            else
            {
                _view.SetSeasonEnd(string.Empty, string.Empty, false, false, false);
                _seasonEndGroup = null;
            }

            RenderStandings(season.standings);
            RenderSchedule(season.fixtures);
        }

        /// <summary>Builds the season-end card from the caller's palmarès: the server hands out the awards the
        /// moment the last matchday resolves, so the newest rows for this group ARE the season just played
        /// (finish, rating after, swing, and any promotion/relegation). Fetched once per completed season.</summary>
        private async UniTaskVoid LoadSeasonEndAsync(string groupName)
        {
            if (string.IsNullOrEmpty(groupName) || _seasonEndGroup == groupName) return;

            var result = await _ranked.GetPalmaresAsync();
            if (!result.Success || result.Value?.awards == null) return;

            // The highest season number recorded for this group = the season that just finished.
            int season = -1;
            foreach (var a in result.Value.awards)
                if (a.groupName == groupName && a.seasonNumber > season) season = a.seasonNumber;
            if (season < 0) return;

            RankedAwardDto played = null;
            bool champion = false, promoted = false, relegated = false;
            foreach (var a in result.Value.awards)
            {
                if (a.groupName != groupName || a.seasonNumber != season) continue;
                if (a.kind == (int)RankedAwardKind.SeasonPlayed) played = a;
                else if (a.kind == (int)RankedAwardKind.Champion || a.kind == (int)RankedAwardKind.TopFlightTitle)
                    champion = true;
                else if (a.kind == (int)RankedAwardKind.Promotion) promoted = true;
                else if (a.kind == (int)RankedAwardKind.Relegation) relegated = true;
            }
            if (played == null) return;

            string title = champion ? _loc.Tr("ranked.seasonend.champion")
                : promoted ? _loc.Tr("ranked.seasonend.promoted")
                : relegated ? _loc.Tr("ranked.seasonend.relegated")
                : _loc.Tr("ranked.seasonend.done");

            string detail = _loc.Tr("ranked.seasonend.detail",
                played.position, played.ratingAfter, Signed(played.ratingDelta))
                + "\n" + _loc.Tr("ranked.seasonend.break");

            _view.SetSeasonEnd(title, detail, true, champion || promoted, relegated);
            _seasonEndGroup = groupName;
        }

        private void RenderStandings(List<RankedStandingDto> standings)
        {
            var rows = new List<StandingRowVm>();
            if (standings != null)
            {
                for (int i = 0; i < standings.Count; i++)
                {
                    var s = standings[i];
                    rows.Add(new StandingRowVm
                    {
                        Pos = i + 1,
                        ClubName = s.clubName,
                        Played = s.played,
                        Won = s.won,
                        Drawn = s.drawn,
                        Lost = s.lost,
                        GoalsFor = s.goalsFor,
                        GoalsAgainst = s.goalsAgainst,
                        GoalDifference = s.goalDifference,
                        Points = s.points,
                        IsYours = s.isYou,
                    });
                }
            }
            _view.SetStandings(rows);
        }

        private void RenderSchedule(List<RankedFixtureDto> fixtures)
        {
            _fixtureNames.Clear();
            var groups = new List<FixtureGroupVm>();
            if (fixtures != null)
            {
                int lastRound = -1;
                List<SeasonFixtureRowVm> rows = null;
                foreach (var f in fixtures)
                {
                    if (f.round != lastRound || rows == null)
                    {
                        lastRound = f.round;
                        rows = new List<SeasonFixtureRowVm>();
                        groups.Add(new FixtureGroupVm { RoundLabel = _loc.Tr("ranked.round", f.round), Rows = rows });
                    }
                    // Played fixtures are tappable → the replay.
                    if (f.played) _fixtureNames[f.id] = (f.homeClubName, f.awayClubName);
                    rows.Add(new SeasonFixtureRowVm
                    {
                        FixtureId = f.id,
                        HomeName = f.homeClubName,
                        AwayName = f.awayClubName,
                        Played = f.played,
                        HomeGoals = f.homeGoals,
                        AwayGoals = f.awayGoals,
                        IsYours = f.isYours,
                        CanPlayLive = false, // the ladder resolves its matchdays on the server clock
                    });
                }
            }
            _view.SetSchedule(groups);
        }

        private void OnLineup() => _navigator.Push<RankedLineupScreenPresenter>();

        private void OnMarket() => _navigator.Push<RankedMarketScreenPresenter>();

        private void OnFixtureSelected(string fixtureId)
        {
            if (string.IsNullOrEmpty(fixtureId)) return;
            var (home, away) = _fixtureNames.TryGetValue(fixtureId, out var n) ? n : (string.Empty, string.Empty);
            _replayTarget.Set(fixtureId, home, away);
            _navigator.Push<RankedReplayScreenPresenter>();
        }

        // Dev-only: fill the placement cohort with bots FIRST (a no-op once the season is under way), then
        // FAST-FORWARD the ranked calendar — with the real 1-matchday-a-day spacing a single tick resolves
        // almost nothing, while the fast-forward time-travels the ladder, so a solo human can watch a whole
        // season, the seasonal reset and the season after it without a second account or a two-week wait.
        private void OnAdvanceDev() => AdvanceDevAsync().Forget();

        private async UniTaskVoid AdvanceDevAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.dev_filling"), isError: false);
            await _ranked.FillDevAsync();   // fills the forming placement group (0 bots if already full/started)
            await _ranked.FastForwardDevAsync(DevFastForwardMatchdays);
            _busy = false;
            _view.SetBusy(false);
            _view.ClearStatus();
            // The finished season may have been reset behind us → let the card rebuild for the new season.
            _seasonEndGroup = null;
            await LoadAsync();
        }

        /// <summary>How many matchdays one press of the dev fast-forward covers — enough to walk a season
        /// forward briskly without skipping past the between-seasons break in a single tap.</summary>
        private const int DevFastForwardMatchdays = 3;

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
