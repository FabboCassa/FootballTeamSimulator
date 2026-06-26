using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Difficulty;
using Sim.Core.Domain;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// New-career setup: generates a two-division world (rerollable) and
    /// starts the career when a club is picked. Runs in the App scope.
    /// </summary>
    public sealed class CareerSetupPresenter : IScreenPresenter
    {
        private readonly CareerFactory _factory;
        private readonly IGameSessionService _session;
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly CareerSetupView _view;

        private ulong _seed;
        private List<League> _leagues;
        private DifficultyLevel _difficulty = DifficultyLevel.Normal;

        public VisualElement View => _view.Root;

        public CareerSetupPresenter(
            CareerFactory factory,
            IGameSessionService session,
            ScreenNavigator navigator,
            ILocalizationService loc)
        {
            _factory = factory;
            _session = session;
            _navigator = navigator;
            _loc = loc;
            _view = new CareerSetupView(loc.Tr);
        }

        public void Enter()
        {
            _view.ClubSelected += OnClubSelected;
            _view.RerollClicked += Reroll;
            _view.BackClicked += OnBack;
            _view.DifficultySelected += OnDifficultySelected;
            _view.SetDifficulty((int)_difficulty);
            Reroll();
        }

        public void Exit()
        {
            _view.ClubSelected -= OnClubSelected;
            _view.RerollClicked -= Reroll;
            _view.BackClicked -= OnBack;
            _view.DifficultySelected -= OnDifficultySelected;
        }

        private void OnDifficultySelected(int level)
        {
            _difficulty = (DifficultyLevel)level;
            _view.SetDifficulty(level);
        }

        private void Reroll()
        {
            _seed = _factory.NewSeed();
            _leagues = _factory.GenerateWorld(_seed);

            var clubs = new List<KeyValuePair<int, string>>();
            foreach (League league in _leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    clubs.Add(new KeyValuePair<int, string>(
                        club.Id,
                        _loc.Tr("career_setup.club_label", league.Division, club.Name)));
                }
            }

            _view.SetLeague($"{_leagues[0].Name} / {_leagues[1].Name}", clubs);
        }

        private void OnClubSelected(int clubId)
        {
            var state = _factory.Create(_seed, _leagues, clubId, _difficulty);

            // Pop the setup screen first; StartNewCareer then pushes the Hub.
            _navigator.Pop();
            _session.StartNewCareer(state);
        }

        private void OnBack() => _navigator.Pop();
    }
}
