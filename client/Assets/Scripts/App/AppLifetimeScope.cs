using Fts.Services;
using Fts.Services.Messaging;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Fts.App
{
    /// <summary>
    /// Root (App) DI scope, placed in the Boot scene.
    /// Scope hierarchy: App → Game (active career) → Screen (one per pushed screen).
    /// </summary>
    public sealed class AppLifetimeScope : LifetimeScope
    {
        [SerializeField] private UIDocument uiDocument;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(uiDocument);

            builder.Register<IMessageBroker, MessageBroker>(Lifetime.Singleton);
            builder.Register<ScreenNavigator>(Lifetime.Singleton);
            builder.Register<OverlayHost>(Lifetime.Singleton);
            builder.Register<ISaveRepository, LocalJsonSaveRepository>(Lifetime.Singleton);
            builder.Register<CareerFactory>(Lifetime.Singleton);
            builder.Register<ILocalizationService, LocalizationService>(Lifetime.Singleton);
            builder.Register<IGameSessionService, GameSessionService>(Lifetime.Singleton)
                   .WithParameter<LifetimeScope>(this);

            builder.RegisterEntryPoint<AppEntryPoint>();

            // The navigator's default parent scope for Screen scopes is the App scope.
            builder.RegisterBuildCallback(container =>
                container.Resolve<ScreenNavigator>().SetActiveScope(this));
        }
    }
}
