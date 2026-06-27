using System;
using Fts.Presenters;
using Fts.Services;
using Fts.Services.Messaging;
using Fts.Services.Navigation;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Fts.App
{
    /// <summary>
    /// App startup: wires the navigator to the UI root, shows the main menu,
    /// and routes Esc / Android hardware back to the navigator.
    /// </summary>
    public sealed class AppEntryPoint : IStartable, ITickable, IDisposable
    {
        private readonly ScreenNavigator _navigator;
        private readonly UIDocument _uiDocument;
        private readonly IMessageBroker _broker;
        private readonly OverlayHost _overlay;
        private IDisposable _subscription;

        public AppEntryPoint(ScreenNavigator navigator, UIDocument uiDocument, IMessageBroker broker, OverlayHost overlay)
        {
            _navigator = navigator;
            _uiDocument = uiDocument;
            _broker = broker;
            _overlay = overlay;
        }

        public void Start()
        {
            _navigator.SetRoot(_uiDocument.rootVisualElement);
            _overlay.SetRoot(_uiDocument.rootVisualElement);

            _subscription = _broker.Subscribe<ScreenChangedMessage>(m =>
                Debug.Log($"[Nav] {m.ScreenName} (stack depth {m.StackDepth})"));

            _navigator.Push<MainMenuPresenter>();
        }

        public void Tick()
        {
            // KeyCode.Escape is also the Android hardware back button.
            if (Input.GetKeyDown(KeyCode.Escape))
                _navigator.Back();
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
