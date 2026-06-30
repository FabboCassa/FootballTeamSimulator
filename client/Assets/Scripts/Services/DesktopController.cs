using System;
using Fts.Services.Messaging;
using Fts.Services.Navigation;
using UnityEngine;
using VContainer.Unity;

namespace Fts.Services
{
    /// <summary>
    /// Desktop keyboard/mouse niceties (Roadmap 6.5 — "keyboard/mouse niceties; alt-tab/resize safe").
    ///
    /// Three things, all desktop-standalone only (and the Editor for testing); on mobile/WebGL this
    /// is inert because the gate below is false and the keys aren't pressed:
    ///   * keeps the player responsive in the background (alt-tab safe) — also set in ProjectSettings,
    ///     done here too as belt-and-suspenders;
    ///   * a fullscreen toggle (F11 or Alt+Enter) that flips between a windowed (resizable) player and
    ///     borderless fullscreen, restoring the previous window size on the way back;
    ///   * Hub navigation hotkeys: it publishes a <see cref="HubShortcutMessage"/> that ONLY the Hub
    ///     acts on, and only while the Hub is the top screen with no modal open — so a hotkey can never
    ///     push a screen onto the wrong place. Mouse-wheel scrolling already works on every screen
    ///     (each long screen is a UI Toolkit ScrollView), so there's nothing to add for it.
    ///
    /// The active-screen name is tracked the same way <see cref="FrameRateController"/> does it.
    /// </summary>
    public sealed class DesktopController : IStartable, ITickable, IDisposable
    {
        /// <summary>Hub presenter type name — shortcuts are only published while this screen is on top.</summary>
        private const string HubScreen = "HubPresenter";

        private const int DefaultWindowedWidth = 1600;
        private const int DefaultWindowedHeight = 900;

        private readonly IMessageBroker _broker;
        private readonly OverlayHost _overlay;
        private IDisposable _screenSub;
        private string _currentScreen = string.Empty;
        private bool _desktop;
        private int _windowedWidth = DefaultWindowedWidth;
        private int _windowedHeight = DefaultWindowedHeight;

        public DesktopController(IMessageBroker broker, OverlayHost overlay)
        {
            _broker = broker;
            _overlay = overlay;
        }

        public void Start()
        {
            _desktop =
                Application.platform == RuntimePlatform.WindowsPlayer ||
                Application.platform == RuntimePlatform.OSXPlayer ||
                Application.platform == RuntimePlatform.LinuxPlayer ||
                Application.isEditor;

            if (_desktop)
                Application.runInBackground = true; // alt-tab safe

            _screenSub = _broker.Subscribe<ScreenChangedMessage>(m => _currentScreen = m.ScreenName);
        }

        public void Tick()
        {
            if (!_desktop)
                return;

            if (FullscreenTogglePressed())
                ToggleFullscreen();

            // Hub hotkeys only while the Hub is on top and nothing modal is covering it.
            if (_currentScreen == HubScreen && !_overlay.IsModalOpen)
                PollHubShortcuts();
        }

        private static bool FullscreenTogglePressed()
        {
            if (Input.GetKeyDown(KeyCode.F11))
                return true;

            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            return alt && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter));
        }

        private void ToggleFullscreen()
        {
            if (Screen.fullScreen)
            {
                Screen.SetResolution(_windowedWidth, _windowedHeight, FullScreenMode.Windowed);
            }
            else
            {
                // Remember the windowed size so we can come back to it.
                if (Screen.width > 0 && Screen.height > 0)
                {
                    _windowedWidth = Screen.width;
                    _windowedHeight = Screen.height;
                }

                Resolution r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
        }

        private void PollHubShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.Space)) Publish(HubShortcut.AdvanceDay);
            else if (Input.GetKeyDown(KeyCode.N)) Publish(HubShortcut.NextMatch);
            else if (Input.GetKeyDown(KeyCode.I)) Publish(HubShortcut.Inbox);
            else if (Input.GetKeyDown(KeyCode.Alpha1)) Publish(HubShortcut.Squad);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) Publish(HubShortcut.Tactics);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) Publish(HubShortcut.Training);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) Publish(HubShortcut.Support);
            else if (Input.GetKeyDown(KeyCode.Alpha5)) Publish(HubShortcut.Market);
            else if (Input.GetKeyDown(KeyCode.Alpha6)) Publish(HubShortcut.Scouting);
            else if (Input.GetKeyDown(KeyCode.Alpha7)) Publish(HubShortcut.Club);
            else if (Input.GetKeyDown(KeyCode.Alpha8)) Publish(HubShortcut.Career);
            else if (Input.GetKeyDown(KeyCode.Alpha9)) Publish(HubShortcut.League);
        }

        private void Publish(HubShortcut action) => _broker.Publish(new HubShortcutMessage(action));

        public void Dispose()
        {
            _screenSub?.Dispose();
            _screenSub = null;
        }
    }
}
