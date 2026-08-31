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
    /// THE LADDER'S LIVE MATCH (task 12.3) — the screen that turns "one match a day" from something you read
    /// about afterwards into an appointment you keep.
    ///
    /// It reuses 8.6b's furniture unchanged — <see cref="LiveMatchView"/>, <see cref="InMatchPanel"/>,
    /// <see cref="MatchRenderer"/> — and is a SIBLING of <see cref="OnlineLiveMatchScreenPresenter"/> rather
    /// than a rework of it. That is the same call task 12.2b took with the auction room, and for the same
    /// reason: the two competitions' rules differ enough that one presenter would be a knot of conditionals,
    /// and the private-league screen has already been verified in Play mode — a refactor would put a proven
    /// screen at risk to save duplication in the panel glue.
    ///
    /// THREE THINGS ARE GENUINELY DIFFERENT FROM THE PRIVATE-LEAGUE SCREEN, and each is the point of 12.3:
    /// <list type="number">
    /// <item><b>The clock is the calendar's.</b> 8.6b kicks off when both members are present; here kick-off
    /// is a scheduled instant — 21:00 of the world's own evening — so before it the screen is a LOBBY with a
    /// countdown, and at it the match starts whether or not the opponent turned up. Nobody's absence moves a
    /// ranked kick-off and nobody's presence brings one forward.</item>
    /// <item><b>The playback rate comes from the SERVER.</b> It is what the server judges a pause-point change
    /// against ("that minute has not been played yet"), so rendering on a local constant would mean the screen
    /// and the server disagreeing about what minute it is — at the exact moment a substitution matters.</item>
    /// <item><b>The opponent may be an AI seat</b>, and the screen says so plainly instead of waiting forever
    /// for somebody who does not exist.</item>
    /// </list>
    ///
    /// Everything else is 8.6b's mechanism, and deliberately so: each change is posted to /change, the server
    /// re-simulates the whole 90' deterministically from the fixture seed and returns the new state, and the
    /// renderer rebuilds from the current live minute — so the minutes already watched never rewind. Leaving
    /// the screen marks the caller absent and the match plays on from his stored orders, exactly as an AI
    /// seat's side does.
    /// </summary>
    public sealed class RankedLiveMatchScreenPresenter : IScreenPresenter
    {
        /// <summary>A ranked lot can run for a day but a ranked MATCH runs for three minutes: poll at 1s like
        /// the private leagues, because here a second of latency is a second of the match.</summary>
        private const int PollMillis = 1000;
        private const int MaxSubstitutions = 5;
        /// <summary>Fallback playback rate, used only until the first state arrives (the server owns it).</summary>
        private const int DefaultSecondsPerMinute = 2;

        private static readonly Color HomeColor = UiKit.Accent;
        private static readonly Color AwayColor = UiKit.Danger;

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly RankedLiveTarget _target;
        private readonly LiveMatchView _view;
        private readonly InMatchPanel _panel;

        private string _fixtureId;
        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _panelOpen;

        private RankedLiveStateDto _state;
        private string _lastReportJson;
        private DateTime? _kickoff;
        private TimeSpan _clockOffset = TimeSpan.Zero;
        private int _secondsPerMinute = DefaultSecondsPerMinute;

        private MatchRenderer _renderer;
        private int _homeClubId;
        private int _homeGoals;
        private int _awayGoals;

        // My side (for control). The XI is client-tracked; the first change fixes it authoritatively.
        private LiveSide? _mySide;
        private int _myClubExternalId;
        private readonly Dictionary<int, RankedPlayerDto> _squad = new Dictionary<int, RankedPlayerDto>();
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

        public RankedLiveMatchScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked, RankedLiveTarget target)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
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

            _fixtureId = _target.FixtureId;
            _cts = new CancellationTokenSource();
            InitAsync(_cts.Token).Forget();
        }

        public void Exit()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            // Step away: the opponent's screen sees it, and the match plays on from the stored orders.
            if (!string.IsNullOrEmpty(_fixtureId)) _ranked.LeaveLiveAsync(_fixtureId).Forget();

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
            if (string.IsNullOrEmpty(_fixtureId))
            {
                _view.ShowStatus(_loc.Tr("ranked.error.fixture_not_found"));
                return;
            }

            _view.ShowStatus(_loc.Tr("ranked.loading"));
            var open = await _ranked.OpenLiveAsync(_fixtureId);
            if (!open.Success)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(open.Error)));
                return;
            }

            _view.ShowStatus(string.Empty);
            await LoadSquadAsync(open.Value);
            Apply(open.Value, rebuild: true);
            PollAsync(ct).Forget();
        }

        private async UniTask LoadSquadAsync(RankedLiveStateDto state)
        {
            _mySide = state.yourSide.HasValue ? (LiveSide)state.yourSide.Value : (LiveSide?)null;
            if (!_mySide.HasValue) return;

            _myClubExternalId = _mySide.Value == LiveSide.Home ? state.homeClubExternalId : state.awayClubExternalId;

            var squad = await _ranked.GetClubSquadAsync(_myClubExternalId);
            if (!squad.Success || squad.Value?.players == null) return;

            _squad.Clear();
            foreach (RankedPlayerDto p in squad.Value.players) _squad[p.externalId] = p;

            // Seed a starting XI: the first 11 in the default formation slots. The first change sends this
            // (with any subs) as the authoritative lineup for my side from that minute on.
            _workingLineup.Clear();
            var roles = LineupSelector.DefaultFormation;
            for (int i = 0; i < 11 && i < squad.Value.players.Count; i++)
                _workingLineup.Add(new Slot { Role = roles[i], PlayerId = squad.Value.players[i].externalId });
        }

        private async UniTaskVoid PollAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                bool cancelled = await UniTask.Delay(
                    TimeSpan.FromMilliseconds(PollMillis), cancellationToken: ct).SuppressCancellationThrow();
                if (cancelled) return;

                // The lobby counts down even while a request is in flight — an appointment that stalls its
                // own clock is worse than one that is a second stale.
                if (_state != null && (LiveMatchStatus)_state.status == LiveMatchStatus.Pending)
                    _view.SetBanner(BannerFor(_state, LiveMatchStatus.Pending));

                if (_busy) continue;

                var result = await _ranked.GetLiveAsync(_fixtureId);
                if (result.Success) Apply(result.Value, rebuild: !_panelOpen);
            }
        }

        /// <summary>Apply a fresh state: banner, score, controls and — when a new report arrived and we are
        /// not mid-edit — a renderer rebuilt at the current live minute.</summary>
        private void Apply(RankedLiveStateDto state, bool rebuild)
        {
            _state = state;
            _homeClubId = state.homeClubExternalId;
            _kickoff = OnlineClock.ParseUtc(state.kickoffUtc);
            _clockOffset = OnlineClock.OffsetFrom(state.serverUtc);
            if (state.secondsPerMatchMinute > 0) _secondsPerMinute = state.secondsPerMatchMinute;

            var status = (LiveMatchStatus)state.status;
            _view.SetBanner(BannerFor(state, status));

            bool iControl = _mySide.HasValue && status == LiveMatchStatus.Live;
            _view.SetModifyEnabled(iControl && !_busy);
            // "Fine partita" appears once the shared clock reaches full time (nobody finishes early).
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

        private void OnMinuteChanged(int minute) => _view.SetClock(_loc.Tr("match.clock", minute));

        private void OnEventReached(MatchEvent e)
        {
            if (e.Type != MatchEventType.Goal) return;
            if (e.ClubId == _homeClubId) _homeGoals++; else _awayGoals++;
            UpdateScore();
            string club = e.ClubId == _homeClubId ? HomeName() : AwayName();
            _view.ShowToast(_loc.Tr("replay.goal", e.Minute, club));
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
            // Re-anchor to the current live minute — the opponent may have changed the report meanwhile.
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

            // The minute is the one the SHARED clock says has been played — never a minute of our own
            // choosing. The server refuses anything ahead of it, which is the guard that stops a doctored
            // client reading the ending out of the report and substituting with hindsight.
            int from = Mathf.Clamp(LiveMinute(), 1, 90);

            var result = await _ranked.SubmitLiveChangeAsync(
                _fixtureId, from, BuildLineupPlan(), BuildTacticPlan());

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
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)));
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
            _panel.SetFamiliarity(string.Empty); // online tactics run at full familiarity (server-side, 8.4)

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
                    Fitness = 100, // condition is server-side online; a neutral strip beats a misleading one
                });
            }
            _panel.SetPitch(pitch);

            var bench = new List<InMatchRowVm>();
            foreach (KeyValuePair<int, RankedPlayerDto> kv in _squad)
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

            var result = await _ranked.FinishLiveAsync(_fixtureId);
            _busy = false;

            if (result.Success) _navigator.Pop();
            else _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(result.Error)));
        }

        private void OnBack() => _navigator.Pop();

        // --- dev tooling (gated by DevFlags.OnlineTestTools) ---------------------------------------

        // Simulate the opponent turning up, so a solo tester sees the other side of his 21:00 match.
        private void OnBotJoin() => BotAsync(sub: false).Forget();

        // …and making a substitution at the minute actually being played, so the ✅ is observable solo.
        private void OnBotSub() => BotAsync(sub: true).Forget();

        private async UniTaskVoid BotAsync(bool sub)
        {
            if (_busy || string.IsNullOrEmpty(_fixtureId)) return;
            _busy = true;
            _view.ShowStatus(_loc.Tr("live.status.bot"));

            bool ok = await _ranked.BotLiveDevAsync(_fixtureId, sub, LiveMinute());
            _busy = false;

            // A vacant AI seat has no account to act as — the server says so and nothing happens, which is
            // the match working rather than the tool failing. Say that, instead of showing an error.
            _view.ShowStatus(ok ? string.Empty : _loc.Tr("ranked.live.bot_is_ai"));

            var refreshed = await _ranked.GetLiveAsync(_fixtureId);
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

        /// <summary>
        /// The shared match minute: derived from the SCHEDULED kick-off and the server's own playback rate,
        /// against a clock corrected for this device's drift. Every one of those three is deliberate — two
        /// coaches in different countries must render the same minute at the same instant, and the server
        /// refuses a change that runs ahead of this number.
        /// </summary>
        private int LiveMinute()
        {
            if (_kickoff == null) return 0;
            double secs = (OnlineClock.NowUtc(_clockOffset) - _kickoff.Value).TotalSeconds;
            int m = (int)(secs / Math.Max(1, _secondsPerMinute));
            return m < 0 ? 0 : (m > 90 ? 90 : m);
        }

        private string BannerFor(RankedLiveStateDto state, LiveMatchStatus status)
        {
            switch (status)
            {
                case LiveMatchStatus.Pending:
                {
                    // The lobby. Not "waiting for your opponent" — waiting for the APPOINTMENT, which is the
                    // whole difference between the ladder and a friends' league.
                    TimeSpan left = _kickoff.HasValue
                        ? _kickoff.Value - OnlineClock.NowUtc(_clockOffset)
                        : TimeSpan.Zero;
                    return _loc.Tr("ranked.live.kickoff_in", OnlineClock.Countdown(left));
                }
                case LiveMatchStatus.Finished:
                    return _loc.Tr("live.finished");
                default:
                    return _loc.Tr("live.live", SidePresence(state, LiveSide.Home), SidePresence(state, LiveSide.Away));
            }
        }

        /// <summary>What to call each side in the live banner: its name when a coach is watching, "AI" when
        /// the seat is vacant, and the absent marker when a coach simply is not there.</summary>
        private string SidePresence(RankedLiveStateDto state, LiveSide side)
        {
            bool isAi = side == LiveSide.Home ? state.homeIsAi : state.awayIsAi;
            if (isAi) return _loc.Tr("ranked.live.ai_side");
            bool present = side == LiveSide.Home ? state.homePresent : state.awayPresent;
            return present
                ? (side == LiveSide.Home ? HomeName() : AwayName())
                : _loc.Tr("live.absent_short");
        }

        private void UpdateScore() =>
            _view.SetScore(_loc.Tr("match.score", HomeName(), _homeGoals, _awayGoals, AwayName()));

        private string HomeName() => _state?.homeClubName ?? _target.HomeName ?? "?";
        private string AwayName() => _state?.awayClubName ?? _target.AwayName ?? "?";

        private string PlayerName(int playerId) =>
            _squad.TryGetValue(playerId, out RankedPlayerDto p) ? p.name : ("#" + playerId);

        private int Overall(int playerId) =>
            _squad.TryGetValue(playerId, out RankedPlayerDto p) ? p.overall : 0;

        private string RoleAbbr(PositionRole role) => _loc.Tr("role." + role.ToString().ToLowerInvariant());

        private static MatchReport TryParseReport(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<MatchReport>(json); }
            catch (JsonException) { return null; }
        }
    }
}
