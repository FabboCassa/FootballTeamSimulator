using UnityEngine;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Fts.Services
{
    /// <summary>
    /// Insets the UI root by the device safe area (Roadmap 6.4) so content clears
    /// notches, punch-holes, the status bar and rounded corners in BOTH orientations.
    ///
    /// The player renders edge-to-edge (androidRenderOutsideSafeArea = true / iOS
    /// full screen); we pad the root here instead. The root background is set to the
    /// app colour so the padded border matches the screen behind the cutout rather
    /// than showing a transparent gap. Re-applies whenever the safe area or the panel
    /// geometry changes (first layout, a device rotation, the status bar appearing).
    ///
    /// Outside the player (Editor desktop, WebGL desktop) Screen.safeArea equals the
    /// full screen, so the padding is zero and nothing changes.
    /// </summary>
    public sealed class SafeAreaController : IStartable, ITickable
    {
        // Mirrors Fts.Views.UiKit.Background (0x141C30). Hardcoded because the Services
        // layer does not reference the Views layer; keep in sync with the UiKit token.
        private static readonly Color AppBackground = new Color(0x14 / 255f, 0x1C / 255f, 0x30 / 255f);

        private readonly UIDocument _uiDocument;
        private VisualElement _root;
        private Rect _lastSafeArea = new Rect(float.NaN, float.NaN, float.NaN, float.NaN);
        private int _lastWidth = -1;
        private int _lastHeight = -1;

        public SafeAreaController(UIDocument uiDocument)
        {
            _uiDocument = uiDocument;
        }

        public void Start()
        {
            _root = _uiDocument != null ? _uiDocument.rootVisualElement : null;
            if (_root == null)
                return;

            _root.style.backgroundColor = AppBackground;
            // Re-apply once the panel has a resolved size and on every later layout
            // change (rotation), so the pixel→point conversion uses real dimensions.
            _root.RegisterCallback<GeometryChangedEvent>(_ => Apply());
            Apply();
        }

        public void Tick()
        {
            if (_root == null)
                return;

            // Catches safe-area changes that don't move geometry (e.g. the status
            // bar toggling); rotations are also caught here in addition to the
            // geometry callback.
            if (Screen.safeArea != _lastSafeArea || Screen.width != _lastWidth || Screen.height != _lastHeight)
                Apply();
        }

        private void Apply()
        {
            _lastSafeArea = Screen.safeArea;
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;

            if (_lastWidth <= 0 || _lastHeight <= 0)
                return;

            Rect sa = _lastSafeArea;

            // Safe-area insets in screen PIXELS (Screen.safeArea origin is bottom-left).
            float leftPx = Mathf.Max(0f, sa.xMin);
            float rightPx = Mathf.Max(0f, _lastWidth - sa.xMax);
            float topPx = Mathf.Max(0f, _lastHeight - sa.yMax);
            float bottomPx = Mathf.Max(0f, sa.yMin);

            // UI Toolkit lays out in POINTS (PanelSettings scales pixels → points).
            // Convert with the resolved panel size; fall back to 1:1 before the first
            // layout resolves (the GeometryChangedEvent re-runs this with real sizes).
            float panelW = _root.resolvedStyle.width;
            float panelH = _root.resolvedStyle.height;
            float ratioX = (panelW > 1f) ? panelW / _lastWidth : 1f;
            float ratioY = (panelH > 1f) ? panelH / _lastHeight : 1f;

            _root.style.paddingLeft = leftPx * ratioX;
            _root.style.paddingRight = rightPx * ratioX;
            _root.style.paddingTop = topPx * ratioY;
            _root.style.paddingBottom = bottomPx * ratioY;
        }
    }
}
