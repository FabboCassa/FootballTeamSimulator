using System.Collections.Generic;
using Fts.MatchView;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
using Fts.Views;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Tactics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Watchable match (task 3.1) with in-match interaction (task 3.4). Plays back
    /// the user's match; the user can pause to make substitutions and/or change
    /// instructions, which re-simulates the remainder from that minute via the
    /// deterministic engine (the minutes already watched are unchanged). The final
    /// (possibly intervened) report becomes the official result, re-committed to
    /// the season on Continue. Lives in a Screen scope under the Game scope.
    /// </summary>
    public sealed class MatchWatchScreenPresenter : IScreenPresenter
    {
        private const int MaxSubstitutions = 5;

        // Minimum kit-colour separation (Unity RGB, 0..~1.73) so the two sides read apart on the
        // pitch; below this the away side falls back to its secondary/accent (task 6.8 clash guard).
        private const float MinKitColorDistance = 0.42f;

        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly UserMatchLog _matchLog;
        private readonly UserMatchContextHolder _contextHolder;
        private readonly ISaveRepository _saveRepository;
        private readonly ILocalizationService _loc;
        private readonly ClubIdentityService _identity;
        private readonly MatchWatchView _view;
        private readonly InMatchPanel _panel;
        // Condition-aware + within-match fatigue, matching the headless advance (task 4.2);
        // the re-sim restores each player's kickoff condition (see ResimWithKickoffCondition)
        // so it stays consistent with the committed result even though the live players have evolved.
        private readonly MatchEngine _engine = new MatchEngine(applyCondition: true, applyMatchFatigue: true);
        private readonly int _famMax = new BalanceConfig().Tactics.FamiliarityMax;

        private MatchRenderer _renderer;
        private UserMatchContext _context;
        private MatchReport _baseline;   // the committed (pre-intervention) result
        private MatchReport _current;    // what is being watched (re-sim after changes)
        private MatchPlan _plan;         // grows with each applied change
        private bool _changed;

        private Color _homeColor;
        private Color _awayColor;
        private int _homeGoals;
        private int _awayGoals;
        private int _shownMinute;
        private float _speed = 1f;

        // Working intervention state (persists across pauses within the match).
        private Lineup _workingLineup;       // the user side's live XI
        private Lineup _opponentLineup;      // fixed
        private TacticContext _opponentCtx;
        private Formation _formation;
        private Mentality _mentality;
        private Pressing _pressing;
        private Tempo _tempo;
        private Width _width;
        private Tactic _kickoffTactic;   // the tactic (and familiarity) the match started with
        private int _kickoffFam;
        private int _subsUsed;
        private int _selectedSlot = -1;

        public VisualElement View => _view.Root;

        public MatchWatchScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            UserMatchLog matchLog,
            UserMatchContextHolder contextHolder,
            ISaveRepository saveRepository,
            ILocalizationService loc,
            ClubIdentityService identity)
        {
            _navigator = navigator;
            _career = career;
            _matchLog = matchLog;
            _contextHolder = contextHolder;
            _saveRepository = saveRepository;
            _loc = loc;
            _identity = identity;
            _view = new MatchWatchView(loc.Tr);
            _panel = new InMatchPanel(loc.Tr);
        }

        public void Enter()
        {
            _context = _contextHolder.Current;
            if (_matchLog.LastMatch == null || _context == null)
                return;

            _baseline = _matchLog.LastMatch.Report;
            _current = _baseline;
            _plan = _context.Plan;
            InitInterventionState();

            // Kit colours from the club identities (task 6.8), with a clash guard so two
            // similarly-coloured clubs still read apart on the pitch.
            int homeId = _context.Fixture.HomeClubId;
            int awayId = _context.Fixture.AwayClubId;
            ClubVisual homeVis = _identity.Visual(homeId);
            ClubVisual awayVis = _identity.Visual(awayId);
            _homeColor = homeVis.Primary;
            _awayColor = PickAwayColor(homeVis.Primary, awayVis);

            _view.SetCrests(
                Crests.Badge(homeVis, 30f, ClubShort(homeId), UiKit.Background),
                Crests.Badge(awayVis, 30f, ClubShort(awayId), UiKit.Background));

            _view.Root.Add(_panel.Root); // overlay on top of the HUD/pitch/controls
            _panel.SetVisible(false);

            _view.SpeedClicked += OnSpeed;
            _view.SkipClicked += OnSkip;
            _view.PauseClicked += OnPause;
            _view.ContinueClicked += OnContinue;
            WirePanel();

            BuildRenderer(_current, seekToMinute: 0);

            UpdateScore();
            _view.SetClock(_loc.Tr("match.clock", 0));
            _view.SetActiveSpeed(1f);
        }

        public void Exit()
        {
            DetachRenderer();

            _view.SpeedClicked -= OnSpeed;
            _view.SkipClicked -= OnSkip;
            _view.PauseClicked -= OnPause;
            _view.ContinueClicked -= OnContinue;
            UnwirePanel();
        }

        // -------------------------------------------------------- playback control

        private void OnSpeed(float speed)
        {
            _speed = speed;
            _renderer?.SetSpeed(speed);
            _view.SetActiveSpeed(speed);
        }

        private void OnSkip() => _renderer?.Skip();

        private void OnPause()
        {
            if (_renderer == null)
                return;

            _renderer.Stop();
            RefreshPanel();
            _panel.SetVisible(true);
        }

        private void OnContinue()
        {
            if (_changed)
            {
                // Replace the provisional result with the intervened one.
                SeasonProgressor.RevertResult(_career.Season, _context.Fixture, _baseline);
                SeasonProgressor.RecordResult(_career.Season, _context.Fixture, _current);
                _matchLog.LastMatch = new MatchOutcome(_context.Fixture, _current);
                _saveRepository.Save(_career);
            }

            _navigator.Pop();
            _navigator.Push<MatchResultScreenPresenter>();
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
                if (e.ClubId == _context.Fixture.HomeClubId) _homeGoals++;
                else _awayGoals++;
                UpdateScore();
            }

            _view.ShowToast(_loc.Tr(EventKey(e.Type), e.Minute, PlayerName(e.ClubId, e.PlayerId), ClubName(e.ClubId)));
        }

        private void OnFinished()
        {
            _homeGoals = _current.HomeGoals;
            _awayGoals = _current.AwayGoals;
            UpdateScore();
            _view.SetClock(_loc.Tr("match.clock", 90));
            _view.SetFinished(true);
        }

        // ----------------------------------------------------------- intervention

        private void OnResume()
        {
            _panel.SetVisible(false);
            _renderer?.Play();
        }

        private void OnApply()
        {
            int from = Mathf.Clamp(_shownMinute + 1, 1, 90);
            MatchInput input = BuildChangedInput();
            _plan = _plan.WithChange(from, input);
            // Re-sim with the user's conditional rules (3.5) layered under the manual
            // change so the prefix still matches what was committed/watched. With no
            // plan both rule lists are null and this is byte-identical to the 3.4 path.
            _current = ResimWithKickoffCondition();
            _changed = true;

            _panel.SetVisible(false);
            BuildRenderer(_current, seekToMinute: _shownMinute);
        }

        private void OnPitchClicked(int slotIndex)
        {
            _selectedSlot = slotIndex;
            RefreshPanel();
        }

        private void OnBenchClicked(int playerId)
        {
            if (_selectedSlot < 0 || _subsUsed >= MaxSubstitutions)
                return;

            Player incoming = FindSquadPlayer(playerId);
            if (incoming == null)
                return;

            _workingLineup.Slots[_selectedSlot].Player = incoming;
            _subsUsed++;
            _selectedSlot = -1;
            RefreshPanel();
        }

        private void OnMentality() { _mentality = (Mentality)(((int)_mentality + 1) % 3); RefreshPanel(); }
        private void OnPressing() { _pressing = (Pressing)(((int)_pressing + 1) % 3); RefreshPanel(); }
        private void OnTempo() { _tempo = (Tempo)(((int)_tempo + 1) % 3); RefreshPanel(); }
        private void OnWidth() { _width = (Width)(((int)_width + 1) % 3); RefreshPanel(); }

        /// <summary>
        /// Re-simulates the current plan with every involved player's KICKOFF condition
        /// restored for the duration of the sim (task 4.2). The headless advance evolves
        /// condition right after the match, so the live players are already drained/
        /// rested by the time this screen runs; without restoring the kickoff values the
        /// re-sim prefix would diverge from the committed/watched result. Live values are
        /// put back in a finally so the rest of the career keeps the evolved condition.
        /// </summary>
        private MatchReport ResimWithKickoffCondition()
        {
            var rng = SeasonProgressor.FixtureRng(_context.WorldSeed, _context.Fixture.Id);

            var restore = new List<(PlayerCondition Cond, int Form, int Morale, int Fitness)>();
            foreach (KeyValuePair<int, PlayerCondition> kv in _context.KickoffCondition)
            {
                Player p = _career.FindPlayer(kv.Key);
                if (p == null)
                    continue;

                restore.Add((p.Condition, p.Condition.Form, p.Condition.Morale, p.Condition.Fitness));
                p.Condition.Form = kv.Value.Form;
                p.Condition.Morale = kv.Value.Morale;
                p.Condition.Fitness = kv.Value.Fitness;
            }

            try
            {
                return _engine.Simulate(_plan, _context.HomeRules, _context.AwayRules, rng);
            }
            finally
            {
                foreach ((PlayerCondition cond, int form, int morale, int fitness) in restore)
                {
                    cond.Form = form;
                    cond.Morale = morale;
                    cond.Fitness = fitness;
                }
            }
        }

        /// <summary>
        /// Fills a panel row's condition strip from the player's KICKOFF condition
        /// (task 4.2). The live players were already drained by EvolveCondition after
        /// the match, but in-match the relevant value is what they took into the game —
        /// the model has no within-match fatigue curve — so we read the frozen snapshot.
        /// </summary>
        private void ApplyKickoffCondition(InMatchRowVm vm, Player player)
        {
            ConditionDisplay d = ConditionDisplay.Build(KickoffConditionOf(player), _loc.Tr);
            vm.FormArrow = d.FormArrow;
            vm.MoraleFace = d.MoraleFace;
            vm.Fitness = d.Fitness;
            vm.Tooltip = d.Tooltip;
        }

        private PlayerCondition KickoffConditionOf(Player player)
        {
            if (_context.KickoffCondition != null
                && _context.KickoffCondition.TryGetValue(player.Id, out PlayerCondition c) && c != null)
                return c;

            return player.Condition; // fallback (e.g. condition snapshot empty)
        }

        private MatchInput BuildChangedInput()
        {
            var tactic = new Tactic(_formation, new TacticInstructions(_mentality, _pressing, _tempo, _width));
            var userCtx = new TacticContext(tactic, FamFor(tactic));
            MatchTactics tactics = _context.UserIsHome
                ? new MatchTactics(userCtx, _opponentCtx)
                : new MatchTactics(_opponentCtx, userCtx);

            Lineup home = _context.UserIsHome ? _workingLineup : _opponentLineup;
            Lineup away = _context.UserIsHome ? _opponentLineup : _workingLineup;
            return new MatchInput(home, away, tactics);
        }

        private void InitInterventionState()
        {
            MatchInput initial = _plan.Initial;
            Lineup userSide = _context.UserIsHome ? initial.Home : initial.Away;
            _opponentLineup = _context.UserIsHome ? initial.Away : initial.Home;
            _workingLineup = CloneLineup(userSide);

            if (initial.Tactics != null)
            {
                TacticContext userCtx = _context.UserIsHome ? initial.Tactics.Home : initial.Tactics.Away;
                _opponentCtx = _context.UserIsHome ? initial.Tactics.Away : initial.Tactics.Home;
                _formation = userCtx.Tactic.Formation;
                _mentality = userCtx.Tactic.Instructions.Mentality;
                _pressing = userCtx.Tactic.Instructions.Pressing;
                _tempo = userCtx.Tactic.Instructions.Tempo;
                _width = userCtx.Tactic.Instructions.Width;
                _kickoffTactic = userCtx.Tactic;
                _kickoffFam = userCtx.Familiarity;
            }
            else
            {
                // No kickoff tactic: a neutral, fully-familiar setup = engine identity,
                // so a substitution-only change does not introduce a tactical swing.
                _opponentCtx = TacticContext.Neutral(_famMax);
                _formation = Formation.F433;
                _mentality = Mentality.Balanced;
                _pressing = Pressing.Medium;
                _tempo = Tempo.Normal;
                _width = Width.Normal;
                _kickoffTactic = Tactic.Neutral;
                _kickoffFam = _famMax;
            }

            _subsUsed = 0;
            _selectedSlot = -1;
        }

        /// <summary>
        /// Familiarity the team would carry into a mid-match switch to <paramref name="t"/>:
        /// the kickoff tactic keeps its kickoff familiarity, any other tactic uses the
        /// club's stored familiarity for it (0 if never drilled). So switching to a
        /// well-drilled alternative is safe, improvising an unfamiliar one costs
        /// effectiveness — the change can help, hurt, or do nothing by habituation.
        /// </summary>
        private int FamFor(Tactic t)
        {
            if (t.Equals(_kickoffTactic))
                return _kickoffFam;

            return _career.TacticFamiliarity.TryGetValue(TacticPlan.FromTactic(t).Key(), out int v) ? v : 0;
        }

        // --------------------------------------------------------------- rendering

        private void BuildRenderer(MatchReport report, int seekToMinute)
        {
            DetachRenderer();

            _renderer = new MatchRenderer(report, _homeColor, _awayColor);
            _renderer.MinuteChanged += OnMinuteChanged;
            _renderer.EventReached += OnEventReached;
            _renderer.Finished += OnFinished;
            _view.PitchContainer.Insert(0, _renderer); // behind the toast overlay

            _renderer.SetSpeed(_speed);
            _view.SetFinished(false);

            if (seekToMinute > 0)
                _renderer.SeekToMinute(seekToMinute);

            RecomputeScoreUpTo(report, seekToMinute);
            UpdateScore();
            _view.SetClock(_loc.Tr("match.clock", seekToMinute));
            _shownMinute = seekToMinute;

            _renderer.Play();
        }

        private void DetachRenderer()
        {
            if (_renderer == null)
                return;

            _renderer.Stop();
            _renderer.MinuteChanged -= OnMinuteChanged;
            _renderer.EventReached -= OnEventReached;
            _renderer.Finished -= OnFinished;
            if (_renderer.parent != null)
                _renderer.RemoveFromHierarchy();
            _renderer = null;
        }

        private void RecomputeScoreUpTo(MatchReport report, int minute)
        {
            _homeGoals = 0;
            _awayGoals = 0;
            foreach (MatchEvent e in report.Events)
            {
                if (e.Type != MatchEventType.Goal || e.Minute > minute)
                    continue;
                if (e.ClubId == _context.Fixture.HomeClubId) _homeGoals++;
                else _awayGoals++;
            }
        }

        // ------------------------------------------------------------- panel glue

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

        private void RefreshPanel()
        {
            _panel.SetTitle(_loc.Tr("inmatch.paused_at", _shownMinute));
            _panel.SetSubsRemaining(_loc.Tr("inmatch.subs_remaining", MaxSubstitutions - _subsUsed));

            var pitch = new List<InMatchRowVm>(_workingLineup.Slots.Count);
            var ids = new HashSet<int>();
            for (int i = 0; i < _workingLineup.Slots.Count; i++)
            {
                LineupSlot slot = _workingLineup.Slots[i];
                ids.Add(slot.Player.Id);
                var pitchVm = new InMatchRowVm
                {
                    Id = i,
                    Label = $"{RoleAbbr(slot.Role)}  {slot.Player.FullName}  ({PlayerRating.OverallFor(slot.Player, slot.Role)})",
                    Selected = i == _selectedSlot
                };
                ApplyKickoffCondition(pitchVm, slot.Player);
                pitch.Add(pitchVm);
            }
            _panel.SetPitch(pitch);

            Club userClub = _career.FindClub(_career.UserClubId);
            var bench = new List<InMatchRowVm>();
            if (userClub != null)
            {
                foreach (Player p in userClub.Squad.Players)
                {
                    if (ids.Contains(p.Id))
                        continue;
                    var benchVm = new InMatchRowVm
                    {
                        Id = p.Id,
                        Label = $"{RoleAbbr(p.Role)}  {p.FullName}  OVR {PlayerRating.Overall(p)}"
                    };
                    ApplyKickoffCondition(benchVm, p);
                    bench.Add(benchVm);
                }
            }
            _panel.SetBench(bench);

            var working = new Tactic(_formation, new TacticInstructions(_mentality, _pressing, _tempo, _width));
            int pct = _famMax > 0 ? FamFor(working) * 100 / _famMax : 0;
            _panel.SetFamiliarity(_loc.Tr("inmatch.familiarity", pct));

            _panel.SetMentality(_loc.Tr("tactics.label.mentality",
                _loc.Tr("tactics.mentality." + _mentality.ToString().ToLowerInvariant())));
            _panel.SetPressing(_loc.Tr("tactics.label.pressing",
                _loc.Tr("tactics.pressing." + _pressing.ToString().ToLowerInvariant())));
            _panel.SetTempo(_loc.Tr("tactics.label.tempo",
                _loc.Tr("tactics.tempo." + _tempo.ToString().ToLowerInvariant())));
            _panel.SetWidth(_loc.Tr("tactics.label.width",
                _loc.Tr("tactics.width." + _width.ToString().ToLowerInvariant())));
        }

        // ---------------------------------------------------------------- helpers

        private void UpdateScore()
        {
            string home = ClubName(_context.Fixture.HomeClubId);
            string away = ClubName(_context.Fixture.AwayClubId);
            _view.SetScore(_loc.Tr("match.score", home, _homeGoals, _awayGoals, away));
        }

        private static string EventKey(MatchEventType type)
        {
            switch (type)
            {
                case MatchEventType.Goal: return "match.event.goal";
                case MatchEventType.ChanceSaved: return "match.event.saved";
                default: return "match.event.missed";
            }
        }

        private static Lineup CloneLineup(Lineup src)
        {
            var copy = new Lineup { ClubId = src.ClubId };
            foreach (LineupSlot slot in src.Slots)
                copy.Slots.Add(new LineupSlot { Role = slot.Role, Player = slot.Player });
            return copy;
        }

        private Player FindSquadPlayer(int playerId)
        {
            Club club = _career.FindClub(_career.UserClubId);
            if (club != null)
            {
                foreach (Player p in club.Squad.Players)
                {
                    if (p.Id == playerId)
                        return p;
                }
            }

            return null;
        }

        private string RoleAbbr(PositionRole role) => _loc.Tr("role." + role.ToString().ToLowerInvariant());

        private string ClubName(int clubId) => _career.FindClub(clubId)?.Name ?? $"Club {clubId}";

        private string ClubShort(int clubId) => _career.FindClub(clubId)?.ShortName ?? "?";

        /// <summary>
        /// The away side's on-pitch colour (task 6.8): its primary, unless that's too close to the
        /// home primary — then its secondary, then its accent, so the two teams never blur together.
        /// </summary>
        private static Color PickAwayColor(Color home, ClubVisual away)
        {
            if (ColorDistance(home, away.Primary) >= MinKitColorDistance) return away.Primary;
            if (ColorDistance(home, away.Secondary) >= MinKitColorDistance) return away.Secondary;
            return away.Accent;
        }

        private static float ColorDistance(Color a, Color b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        private string PlayerName(int clubId, int playerId)
        {
            Club club = _career.FindClub(clubId);
            if (club != null)
            {
                foreach (Player p in club.Squad.Players)
                {
                    if (p.Id == playerId)
                        return p.FullName;
                }
            }

            return $"Player {playerId}";
        }
    }
}
