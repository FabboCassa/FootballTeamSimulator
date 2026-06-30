using System;
using Fts.Services.Messaging;
using Fts.Services.Navigation;
using UnityEngine;
using VContainer.Unity;

namespace Fts.Services
{
    /// <summary>
    /// Adaptive frame-rate governor (Roadmap 6.4 — "60fps + idle adaptive").
    ///
    /// A management game is mostly a static, retained-mode UI, so running the GPU
    /// flat out at 60fps while nothing moves just drains battery and heats the
    /// phone. This caps to <see cref="HighFps"/> while the player is actively
    /// interacting OR watching a match (the renderer animates then), and drops to
    /// <see cref="LowFps"/> after <see cref="IdleSeconds"/> of no input on a static
    /// screen. Any touch/click/key/mouse-move instantly restores the high rate.
    ///
    /// On mobile vSync is disabled so Application.targetFrameRate is honoured; on
    /// desktop the target is set too (ignored under vSync, harmless).
    /// </summary>
    public sealed class FrameRateController : IStartable, ITickable, IDisposable
    {
        private const int HighFps = 60;
        private const int LowFps = 30;
        private const float IdleSeconds = 8f;

        /// <summary>Screen that animates continuously and must stay at the high rate.</summary>
        private const string MatchWatchScreen = "MatchWatchScreenPresenter";

        private readonly IMessageBroker _broker;
        private IDisposable _screenSub;
        private string _currentScreen = string.Empty;
        private Vector3 _lastMousePos;
        private float _lastActivity;
        private int _appliedTarget = -1;

        public FrameRateController(IMessageBroker broker)
        {
            _broker = broker;
        }

        public void Start()
        {
            if (Application.isMobilePlatform)
                QualitySettings.vSyncCount = 0; // let targetFrameRate take effect

            _lastMousePos = Input.mousePosition;
            _lastActivity = Time.unscaledTime;

            _screenSub = _broker.Subscribe<ScreenChangedMessage>(m => _currentScreen = m.ScreenName);

            Apply(HighFps);
        }

        public void Tick()
        {
            if (HasInput())
                _lastActivity = Time.unscaledTime;

            bool watchingMatch = _currentScreen == MatchWatchScreen;
            bool recentlyActive = (Time.unscaledTime - _lastActivity) < IdleSeconds;

            Apply(watchingMatch || recentlyActive ? HighFps : LowFps);
        }

        private bool HasInput()
        {
            if (Input.touchCount > 0)
                return true;
            if (Input.anyKey || Input.GetMouseButton(0))
                return true;

            Vector3 mouse = Input.mousePosition;
            if ((mouse - _lastMousePos).sqrMagnitude > 1f)
            {
                _lastMousePos = mouse;
                return true;
            }

            return false;
        }

        private void Apply(int target)
        {
            if (target == _appliedTarget)
                return;

            _appliedTarget = target;
            Application.targetFrameRate = target;
        }

        public void Dispose()
        {
            _screenSub?.Dispose();
            _screenSub = null;
        }
    }
}
