using System.Collections.Generic;
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
    /// The ranked home — since Phase 9.4 the DAILY DIGEST (the ≤10-minute loop lives here). One
    /// <c>GET /ranked/today</c> fills the whole screen: where you stand, how the last match went, who you play
    /// next and when, whether your inputs are ready, the market picture, and a short prioritised to-do list
    /// whose rows jump straight to the screen that clears them. "Confirm matchday" closes an uneventful day in
    /// one tap (the server seeds/repairs the inputs and stamps the round).
    ///
    /// Still the entry point for a coach who has never joined (enrol) and the hub for the other ranked
    /// screens. Every failure maps to a localized line via <see cref="RankedErrorFormat"/>.
    /// </summary>
    public sealed class RankedHomeScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly RankedHomeView _view;

        private bool _busy;
        private RankedTodayDto _today;

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
            _view.ConfirmClicked += OnConfirm;
            _view.SeasonClicked += OnSeason;
            _view.LineupClicked += OnLineup;
            _view.TrainingClicked += OnTraining;
            _view.MarketClicked += OnMarket;
            _view.LeaderboardClicked += OnLeaderboard;
            _view.AutoEnrolClicked += OnAutoEnrol;
            _view.RefreshClicked += OnRefresh;
            _view.TodoClicked += OnTodo;
            _view.FillDevClicked += OnFillDev;
            _view.BackClicked += OnBack;
            _view.SetDevToolsVisible(DevFlags.OnlineTestTools);
            LoadAsync().Forget();
        }

        public void Exit()
        {
            _view.EnrolClicked -= OnEnrol;
            _view.ConfirmClicked -= OnConfirm;
            _view.SeasonClicked -= OnSeason;
            _view.LineupClicked -= OnLineup;
            _view.TrainingClicked -= OnTraining;
            _view.MarketClicked -= OnMarket;
            _view.LeaderboardClicked -= OnLeaderboard;
            _view.AutoEnrolClicked -= OnAutoEnrol;
            _view.RefreshClicked -= OnRefresh;
            _view.TodoClicked -= OnTodo;
            _view.FillDevClicked -= OnFillDev;
            _view.BackClicked -= OnBack;
        }

        /// <summary>Coming back from a ranked screen re-reads the digest, so an offer you just answered or a
        /// lineup you just saved is reflected without a manual refresh.</summary>
        public void Reveal() => LoadAsync().Forget();

        // --- loading ---------------------------------------------------------------------------------

        private async UniTask LoadAsync()
        {
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.loading"), isError: false);

            var result = await _ranked.GetTodayAsync();

            _view.SetBusy(false);
            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)), isError: true);
                return;
            }

            _view.ClearStatus();
            Render(result.Value);
        }

        private void Render(RankedTodayDto d)
        {
            _today = d;

            if (d == null || !d.enrolled)
            {
                _view.SetSubtitle(_loc.Tr("ranked.subtitle"));
                _view.SetInfo(_loc.Tr("ranked.not_enrolled"));
                _view.SetToday(false, null, null, null, null);
                _view.SetTodo(false, null, string.Empty);
                _view.SetConfirm(false, string.Empty);
                _view.SetInputs(false, string.Empty, false, string.Empty);
                _view.SetMarket(false, string.Empty, false, string.Empty, string.Empty);
                _view.SetEnrolVisible(true);
                _view.SetActionsVisible(false);
                _view.SetAutoEnrol(false, string.Empty);
                return;
            }

            _view.SetEnrolVisible(false);
            _view.SetActionsVisible(true);
            _view.SetAutoEnrol(true, _loc.Tr(d.autoEnrol ? "ranked.auto_enrol_on" : "ranked.auto_enrol_off"));
            _view.SetSubtitle(Subtitle(d));

            if (!d.inSeason)
            {
                // Enrolled, waiting for the cohort to fill (or between seats after a promotion/relegation).
                _view.SetInfo(_loc.Tr("ranked.today.waiting"));
                _view.SetToday(false, null, null, null, null);
                _view.SetTodo(false, null, string.Empty);
                _view.SetConfirm(false, string.Empty);
                _view.SetInputs(false, string.Empty, false, string.Empty);
                _view.SetMarket(false, string.Empty, false, string.Empty, string.Empty);
                return;
            }

            _view.SetInfo(string.Empty);
            RenderToday(d);
            RenderTodo(d);
            RenderInputs(d);
            RenderMarket(d);
        }

        private string Subtitle(RankedTodayDto d)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(d.groupName))
                sb.Append(_loc.Tr("ranked.group", d.groupName, d.tier ?? 0));
            if (!string.IsNullOrEmpty(d.clubName))
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(d.clubName);
            }
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(_loc.Tr("ranked.rating", d.rating));
            return sb.ToString();
        }

        private void RenderToday(RankedTodayDto d)
        {
            string position = d.yourPosition is int p
                ? _loc.Tr("ranked.today.position", p, d.yourPoints ?? 0, d.roundsPlayed, d.totalRounds)
                : string.Empty;

            string last = d.lastResult == null
                ? string.Empty
                : _loc.Tr(ResultKey(d.lastResult), d.lastResult.opponentClubName,
                    d.lastResult.goalsFor, d.lastResult.goalsAgainst);

            string next = string.Empty, countdown = string.Empty;
            if (d.nextMatch != null)
            {
                next = _loc.Tr(d.nextMatch.youAreHome ? "ranked.today.next_home" : "ranked.today.next_away",
                    d.nextMatch.opponentClubName, d.nextMatch.round);
                countdown = d.nextMatch.secondsToKickoff > 0
                    ? _loc.Tr("ranked.today.kickoff_in", Countdown(d.nextMatch.secondsToKickoff))
                    : _loc.Tr("ranked.today.kickoff_now");
            }
            else if (d.seasonComplete)
            {
                next = _loc.Tr("ranked.today.season_over");
            }

            _view.SetToday(true, position, last, next, countdown);
        }

        private static string ResultKey(RankedTodayLastResultDto r) =>
            r.goalsFor > r.goalsAgainst ? "ranked.today.last_win"
            : r.goalsFor < r.goalsAgainst ? "ranked.today.last_loss"
            : "ranked.today.last_draw";

        private void RenderTodo(RankedTodayDto d)
        {
            var rows = new List<RankedTodoRowVm>();
            if (d.todo != null)
            {
                foreach (RankedTodoDto t in d.todo)
                    rows.Add(new RankedTodoRowVm
                    {
                        Kind = t.kind,
                        Label = TodoLabel(t),
                        ActionLabel = _loc.Tr("ranked.today.go"),
                    });
            }

            _view.SetTodo(true, rows, _loc.Tr("ranked.today.all_done"));
            // The one-tap confirm is offered whenever the matchday still needs a nod.
            _view.SetConfirm(!d.lineupConfirmed && !d.seasonComplete && d.nextRound.HasValue,
                _loc.Tr("ranked.today.confirm"));
        }

        private string TodoLabel(RankedTodoDto t) => t.kind switch
        {
            (int)RankedTodoKind.Enrol => _loc.Tr("ranked.todo.enrol"),
            (int)RankedTodoKind.ConfirmMatchday => _loc.Tr("ranked.todo.confirm_matchday"),
            (int)RankedTodoKind.RespondOffer => _loc.Tr("ranked.todo.respond_offer", t.count),
            (int)RankedTodoKind.MarketWindow => _loc.Tr("ranked.todo.market_window", t.count),
            (int)RankedTodoKind.SeasonSummary => _loc.Tr("ranked.todo.season_summary"),
            _ => string.Empty,
        };

        private void RenderInputs(RankedTodayDto d)
        {
            string lineup = d.lineupConfirmed
                ? _loc.Tr("ranked.today.lineup_confirmed")
                : d.lineupReady
                    ? _loc.Tr("ranked.today.lineup_ready")
                    : _loc.Tr("ranked.today.lineup_missing");

            string training = d.trainingTeamFocus is int focus
                ? _loc.Tr("ranked.today.training", _loc.Tr("training.team." + FocusName(focus)))
                : _loc.Tr("ranked.today.training_none");

            _view.SetInputs(true, lineup, d.lineupConfirmed, training);
        }

        /// <summary>The Sim.Core TeamTrainingFocus name for the loc key (mirrors the enum's order).</summary>
        private static string FocusName(int focus) => focus switch
        {
            1 => "attacking",
            2 => "defending",
            3 => "physical",
            4 => "technical",
            5 => "tactical",
            _ => "balanced",
        };

        private void RenderMarket(RankedTodayDto d)
        {
            bool open = d.marketWindow != null;
            string window = open ? _loc.Tr("ranked.today.market_open") : _loc.Tr("ranked.today.market_shut");
            string budget = _loc.Tr("ranked.today.budget", MoneyFormat.Short(d.budget));

            var sb = new StringBuilder();
            if (d.incomingOffers > 0) sb.Append(_loc.Tr("ranked.today.offers_in", d.incomingOffers));
            if (d.outgoingOffers > 0)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(_loc.Tr("ranked.today.offers_out", d.outgoingOffers));
            }
            if (open && d.openLots > 0)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(_loc.Tr("ranked.today.lots", d.openLots, d.lotsYouLead));
            }

            _view.SetMarket(true, window, open, budget, sb.ToString());
        }

        // --- actions ---------------------------------------------------------------------------------

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
            await LoadAsync();
        }

        private void OnConfirm() => ConfirmAsync().Forget();

        private async UniTaskVoid ConfirmAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.today.confirming"), isError: false);

            var result = await _ranked.ConfirmMatchdayAsync();

            _busy = false;
            _view.SetBusy(false);
            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)), isError: true);
                return;
            }

            _view.ShowStatus(_loc.Tr("ranked.today.confirmed"), isError: false);
            Render(result.Value);
        }

        /// <summary>A to-do row's button opens the screen that clears it (or performs it, for the two that
        /// are a single action).</summary>
        private void OnTodo(int kind)
        {
            switch (kind)
            {
                case (int)RankedTodoKind.Enrol: OnEnrol(); break;
                case (int)RankedTodoKind.ConfirmMatchday: OnConfirm(); break;
                case (int)RankedTodoKind.RespondOffer:
                case (int)RankedTodoKind.MarketWindow: OnMarket(); break;
                case (int)RankedTodoKind.SeasonSummary: OnSeason(); break;
            }
        }

        private void OnSeason() => _navigator.Push<RankedSeasonScreenPresenter>();

        private void OnLineup() => _navigator.Push<RankedLineupScreenPresenter>();

        private void OnTraining() => _navigator.Push<RankedTrainingScreenPresenter>();

        private void OnMarket() => _navigator.Push<RankedMarketScreenPresenter>();

        private void OnLeaderboard() => _navigator.Push<RankedLeaderboardScreenPresenter>();

        private void OnRefresh() => LoadAsync().Forget();

        private void OnAutoEnrol() => AutoEnrolAsync().Forget();

        private async UniTaskVoid AutoEnrolAsync()
        {
            if (_busy || _today == null) return;
            _busy = true;
            _view.SetBusy(true);

            var result = await _ranked.SetAutoEnrolAsync(!_today.autoEnrol);

            _busy = false;
            _view.SetBusy(false);
            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)), isError: true);
                return;
            }
            await LoadAsync();
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

        /// <summary>A compact "2g 4h" / "3h 12m" / "45m" countdown to the next kickoff.</summary>
        private string Countdown(int seconds)
        {
            int days = seconds / 86400;
            int hours = seconds % 86400 / 3600;
            int minutes = seconds % 3600 / 60;
            if (days > 0) return _loc.Tr("ranked.today.dur_dh", days, hours);
            if (hours > 0) return _loc.Tr("ranked.today.dur_hm", hours, minutes);
            return _loc.Tr("ranked.today.dur_m", minutes);
        }
    }
}
