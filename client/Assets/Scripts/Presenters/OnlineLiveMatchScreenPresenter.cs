using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fts.MatchView;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using Newtonsoft.Json;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Tactics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Live match control screen (task 8.6b). Both members open the same current-round fixture; when both
    /// are present the server kicks it off and the client renders the match with the existing
    /// <see cref="MatchRenderer"/>, synced to the shared kickoff time. A ~1s REST poll (WebGL-safe, like the
    /// 8.5b auctions) picks up the opponent's changes — when a new report arrives the renderer rebuilds from
    /// the current live minute, so a sub by the opponent shows in the remainder. "Modifica" opens the
    /// sub/instruction panel (reusing the SP <see cref="InMatchPanel"/>): the change is posted to /change,
    /// the server re-simulates deterministically from the fixture seed and returns the new state. Leaving
    /// the screen marks the caller absent — the match then plays out on the accumulated plan (graceful
    /// fallback). App scope; the fixture hand-off rides <see cref="SeasonReplayTarget"/>.
    /// </summary>
    public sealed class OnlineLiveMatchScreenPresenter : IScreenPresenter
    {
        private const int PollMillis = 1000;
        private const int MaxSubstitutions = 5;
        // 90' play over MatchRenderer.BaseSecondsAt1x (180s) at 1x → 2 real seconds per sim-minute; both
        // clients derive the same live minute from the shared kickoff, so change minutes stay monotonic.
        private const float LiveSecondsPerMinute = 2f;

        private static readonly Color HomeColor = UiKit.Accent;
        private static readonly Color AwayColor = UiKit.Danger;

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly LeagueSelection _selection;
        private readonly SeasonReplayTarget _target;
        private readonly LiveMatchView _view;
        private readonly InMatchPanel _panel;

        private string _leagueId;
        private string _fixtureId;
        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _panelOpen;

        private LiveMatchStateDto _state;
        private string _lastReportJson;
        private DateTime? _kickoff;

        private MatchRenderer _renderer;
        private int _homeClubId;
        private int _homeGoals;
        private int _awayGoals;
        private int _shownMinute;

        // My side (for control). The XI is client-tracked; the first change fixes it authoritatively.
        private LiveSide? _mySide;
        private int _myClubExternalId;
        private readonly Dictionary<int, LeaguePlayerDto> _squad = new Dictionary<int, LeaguePlayerDto>();
        private readonly List<Slot> _workingLineup = new List<Slot>();
        private Formation _formation = Formation.F433;
        private Mentality _mentality = Mentality.Balanced;
        private Pressing _pressing = Pressing.Medium;
        private Tempo _tempo = Tempo.Normal;
        private Width _width = Width.Normal;
        private int _subsUsed;
        private int _selectedSlot = -1;

        private sealed class Slot { public PositionRole Role; public int PlayerId; }

        public VisualElement View => _view.Root;

        public OnlineLiveMatchScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues,
            LeagueSelection selection, SeasonReplayTarget target)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _target = target;
            _view = new LiveMatchView(loc.Tr);
            _panel = new InMatchPanel(loc.Tr);
        }

        public void Enter()
        {
            _view.ModifyClicked += OnModify;
            _view.FinishClicked += OnFinish;
            _view.BackClicked += OnBack;
            _view.BotJoinClicked += OnBotJoin;
            _view.BotSubClicked += OnBotSub;
            _view.Root.Add(_panel.Root);
            _panel.SetVisible(false);
            WirePanel();

            _view.SetDevToolsVisible(DevFlags.OnlineTestTools);
            _view.SetModifyEnabled(false);
            _view.SetFinishEnabled(false);
            _view.SetClock(_loc.Tr("match.clock", 0));
            UpdateScore();

            _leagueId = _selection.LeagueId;
            _fixtureId = _target.FixtureId;
            _cts = new CancellationTokenSource();
            InitAsync(_cts.Token).Forget();
        }

        public void Exit()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            // Mark absent so the opponent's UI updates and the match falls back to the plan/AI.
            if (!string.IsNullOrEmpty(_leagueId) && !string.IsNullOrEmpty(_fixtureId))
                _leagues.LeaveLiveAsync(_leagueId, _fixtureId).Forget();

            DetachRenderer();
            _view.ModifyClicked -= OnModify;
            _view.FinishClicked -= OnFinish;
            _view.BackClicked -= OnBack;
            _view.BotJoinClicked -= OnBotJoin;
            _view.BotSubClicked -= OnBotSub;
            UnwirePanel();
        }

        // --- loading / polling ---------------------------------------------------------------------

        private async UniTaskVoid InitAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_leagueId) || string.IsNullOrEmpty(_fixtureId))
            {
                _view.ShowStatus(_loc.Tr("leagues.error.not_found"));
                return;
            }

            _view.ShowStatus(_loc.Tr("leagues.loading"));
            var open = await _leagues.OpenLiveAsync(_leagueId, _fixtureId);
            if (!open.Success)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(open.Error)));
                return;
            }

            _view.ShowStatus(string.Empty);
            await LoadSquadAsync(open.Value);
            Apply(open.Value, rebuild: true);
            PollAsync(ct).Forget();
        }

        private async UniTask LoadSquadAsync(LiveMatchStateDto state)
        {
            _mySide = state.yourSide.HasValue ? (LiveSide)state.yourSide.Value : (LiveSide?)null;
            if (!_mySide.HasValue) return;

            _myClubExternalId = _mySide.Value == LiveSide.Home ? state.homeClubExternalId : state.awayClubExternalId;

            var detail = await _leagues.GetAsync(_leagueId);
            if (!detail.Success) return;

            LeagueClubDto club = detail.Value.clubs.Find(c => c.externalId == _myClubExternalId);
            if (club == null || club.players == null) return;

            _squad.Clear();
            foreach (LeaguePlayerDto p in club.players) _squad[p.externalId] = p;

            // Seed a starting XI: the first 11 squad players in the default formation slots. The first
            // change sends this (with any subs) as the authoritative lineup for my side from that minute.
            _workingLineup.Clear();
            var roles = LineupSelector.DefaultFormation;
            for (int i = 0; i < 11 && i < club.players.Count; i++)
                _workingLineup.Add(new Slot { Role = roles[i], PlayerId = club.players[i].externalId });
        }

        private async UniTaskVoid PollAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                bool cancelled = await UniTask.Delay(
                    TimeSpan.FromMilliseconds(PollMillis), cancellationToken: ct).SuppressCancellationThrow();
                if (cancelled) return;
                if (_busy) continue;

                var result = await _leagues.GetLiveAsync(_leagueId, _fixtureId);
                if (result.Success) Apply(result.Value, rebuild: !_panelOpen);
            }
        }

        /// <summary>Apply a fresh state: update the banner/score/controls and, if a new report arrived and we
        /// are not mid-edit, (re)build the renderer seeked to the current live minute.</summary>
        private void Apply(LiveMatchStateDto state, bool rebuild)
        {
            _state = state;
            _homeClubId = state.homeClubExternalId;
            _kickoff = ParseUtc(state.kickoffUtc);

            var status = (LiveMatchStatus)state.status;
            _view.SetBanner(BannerFor(state, status));

            bool iControl = _mySide.HasValue && status == LiveMatchStatus.Live;
            _view.SetModifyEnabled(iControl && !_busy);
            // "End match" appears once the shared clock reaches full time (avoids finishing early).
            _view.SetFinishEnabled(iControl && LiveMinute() >= 90);

            bool reportChanged = state.reportJson != _lastReportJson;
            if (rebuild && status != LiveMatchStatus.Pending && !string.IsNullOrEmpty(state.reportJson) && reportChanged)
            {
                _lastReportJson = state.reportJson;
                MatchReport report = TryParseReport(state.reportJson);
                if (report != null) BuildRenderer(report, LiveMinute());
            }
        }

        // --- rendering -----------------------------------------------------------------------------

        private void BuildRenderer(MatchReport report, int seekMinute)
        {
            DetachRenderer();

            _renderer = new MatchRenderer(report, HomeColor, AwayColor);
            _renderer.MinuteChanged += OnMinuteChanged;
            _renderer.EventReached += OnEventReached;
            _renderer.Finished += OnFinished;
            _renderer.ActionReached += OnActionReached;
            _view.PitchContainer.Insert(0, _renderer); // behind the toast overlay
            _view.ClearActions();

            _renderer.SetSpeed(1f);
            if (seekMinute > 0) _renderer.SeekToMinute(seekMinute);

            RecomputeScoreUpTo(report, seekMinute);
            _shownMinute = seekMinute;
            UpdateScore();
            _view.SetClock(_loc.Tr("match.clock", seekMinute));

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
            _view.PushAction(MatchCommentary.Describe(action, _loc.Tr, NameOfSlot));
        }

        private string NameOfSlot(bool home, int slot)
        {
            if (_renderer == null) return string.Empty;
            int[] shirts = home ? _renderer.HomeShirts : _renderer.AwayShirts;
            return slot >= 0 && slot < shirts.Length ? "#" + shirts[slot] : string.Empty;
        }

        private void OnMinuteChanged(int minute)
        {
            _shownMinute = minute;
            _view.SetClock(_loc.Tr("match.clock", minute));
        }

        private void OnEventReached(MatchEvent e)
        {
            if (e.Type == MatchEventType.Goal)
            {
                if (e.ClubId == _homeClubId) _homeGoals++; else _awayGoals++;
                UpdateScore();
                string club = e.ClubId == _homeClubId ? HomeName() : AwayName();
                _view.ShowToast(_loc.Tr("replay.goal", e.Minute, club));
            }
        }

        private void OnFinished() => _view.SetClock(_loc.Tr("match.clock", 90));

        private void RecomputeScoreUpTo(MatchReport report, int minute)
        {
            _homeGoals = 0;
            _awayGoals = 0;
            if (report?.Events == null) return;
            foreach (MatchEvent e in report.Events)
            {
                if (e.Type != MatchEventType.Goal || e.Minute > minute) continue;
                if (e.ClubId == _homeClubId) _homeGoals++; else _awayGoals++;
            }
        }

        // --- modify (sub / instructions) -----------------------------------------------------------

        private void OnModify()
        {
            if (!_mySide.HasValue || _state == null || (LiveMatchStatus)_state.status != LiveMatchStatus.Live) return;
            _panelOpen = true;
            _renderer?.Stop();
            RefreshPanel();
            _panel.SetVisible(true);
        }

        private void OnResume()
        {
            _panelOpen = false;
            _panel.SetVisible(false);
            // Re-anchor to the current live minute (the opponent may have changed the report meanwhile).
            if (_state != null && !string.IsNullOrEmpty(_state.reportJson))
            {
                _lastReportJson = _state.reportJson;
                MatchReport report = TryParseReport(_state.reportJson);
                if (report != null) BuildRenderer(report, LiveMinute());
            }
        }

        private void OnApply() => ApplyChangeAsync().Forget();

        private async UniTaskVoid ApplyChangeAsync()
        {
            if (_busy || !_mySide.HasValue) return;
            _busy = true;
            _view.ShowStatus(_loc.Tr("live.status.sending"));

            int from = Mathf.Clamp(LiveMinute(), 1, 90);
            var body = new SubmitLiveChangeBody
            {
                fromMinute = from,
                lineup = BuildLineupPlan(),
                tactic = BuildTacticPlan(),
            };

            var result = await _leagues.SubmitLiveChangeAsync(_leagueId, _fixtureId, body);
            _busy = false;
            _panelOpen = false;
            _panel.SetVisible(false);

            if (result.Success)
            {
                _view.ShowStatus(string.Empty);
                _lastReportJson = result.Value.reportJson;
                Apply(result.Value, rebuild: false);
                MatchReport report = TryParseReport(result.Value.reportJson);
                if (report != null) BuildRenderer(report, LiveMinute());
            }
            else
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)));
            }
        }

        private void OnPitchClicked(int slotIndex)
        {
            _selectedSlot = slotIndex;
            RefreshPanel();
        }

        private void OnBenchClicked(int playerId)
        {
            if (_selectedSlot < 0 || _selectedSlot >= _workingLineup.Count || _subsUsed >= MaxSubstitutions) return;
            if (!_squad.ContainsKey(playerId)) return;

            _workingLineup[_selectedSlot].PlayerId = playerId;
            _subsUsed++;
            _selectedSlot = -1;
            RefreshPanel();
        }

        private void OnMentality() { _mentality = (Mentality)(((int)_mentality + 1) % 3); RefreshPanel(); }
        private void OnPressing() { _pressing = (Pressing)(((int)_pressing + 1) % 3); RefreshPanel(); }
        private void OnTempo() { _tempo = (Tempo)(((int)_tempo + 1) % 3); RefreshPanel(); }
        private void OnWidth() { _width = (Width)(((int)_width + 1) % 3); RefreshPanel(); }

        private void RefreshPanel()
        {
            _panel.SetTitle(_loc.Tr("inmatch.paused_at", LiveMinute()));
            _panel.SetSubsRemaining(_loc.Tr("inmatch.subs_remaining", MaxSubstitutions - _subsUsed));
            _panel.SetFamiliarity(string.Empty); // online tactics run at full familiarity (server, 8.4)

            var pitch = new List<InMatchRowVm>(_workingLineup.Count);
            var ids = new HashSet<int>();
            for (int i = 0; i < _workingLineup.Count; i++)
            {
                Slot slot = _workingLineup[i];
                ids.Add(slot.PlayerId);
                pitch.Add(new InMatchRowVm
                {
                    Id = i,
                    Label = $"{RoleAbbr(slot.Role)}  {PlayerName(slot.PlayerId)}  ({Overall(slot.PlayerId)})",
                    Selected = i == _selectedSlot,
                    Fitness = 100, // online condition is server-side; a neutral strip avoids a misleading value
                });
            }
            _panel.SetPitch(pitch);

            var bench = new List<InMatchRowVm>();
            foreach (KeyValuePair<int, LeaguePlayerDto> kv in _squad)
            {
                if (ids.Contains(kv.Key)) continue;
                bench.Add(new InMatchRowVm
                {
                    Id = kv.Key,
                    Label = $"{RoleAbbr((PositionRole)kv.Value.role)}  {kv.Value.name}  OVR {kv.Value.overall}",
                    Fitness = 100,
                });
            }
            _panel.SetBench(bench);

            _panel.SetMentality(_loc.Tr("tactics.label.mentality",
                _loc.Tr("tactics.mentality." + _mentality.ToString().ToLowerInvariant())));
            _panel.SetPressing(_loc.Tr("tactics.label.pressing",
                _loc.Tr("tactics.pressing." + _pressing.ToString().ToLowerInvariant())));
            _panel.SetTempo(_loc.Tr("tactics.label.tempo",
                _loc.Tr("tactics.tempo." + _tempo.ToString().ToLowerInvariant())));
            _panel.SetWidth(_loc.Tr("tactics.label.width",
                _loc.Tr("tactics.width." + _width.ToString().ToLowerInvariant())));
        }

        private LineupPlan BuildLineupPlan()
        {
            var plan = new LineupPlan { ClubId = _myClubExternalId };
            foreach (Slot s in _workingLineup)
                plan.Slots.Add(new LineupPlanSlot { Role = s.Role, PlayerId = s.PlayerId });
            return plan;
        }

        private TacticPlan BuildTacticPlan() =>
            TacticPlan.FromTactic(new Tactic(_formation, new TacticInstructions(_mentality, _pressing, _tempo, _width)));

        // --- finish / back -------------------------------------------------------------------------

        private void OnFinish() => FinishAsync().Forget();

        private async UniTaskVoid FinishAsync()
        {
            if (_busy || !_mySide.HasValue) return;
            _busy = true;
            _view.ShowStatus(_loc.Tr("live.status.finishing"));

            var result = await _leagues.FinishLiveAsync(_leagueId, _fixtureId);
            _busy = false;

            if (result.Success) _navigator.Pop();
            else _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)));
        }

        private void OnBack() => _navigator.Pop();

        // --- dev tooling (gated by DevFlags.OnlineTestTools) ---------------------------------------

        // Simulate the opponent: the fixture's bot member joins (→ Live once you're present).
        private void OnBotJoin() => BotAsync(sub: false).Forget();

        // Simulate the opponent making a substitution at the current live minute, so the ✅ is observable solo.
        private void OnBotSub() => BotAsync(sub: true).Forget();

        private async UniTaskVoid BotAsync(bool sub)
        {
            if (_busy || string.IsNullOrEmpty(_leagueId) || string.IsNullOrEmpty(_fixtureId)) return;
            _busy = true;
            _view.ShowStatus(_loc.Tr("live.status.bot"));

            var result = await _leagues.BotLiveAsync(_leagueId, _fixtureId, sub, LiveMinute());
            _busy = false;

            _view.ShowStatus(result.Success ? string.Empty : _loc.Tr(LeagueErrorFormat.Key(result.Error)));
            var refreshed = await _leagues.GetLiveAsync(_leagueId, _fixtureId);
            if (refreshed.Success) Apply(refreshed.Value, rebuild: !_panelOpen);
        }

        // --- panel glue ----------------------------------------------------------------------------

        private void WirePanel()
        {
            _panel.PitchClicked += OnPitchClicked;
            _panel.BenchClicked += OnBenchClicked;
            _panel.MentalityCycleClicked += OnMentality;
            _panel.PressingCycleClicked += OnPressing;
            _panel.TempoCycleClicked += OnTempo;
            _panel.WidthCycleClicked += OnWidth;
            _panel.ApplyClicked += OnApply;
            _panel.ResumeClicked += OnResume;
        }

        private void UnwirePanel()
        {
            _panel.PitchClicked -= OnPitchClicked;
            _panel.BenchClicked -= OnBenchClicked;
            _panel.MentalityCycleClicked -= OnMentality;
            _panel.PressingCycleClicked -= OnPressing;
            _panel.TempoCycleClicked -= OnTempo;
            _panel.WidthCycleClicked -= OnWidth;
            _panel.ApplyClicked -= OnApply;
            _panel.ResumeClicked -= OnResume;
        }

        // --- helpers -------------------------------------------------------------------------------

        /// <summary>The shared match minute, derived from the kickoff time so both clients agree.</summary>
        private int LiveMinute()
        {
            if (_kickoff == null) return 0;
            double secs = (DateTime.UtcNow - _kickoff.Value).TotalSeconds;
            int m = (int)(secs / LiveSecondsPerMinute);
            return m < 0 ? 0 : (m > 90 ? 90 : m);
        }

        private string BannerFor(LiveMatchStateDto state, LiveMatchStatus status)
        {
            switch (status)
            {
                case LiveMatchStatus.Pending:
                    return _loc.Tr("live.waiting");
                case LiveMatchStatus.Finished:
                    return _loc.Tr("live.finished");
                default:
                    return _loc.Tr("live.live",
                        state.homePresent ? HomeName() : _loc.Tr("live.absent_short"),
                        state.awayPresent ? AwayName() : _loc.Tr("live.absent_short"));
            }
        }

        private void UpdateScore() =>
            _view.SetScore(_loc.Tr("match.score", HomeName(), _homeGoals, _awayGoals, AwayName()));

        private string HomeName() => _state?.homeClubName ?? _target.HomeName ?? "?";
        private string AwayName() => _state?.awayClubName ?? _target.AwayName ?? "?";

        private string PlayerName(int playerId) =>
            _squad.TryGetValue(playerId, out LeaguePlayerDto p) ? p.name : ("#" + playerId);

        private int Overall(int playerId) =>
            _squad.TryGetValue(playerId, out LeaguePlayerDto p) ? p.overall : 0;

        private string RoleAbbr(PositionRole role) => _loc.Tr("role." + role.ToString().ToLowerInvariant());

        /// <summary>Delegated to <see cref="OnlineClock"/> in task 12.3, and it is a FIX as well as a tidy-up:
        /// the old <c>RoundtripKind</c> parse read a timestamp with no trailing "Z" as an Unspecified time and
        /// <c>ToUniversalTime()</c> then shifted it by the DEVICE's offset — so the shared match minute drifted
        /// by exactly the local UTC offset whenever the server's string lost its Kind. Postgres hands back a
        /// Kind and hid it in production; SQLite does not.</summary>
        private static DateTime? ParseUtc(string s) => OnlineClock.ParseUtc(s);

        private static MatchReport TryParseReport(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                MatchReport report = JsonConvert.DeserializeObject<MatchReport>(json);
                // The movement stream crosses the wire PACKED since engine phase 2 (a stored
                // replay went from 2077 KB to 794 KB); unpacking it is what turns the blob back
                // into the arrays the renderer walks. A report from before the packed form is
                // already carrying its arrays, and this is a no-op on it.
                report?.Positions?.Unpack();
                return report;
            }
            catch (JsonException) { return null; }
        }
    }
}
