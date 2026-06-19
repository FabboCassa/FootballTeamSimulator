using System;
using Fts.Presenters;
using Fts.Services;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
using VContainer;
using VContainer.Unity;

namespace Fts.App
{
    /// <summary>
    /// Owns the Game (career) child scope and the save lifecycle:
    /// new careers are saved immediately, EndCareer saves again before
    /// disposing the scope.
    /// </summary>
    public sealed class GameSessionService : IGameSessionService, IDisposable
    {
        private readonly LifetimeScope _appScope;
        private readonly ScreenNavigator _navigator;
        private readonly ISaveRepository _saveRepository;
        private LifetimeScope _gameScope;
        private CareerState _career;

        public bool IsCareerActive => _gameScope != null;

        public GameSessionService(LifetimeScope appScope, ScreenNavigator navigator, ISaveRepository saveRepository)
        {
            _appScope = appScope;
            _navigator = navigator;
            _saveRepository = saveRepository;
        }

        public void StartNewCareer(CareerState newCareer)
        {
            if (IsCareerActive)
                return;

            _saveRepository.Save(newCareer);
            OpenCareer(newCareer);
        }

        public SaveLoadStatus ContinueCareer()
        {
            if (IsCareerActive)
                return SaveLoadStatus.Success;

            var status = _saveRepository.TryLoad(out var state);
            if (status == SaveLoadStatus.Success)
                OpenCareer(state);

            return status;
        }

        public void EndCareer()
        {
            if (!IsCareerActive)
                return;

            _saveRepository.Save(_career);

            // Pop Hub and any screens above it first (their scopes are children
            // of the Game scope), then dispose the Game scope itself.
            _navigator.PopToRoot();
            _navigator.SetActiveScope(_appScope);
            _gameScope.Dispose();
            _gameScope = null;
            _career = null;
        }

        public void Dispose()
        {
            _gameScope?.Dispose();
            _gameScope = null;
        }

        private void OpenCareer(CareerState career)
        {
            _career = career;
            _gameScope = _appScope.CreateChild(builder =>
            {
                builder.RegisterInstance(career);
                builder.Register<IGameClock, LocalClock>(Lifetime.Singleton);
                builder.Register<UserMatchLog>(Lifetime.Singleton);
                builder.Register<UserMatchContextHolder>(Lifetime.Singleton);
                builder.Register<PlayerProfileTarget>(Lifetime.Singleton);
                builder.Register<SeasonService>(Lifetime.Singleton);
            });
            _gameScope.name = "GameScope";

            _navigator.SetActiveScope(_gameScope);
            _navigator.Push<HubPresenter>();
        }
    }
}
