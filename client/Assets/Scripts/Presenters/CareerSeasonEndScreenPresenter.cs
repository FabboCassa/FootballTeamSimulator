using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Career;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Career season-end decision (task 5.6), pushed from the Hub when the season is complete. Runs
    /// the coach evaluation (idempotent), shows how the season measured against the board's objective,
    /// the updated confidence/reputation and whether the user was warned or sacked, and lets him accept
    /// a job offer (move clubs) or stay. Committing the choice rolls the season over, then hands off to
    /// the standings season-end screen. Lives in the Game scope.
    /// </summary>
    public sealed class CareerSeasonEndScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly CareerService _service;
        private readonly ILocalizationService _loc;
        private readonly ClubIdentityService _identity;
        private readonly CareerSeasonEndView _view;

        private CareerSeasonReport _report;

        public VisualElement View => _view.Root;

        public CareerSeasonEndScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            CareerService service,
            ILocalizationService loc,
            ClubIdentityService identity)
        {
            _navigator = navigator;
            _career = career;
            _service = service;
            _loc = loc;
            _identity = identity;
            _view = new CareerSeasonEndView(loc.Tr);
        }

        public void Enter()
        {
            _view.AcceptClicked += OnAccept;
            _view.StayClicked += OnStay;

            _report = _service.EvaluateSeason();
            Render();
        }

        public void Exit()
        {
            _view.AcceptClicked -= OnAccept;
            _view.StayClicked -= OnStay;
        }

        private void Render()
        {
            _view.SetSummary(_loc.Tr("careerend.summary",
                _report.UserFinishPosition, _report.UserExpectedPosition, OutcomeName(_report.UserOutcome)));
            _view.SetStanding(_loc.Tr("careerend.standing", _report.UserConfidence, _report.UserReputation));

            int band = _report.UserSacked ? 0 : _report.UserWarned ? 1 : 2;
            string banner = _report.UserSacked ? _loc.Tr("careerend.sacked")
                          : _report.UserWarned ? _loc.Tr("careerend.warned")
                          : _loc.Tr("careerend.safe");
            _view.SetBanner(banner, band);

            var rows = new List<OfferRowVm>();
            foreach (JobOffer offer in _report.UserOffers)
            {
                rows.Add(new OfferRowVm
                {
                    ClubId = offer.ClubId,
                    Text = _loc.Tr("careerend.offer", offer.ClubName, offer.Division, offer.RequiredReputation),
                    ActionLabel = _loc.Tr("careerend.accept"),
                    Crest = Crests.Badge(_identity.Visual(offer.ClubId), 32f,
                        _career.FindClub(offer.ClubId)?.ShortName ?? "?", UiKit.Surface)
                });
            }
            _view.SetOffers(rows);

            // Sacked with offers ⇒ he must leave (no Stay). Otherwise Stay is available; when sacked
            // with no offers it's a board reprieve.
            bool mustLeave = _report.UserSacked && _report.UserOffers.Count > 0;
            string stayLabel = _report.UserSacked
                ? _loc.Tr("careerend.stay_reprieve")
                : _loc.Tr("careerend.stay", _career.GetUserClub()?.Name ?? string.Empty);
            _view.SetStay(stayLabel, !mustLeave);
        }

        private void OnAccept(int clubId)
        {
            _service.Commit(clubId);
            GoToStandings();
        }

        private void OnStay()
        {
            _service.Commit(_career.UserClubId);
            GoToStandings();
        }

        private void GoToStandings()
        {
            _navigator.Pop();                              // back to the Hub (now on the new season)
            _navigator.Push<SeasonEndScreenPresenter>();   // standings/promotion summary
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
