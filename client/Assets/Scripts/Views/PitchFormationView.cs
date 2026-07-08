using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A token to place on the visual pitch (task 6.7). Positions are normalized:
    /// X in [0,1] runs goal-to-goal (attacking side to the right, before any mirror),
    /// Y in [0,1] runs touchline-to-touchline. Dumb data — the presenter fills it.</summary>
    public sealed class PitchTokenVm
    {
        public int SlotIndex = -1;      // lineup slot this token sits on (-1 = read-only preview)
        public int PlayerId = -1;       // drag payload / -1 when unknown
        public float X;                 // normalized length (0..1)
        public float Y;                 // normalized width  (0..1)
        public string Badge = string.Empty; // text inside the disc (OVR or role abbr)
        public string Name = string.Empty;  // caption under the disc
        public int Fitness = -1;        // 0..100 fitness ring; -1 hides it
        public Color Fill = Color.gray;
        public Color Text = Color.white;
        public bool Selected;
    }

    /// <summary>A built token: the pitch is a pure renderer, so an external controller
    /// (the Squad screen) reads these handles to wire drag/tap onto the disc element.</summary>
    public sealed class PitchToken
    {
        public VisualElement Element;   // the pickable disc+caption column
        public int SlotIndex;
        public int PlayerId;
    }

    /// <summary>
    /// Reusable top-down formation pitch (task 6.7). Paints the pitch with the shared
    /// <see cref="PitchGraphics"/> (same look as the 3.1 match renderer) and lays the
    /// player tokens as real VisualElement discs on top so they can be tapped/dragged.
    /// This class only RENDERS and exposes hit-testing + the token handles; interaction
    /// (drag, tap, ghost) is layered on by the owner, which keeps the pitch reusable for
    /// the read-only Tactics/opponent previews. No Sim.Core reference (dumb view).
    /// </summary>
    public sealed class PitchFormationView : VisualElement
    {
        private static readonly Color CaptionColor = new Color(1f, 1f, 1f, 0.9f);

        private readonly bool _mirror;
        private readonly VisualElement _canvas;
        private readonly VisualElement _overlay;
        private readonly List<PitchToken> _tokens = new List<PitchToken>();
        private IReadOnlyList<PitchTokenVm> _vms = new List<PitchTokenVm>();
        private float _tokenD = 34f;

        /// <summary>The built token handles (valid after <see cref="SetTokens"/>).</summary>
        public IReadOnlyList<PitchToken> Tokens => _tokens;

        public PitchFormationView(bool mirror)
        {
            _mirror = mirror;
            style.flexGrow = 1f;
            style.minHeight = 180;
            style.overflow = Overflow.Hidden;

            // Painter2D turf/markings, behind the tokens; never intercepts pointers.
            _canvas = new VisualElement();
            _canvas.style.position = Position.Absolute;
            _canvas.style.left = 0; _canvas.style.top = 0; _canvas.style.right = 0; _canvas.style.bottom = 0;
            _canvas.pickingMode = PickingMode.Ignore;
            _canvas.generateVisualContent += OnPaint;
            Add(_canvas);

            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.top = 0; _overlay.style.right = 0; _overlay.style.bottom = 0;
            _overlay.pickingMode = PickingMode.Ignore; // its token children still pick
            Add(_overlay);

            RegisterCallback<GeometryChangedEvent>(_ => { _canvas.MarkDirtyRepaint(); LayoutTokens(); });
        }

        /// <summary>Rebuilds the token discs from the view-models and lays them out.</summary>
        public void SetTokens(IReadOnlyList<PitchTokenVm> tokens)
        {
            _vms = tokens ?? new List<PitchTokenVm>();
            _overlay.Clear();
            _tokens.Clear();

            foreach (PitchTokenVm vm in _vms)
            {
                VisualElement col = BuildToken(vm);
                _overlay.Add(col);
                _tokens.Add(new PitchToken { Element = col, SlotIndex = vm.SlotIndex, PlayerId = vm.PlayerId });
            }

            LayoutTokens();
        }

        /// <summary>The lineup slot nearest to a panel-space point, or -1 if none is close.</summary>
        public int HitTestSlot(Vector2 panelPosition)
        {
            float best = _tokenD * 1.5f;
            int slot = -1;
            foreach (PitchToken t in _tokens)
            {
                if (t.SlotIndex < 0)
                    continue;
                Rect wb = t.Element.worldBound;
                var center = new Vector2(wb.center.x, wb.y + _tokenD * 0.5f);
                float d = Vector2.Distance(panelPosition, center);
                if (d < best) { best = d; slot = t.SlotIndex; }
            }

            return slot;
        }

        /// <summary>
        /// The nearest token belonging to a DIFFERENT player, within
        /// <paramref name="radiusFactor"/>×token diameter of the point — a swap target
        /// (task 6.10). Returns its slot index, or -1 when the drop is on open turf
        /// (so the owner can reposition the dragged player instead of swapping).
        /// </summary>
        public int NearestOtherToken(Vector2 panelPosition, int excludePlayerId, float radiusFactor)
        {
            float best = _tokenD * radiusFactor;
            int slot = -1;
            foreach (PitchToken t in _tokens)
            {
                if (t.SlotIndex < 0 || t.PlayerId == excludePlayerId)
                    continue;
                Rect wb = t.Element.worldBound;
                var center = new Vector2(wb.center.x, wb.y + _tokenD * 0.5f);
                float d = Vector2.Distance(panelPosition, center);
                if (d < best) { best = d; slot = t.SlotIndex; }
            }

            return slot;
        }

        /// <summary>
        /// Maps a panel-space point to normalized pitch coords (the inverse of the token
        /// layout), for free positioning (task 6.10). X runs own-goal→attacked-goal (before
        /// the mirror is undone), Y touchline→touchline, both clamped to [0,1]. onPitch is
        /// false when the point falls well outside the letterboxed pitch.
        /// </summary>
        public (bool onPitch, float x, float y) ToNormalized(Vector2 panelPosition)
        {
            Rect wb = worldBound;
            Rect fit = PitchGraphics.FitRect(wb.width, wb.height);
            float lx = panelPosition.x - wb.x;
            float ly = panelPosition.y - wb.y;
            float fx = fit.width > 0f ? (lx - fit.x) / fit.width : 0.5f;
            float fy = fit.height > 0f ? (ly - fit.y) / fit.height : 0.5f;

            bool onPitch = fx >= -0.05f && fx <= 1.05f && fy >= -0.05f && fy <= 1.05f;
            float nx = _mirror ? 1f - fx : fx;
            return (onPitch, Mathf.Clamp01(nx), Mathf.Clamp01(fy));
        }

        private void OnPaint(MeshGenerationContext mgc)
        {
            Rect r = contentRect;
            if (r.width <= 1f || r.height <= 1f)
                return;
            PitchGraphics.Draw(mgc.painter2D, PitchGraphics.FitRect(r.width, r.height));
        }

        private void LayoutTokens()
        {
            Rect r = contentRect;
            if (r.width <= 1f || r.height <= 1f || _tokens.Count == 0)
                return;

            Rect fit = PitchGraphics.FitRect(r.width, r.height);
            _tokenD = Mathf.Clamp(Mathf.Min(fit.width, fit.height) * 0.115f, 20f, 46f);
            float colW = _tokenD * 2.6f;

            for (int i = 0; i < _tokens.Count && i < _vms.Count; i++)
            {
                PitchTokenVm vm = _vms[i];
                VisualElement col = _tokens[i].Element;
                Vector2 c = PitchGraphics.ToPixel(fit, vm.X, vm.Y, _mirror);

                col.style.width = colW;
                col.style.left = c.x - colW * 0.5f;
                col.style.top = c.y - _tokenD * 0.5f;

                // The disc is the first child; resize it to the layout-derived diameter.
                VisualElement disc = col.ElementAt(0);
                disc.style.width = _tokenD;
                disc.style.height = _tokenD;
                UiKit.Round(disc, _tokenD * 0.5f);
                var badge = (Label)disc.ElementAt(0);
                badge.style.fontSize = Mathf.Round(_tokenD * 0.42f);
            }
        }

        private VisualElement BuildToken(PitchTokenVm vm)
        {
            var col = new VisualElement();
            col.style.position = Position.Absolute;
            col.style.alignItems = Align.Center;
            col.pickingMode = vm.SlotIndex >= 0 ? PickingMode.Position : PickingMode.Ignore;

            var disc = new VisualElement();
            disc.style.width = _tokenD;
            disc.style.height = _tokenD;
            disc.style.backgroundColor = vm.Fill;
            disc.style.alignItems = Align.Center;
            disc.style.justifyContent = Justify.Center;
            disc.pickingMode = PickingMode.Ignore;
            UiKit.Round(disc, _tokenD * 0.5f);
            UiKit.SetBorder(disc, vm.Selected ? UiKit.Accent : new Color(0f, 0f, 0f, 0.45f), vm.Selected ? 3f : 1.5f);

            var badge = new Label(vm.Badge ?? string.Empty);
            badge.style.color = vm.Text;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.pickingMode = PickingMode.Ignore;
            disc.Add(badge);
            col.Add(disc);

            if (vm.Fitness >= 0)
            {
                var bar = new VisualElement();
                bar.style.width = _tokenD;
                bar.style.height = 4;
                bar.style.marginTop = 2;
                bar.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
                bar.pickingMode = PickingMode.Ignore;
                UiKit.Round(bar, 2f);
                var fill = new VisualElement();
                int f = vm.Fitness > 100 ? 100 : vm.Fitness;
                fill.style.height = Length.Percent(100);
                fill.style.width = Length.Percent(f);
                fill.style.backgroundColor = f >= 70
                    ? new Color(0.45f, 0.80f, 0.45f)
                    : f >= 40 ? new Color(0.90f, 0.78f, 0.35f) : new Color(0.88f, 0.40f, 0.36f);
                UiKit.Round(fill, 2f);
                fill.pickingMode = PickingMode.Ignore;
                bar.Add(fill);
                col.Add(bar);
            }

            if (!string.IsNullOrEmpty(vm.Name))
            {
                var name = new Label(vm.Name);
                name.style.color = CaptionColor;
                name.style.fontSize = 11;
                name.style.marginTop = 1;
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                name.style.whiteSpace = WhiteSpace.NoWrap;
                name.style.textOverflow = TextOverflow.Ellipsis;
                name.style.maxWidth = _tokenD * 2.6f;
                name.style.overflow = Overflow.Hidden;
                name.pickingMode = PickingMode.Ignore;
                col.Add(name);
            }

            return col;
        }
    }
}
