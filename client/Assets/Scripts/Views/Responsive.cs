using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>Which shape the UI is currently drawing itself at (Roadmap task 14.1).</summary>
    public enum Viewport
    {
        /// <summary>A phone held upright: one column, big touch targets, a bottom action bar.</summary>
        Mobile,
        /// <summary>A tablet or a small window: two columns, desktop type at mobile spacing.</summary>
        Tablet,
        /// <summary>A desktop window: the full multi-column layout.</summary>
        Desktop
    }

    /// <summary>
    /// The one place that answers "are we on a phone right now?" (Roadmap task 14.1).
    ///
    /// WHY THIS EXISTS AT ALL. USS has no media queries, so a responsive UI Toolkit app has to
    /// carry its breakpoint as a CLASS on the panel root and let descendant selectors do the rest
    /// (<c>.fts--mobile .fts-btn { ... }</c>). That is what <see cref="Attach"/> does: it watches
    /// the root's geometry and keeps exactly one of <c>fts--mobile</c> / <c>fts--tablet</c> /
    /// <c>fts--desktop</c> on it. Because it is a class and not inline style, every screen in the
    /// game re-lays-out the instant the window crosses a breakpoint — no rebuild, no navigator
    /// round-trip, nothing for a view to subscribe to.
    ///
    /// WHY THE BREAKPOINT IS NOT JUST A WIDTH. The panel is set to ScaleWithScreenSize against a
    /// 1920x1080 reference with match 0.5, so a portrait phone does NOT report a small width: a
    /// 1080x2400 handset resolves to roughly 970x2150 UI POINTS. AppShell's old
    /// <c>width &lt; 720</c> portrait test therefore never fired on a real phone — which is why the
    /// bottom tab bar only ever appeared in a narrow editor window. The test that actually works is
    /// the ASPECT RATIO, with a width test kept as the secondary signal for a squeezed desktop
    /// window.
    ///
    /// WHAT <see cref="Ui"/> IS FOR. One UI point is not one mockup pixel, and the ratio is not the
    /// same on both shapes: the desktop artboard is ~1440 px wide against ~1920 points, the phone
    /// artboard 390 px against ~970. Sizes therefore live in FtsTheme.uss already multiplied per
    /// breakpoint (that is why the mobile numbers there look enormous), and <see cref="Ui"/> is
    /// exposed for the few places that must compute a size in C# — a pitch, a chart, a crest.
    /// </summary>
    public static class Responsive
    {
        /// <summary>Portrait-ish from here up (height / width). A 16:9 phone is 1.78.</summary>
        private const float MobileAspect = 1.15f;
        /// <summary>A landscape window this narrow is treated as a phone too.</summary>
        private const float MobileMaxWidth = 760f;
        /// <summary>Below this the layout drops its third column.</summary>
        private const float TabletMaxWidth = 1180f;

        /// <summary>Width in UI points the desktop mockup was drawn against.</summary>
        private const float DesktopDesignWidth = 1440f;
        /// <summary>Width in UI points the phone mockup was drawn against.</summary>
        private const float MobileDesignWidth = 390f;

        public const string MobileClass = "fts--mobile";
        public const string TabletClass = "fts--tablet";
        public const string DesktopClass = "fts--desktop";

        /// <summary>The shape the last measured root resolved to. Defaults to desktop before the first layout.</summary>
        public static Viewport Current { get; private set; } = Viewport.Desktop;

        /// <summary>True while <see cref="Current"/> is <see cref="Viewport.Mobile"/>.</summary>
        public static bool IsMobile => Current == Viewport.Mobile;

        /// <summary>UI points per mockup pixel for the current shape. See the class remarks.</summary>
        public static float Ui { get; private set; } = 1.35f;

        /// <summary>Raised after <see cref="Current"/> changes, for the handful of views that build different TREES (not just different sizes) per shape.</summary>
        public static event Action<Viewport> Changed;

        private static VisualElement _root;

        /// <summary>
        /// Starts watching <paramref name="root"/> (the UIDocument root) and stamps the breakpoint
        /// class on it. Call once at startup, right after <see cref="UiKit.Attach"/>.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            if (root == null || ReferenceEquals(root, _root))
                return;

            _root = root;
            root.RegisterCallback<GeometryChangedEvent>(_ => Measure(root));
            Measure(root);
        }

        /// <summary>Re-reads the root's size and re-stamps the class. Exposed for tests and for the editor's play-mode resolution switcher.</summary>
        public static void Measure(VisualElement root)
        {
            if (root == null)
                return;

            float width = root.resolvedStyle.width;
            float height = root.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 1f) width = Screen.width;
            if (float.IsNaN(height) || height <= 1f) height = Screen.height;

            Viewport next = Classify(width, height);
            Ui = next == Viewport.Mobile ? width / MobileDesignWidth : width / DesktopDesignWidth;

            root.EnableInClassList(MobileClass, next == Viewport.Mobile);
            root.EnableInClassList(TabletClass, next == Viewport.Tablet);
            root.EnableInClassList(DesktopClass, next == Viewport.Desktop);

            if (next == Current)
                return;

            Current = next;
            Changed?.Invoke(next);
        }

        /// <summary>The breakpoint rule, pure so it can be unit-tested without a panel.</summary>
        public static Viewport Classify(float width, float height)
        {
            if (width <= 0f) return Viewport.Desktop;

            float aspect = height / width;
            if (aspect >= MobileAspect || width < MobileMaxWidth)
                return Viewport.Mobile;

            return width < TabletMaxWidth ? Viewport.Tablet : Viewport.Desktop;
        }
    }
}
