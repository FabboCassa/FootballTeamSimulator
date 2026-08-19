using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Career;
using Sim.Core.Domain;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Career screen (task 5.6): the user's standing as a coach — board objective, current league
    /// position, the board-confidence meter, reputation and a season-by-season history. Read-only;
    /// lives in the Game scope. Reachable from the Hub.
    /// </summary>
    public sealed class CareerScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly CareerService _service;
        private readonly ILocalizationService _loc;
        private readonly CareerView _view;

        public VisualElement View => _view.Root;

        public CareerScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            CareerService service,
            ILocalizationService loc)
        {
            _navigator = navigator;
            _career = career;
            _service = service;
            _loc = loc;
            _view = new CareerView(loc.Tr);
        }

        public void Enter()
        {
            _view.BackClicked += OnBack;
            Refresh();
        }

        public void Exit()
        {
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => Refresh();

        private void OnBack() => _navigator.Pop();

        private void Refresh()
        {
            Club club = _career.GetUserClub();
            _view.SetHeader(_loc.Tr("career.header", club?.Name ?? string.Empty));

            // Short values: the caption ("Board objective", …) is baked into the tile, so the
            // value line carries only the fact (task 6.12 stat tiles).
            _view.SetObjective(_loc.Tr("career.objective_value", TierName(_service.Objective), _service.ObjectivePosition));
            _view.SetPosition(_loc.Tr("career.position_value", _service.CurrentPosition(), _service.ClubCount));
            _view.SetReputation(_loc.Tr("career.reputation_value", _service.Reputation));

            int band = _service.ConfidenceBand;
            string status = _loc.Tr(band <= 0 ? "career.confidence.risk" : band == 1 ? "career.confidence.warned" : "career.confidence.safe");
            _view.SetConfidence(_loc.Tr("career.confidence", _service.Confidence, status), _service.Confidence, band);

            List<string> history = BuildHistory();
            if (history.Count == 0)
                _view.SetHistoryEmpty(_loc.Tr("career.history_empty"));
            else
                _view.SetHistory(history);
        }

        private List<string> BuildHistory()
        {
            var lines = new List<string>();
            IReadOnlyList<CareerHistoryEntry> history = _service.History;
            for (int i = history.Count - 1; i >= 0; i--) // newest first
            {
                CareerHistoryEntry e = history[i];
                string outcome = OutcomeName((SeasonOutcome)e.Outcome);
                string line = _loc.Tr("career.history_row", e.Year, e.ClubName, e.FinishPosition, e.ExpectedPosition, outcome);
                if (e.Champion) line = _loc.Tr("career.history_champion", line);
                if (e.Sacked) line = _loc.Tr("career.history_sacked", line);
                lines.Add(line);
            }

            return lines; // empty → the view shows the illustrated empty state
        }

        private string TierName(ObjectiveTier tier)
        {
            switch (tier)
            {
                case ObjectiveTier.WinTitle: return _loc.Tr("career.tier.win_title");
                case ObjectiveTier.ChallengeForHonours: return _loc.Tr("career.tier.challenge");
                case ObjectiveTier.UpperMidTable: return _loc.Tr("career.tier.upper_mid");
                case ObjectiveTier.MidTable: return _loc.Tr("career.tier.mid");
                default: return _loc.Tr("career.tier.survival");
            }
        }

        private string OutcomeName(SeasonOutcome outcome)
        {
            switch (outcome)
            {
                case SeasonOutcome.Overachieved: return _loc.Tr("career.outcome.over");
                case SeasonOutcome.Underachieved: return _loc.Tr("career.outcome.under");
                default: return _loc.Tr("career.outcome.met");
            }
        }
    }
}
