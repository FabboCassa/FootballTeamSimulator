using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A bench/roster row beside the pitch (task 6.7). Role, age and rating are kept
    /// as separate fields so the row can lay them out in distinct columns (no money — that
    /// lives only on the Market screen).</summary>
    public sealed class BenchRowVm
    {
        public int PlayerId;
        public string Role;
        public int RoleGroup;   // 0 GK · 1 def · 2 mid · 3 att → reparto colour
        public string Name;
        public int Age;
        public int Rating;
        public string FormArrow;
        public string MoraleFace;
        public int Fitness;
        public string Tooltip;
        public bool Selected;
    }

    /// <summary>The player currently picked up (shown in the selection bar).</summary>
    public sealed class SelectionVm
    {
        public int PlayerId;
        public string Text;
    }

    /// <summary>
    /// Squad screen (task 6.7): a graphical top-down pitch with the XI as tokens plus a
    /// bench list. You build the lineup on the pitch — drag a player onto a slot (from the
    /// pitch or the bench) to place/swap, or tap to select then tap a target (a keyboard-free
    /// fallback). A selection bar exposes the picked player's profile. Dumb view: the presenter
    /// computes positions, kit colours, ratings and condition; this only renders and reports
    /// gestures. Duplicates are impossible — the presenter's assign always swaps.
    /// </summary>
    public sealed class SquadView
    {
        private const float StackBreakpoint = 640f;

        public event Action<int> SlotTapped;        // lineup slot index
        public event Action<int> BenchTapped;       // player id
        public event Action<int, int> AssignRequested; // (slot index, player id) — dropped onto another token (swap)
        public event Action<int, float, float> RepositionRequested; // (player id, normX, normY) — dropped on open turf (task 6.10)
        public event Action<int> ProfileClicked;    // player id
        public event Action AutoClicked;
        public event Action SaveClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly PitchFormationView _pitch;
        private readonly VisualElement _content;
        private readonly VisualElement _pitchWrap;
        private readonly VisualElement _benchColumn;
        private readonly ScrollView _benchList;
        private readonly Label _header;
        private readonly Label _status;
        private readonly VisualElement _selectionBar;
        private readonly Label _selectionLabel;
        private readonly Button _profileButton;
        private readonly Label _hint;

        private readonly Label _ghost;
        private VisualElement _dragEl;
        private int _dragPlayerId = -1;
        private Vector2 _dragStart;
        private bool _dragging;
        private Func<string> _ghostText;
        private Action _onTap;
        private bool _stacked;
        private int _selectedPlayerId = -1;
        private readonly Dictionary<int, string> _nameCache = new Dictionary<int, string>();

        public SquadView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = 10;
            Root.style.paddingBottom = 10;
            Root.style.paddingLeft = 14;
            Root.style.paddingRight = 14;

            _header = UiKit.Subtitle(string.Empty);
            _header.style.marginBottom = 4;
            _header.style.unityTextAlign = TextAnchor.MiddleLeft;
            Root.Add(_header);

            // Selection bar: shows the picked player + a profile shortcut, or a hint.
            _selectionBar = UiKit.Row();
            _selectionBar.style.marginBottom = 6;
            _selectionBar.style.minHeight = 30;
            _selectionLabel = new Label(string.Empty);
            _selectionLabel.style.color = UiKit.TextPrimary;
            _selectionLabel.style.fontSize = 14;
            _selectionLabel.style.flexGrow = 1f;
            _selectionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _selectionBar.Add(_selectionLabel);
            _profileButton = UiKit.MenuButton(tr("squad.profile"), () =>
            {
                if (_selectedPlayerId >= 0) ProfileClicked?.Invoke(_selectedPlayerId);
            });
            _profileButton.style.width = 110;
            _profileButton.style.height = 34;
            _profileButton.style.fontSize = 13;
            _selectionBar.Add(_profileButton);
            Root.Add(_selectionBar);

            _hint = UiKit.Caption(tr("squad.pitch_hint"));
            _hint.style.marginBottom = 6;
            Root.Add(_hint);

            _content = new VisualElement();
            _content.style.flexDirection = FlexDirection.Row;
            _content.style.flexGrow = 1f;
            Root.Add(_content);

            _pitchWrap = new VisualElement();
            _pitchWrap.style.flexGrow = 1f;
            _pitchWrap.style.marginRight = 8;
            _pitch = new PitchFormationView(mirror: false);
            _pitchWrap.Add(_pitch);
            _content.Add(_pitchWrap);

            _benchColumn = new VisualElement();
            _benchColumn.style.width = new Length(34, LengthUnit.Percent);
            _benchColumn.style.minWidth = 150;
            var benchCaption = new Label(tr("squad.bench_caption"));
            benchCaption.style.color = UiKit.TextMuted;
            benchCaption.style.fontSize = 13;
            benchCaption.style.marginBottom = 4;
            _benchColumn.Add(benchCaption);
            _benchList = new ScrollView();
            _benchList.style.flexGrow = 1f;
            _benchColumn.Add(_benchList);
            _content.Add(_benchColumn);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 8;
            footer.Add(FooterButton(tr("squad.auto_pick"), () => AutoClicked?.Invoke()));
            footer.Add(FooterButton(tr("squad.save_lineup"), () => SaveClicked?.Invoke()));
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            Root.Add(footer);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = 4;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            Root.Add(_status);

            _ghost = new Label(string.Empty);
            _ghost.style.position = Position.Absolute;
            _ghost.style.display = DisplayStyle.None;
            _ghost.pickingMode = PickingMode.Ignore;
            _ghost.style.backgroundColor = UiKit.Accent;
            _ghost.style.color = UiKit.TextOnAccent;
            _ghost.style.fontSize = 13;
            _ghost.style.unityFontStyleAndWeight = FontStyle.Bold;
            _ghost.style.paddingLeft = 8;
            _ghost.style.paddingRight = 8;
            _ghost.style.paddingTop = 3;
            _ghost.style.paddingBottom = 3;
            UiKit.Round(_ghost, 8);
            Root.Add(_ghost);

            SetSelection(null);
            Root.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsive());
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetStatus(string text) => _status.text = text;

        public void SetTokens(IReadOnlyList<PitchTokenVm> tokens)
        {
            _pitch.SetTokens(tokens);
            foreach (PitchTokenVm vm in tokens)
                if (vm.PlayerId >= 0)
                    _nameCache[vm.PlayerId] = vm.Name;

            foreach (PitchToken t in _pitch.Tokens)
            {
                int slot = t.SlotIndex;
                int playerId = t.PlayerId;
                WireDrag(t.Element, playerId, () => Ghost(playerId), () => SlotTapped?.Invoke(slot));
            }
        }

        public void SetBench(IReadOnlyList<BenchRowVm> rows)
        {
            _benchList.Clear();
            foreach (BenchRowVm vm in rows)
            {
                int playerId = vm.PlayerId;
                _nameCache[playerId] = vm.Name;
                VisualElement row = BenchRow(vm);
                WireDrag(row, playerId, () => Ghost(playerId), () => BenchTapped?.Invoke(playerId));
                _benchList.Add(row);
            }
        }

        private string Ghost(int playerId) =>
            _nameCache.TryGetValue(playerId, out string s) ? s : string.Empty;

        /// <summary>Shows the picked player in the selection bar, or the hint when nothing is picked.</summary>
        public void SetSelection(SelectionVm selection)
        {
            _selectedPlayerId = selection?.PlayerId ?? -1;
            bool has = selection != null;
            _selectionBar.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            _hint.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            if (has)
                _selectionLabel.text = selection.Text;
        }

        private VisualElement BenchRow(BenchRowVm vm)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 34;
            row.style.marginBottom = 2;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 6;
            row.style.backgroundColor = vm.Selected ? UiKit.SurfaceAlt : new Color(1f, 1f, 1f, 0.05f);
            UiKit.Round(row, 6);
            row.tooltip = vm.Tooltip ?? string.Empty;

            // Distinct columns: [role] [name···] [age] [rating] then the condition strip.
            var role = new Label(vm.Role);
            role.style.width = 34;
            role.style.fontSize = 11;
            role.style.unityFontStyleAndWeight = FontStyle.Bold;
            role.style.color = PlayerRowKit.RoleColor(vm.RoleGroup); // reparto colour (task 6.9)
            role.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(role);

            var name = new Label(vm.Name);
            name.style.flexGrow = 1f;
            name.style.fontSize = 12;
            name.style.color = UiKit.TextPrimary;
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.textOverflow = TextOverflow.Ellipsis;
            name.style.overflow = Overflow.Hidden;
            row.Add(name);

            var age = new Label(vm.Age.ToString());
            age.style.width = 26;
            age.style.fontSize = 11;
            age.style.color = UiKit.TextMuted;
            age.style.unityTextAlign = TextAnchor.MiddleRight;
            age.style.marginRight = 6;
            row.Add(age);

            var rating = new Label(vm.Rating.ToString());
            rating.style.width = 26;
            rating.style.fontSize = 13;
            rating.style.unityFontStyleAndWeight = FontStyle.Bold;
            rating.style.color = UiKit.TextPrimary;
            rating.style.unityTextAlign = TextAnchor.MiddleRight;
            rating.style.marginRight = 8;
            row.Add(rating);

            ConditionStrip.Append(row, vm.FormArrow, vm.MoraleFace, vm.Fitness);
            foreach (VisualElement child in row.Children())
                child.pickingMode = PickingMode.Ignore;
            return row;
        }

        // ------------------------------------------------------------- drag/tap

        /// <summary>
        /// Wires unified drag+tap on <paramref name="el"/>: a real drag beyond a small
        /// threshold shows a floating ghost and, on release over a pitch slot, raises
        /// <see cref="AssignRequested"/>; a release without dragging is a tap.
        /// </summary>
        private void WireDrag(VisualElement el, int playerId, Func<string> ghostText, Action onTap)
        {
            el.RegisterCallback<PointerDownEvent>(evt =>
            {
                _dragEl = el;
                _dragPlayerId = playerId;
                _ghostText = ghostText;
                _onTap = onTap;
                _dragStart = evt.position;
                _dragging = false;
                el.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            el.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_dragEl != el || !el.HasPointerCapture(evt.pointerId))
                    return;

                Vector2 pos = evt.position;
                if (!_dragging && (pos - _dragStart).magnitude > 6f)
                {
                    _dragging = true;
                    _ghost.text = _ghostText?.Invoke() ?? string.Empty;
                    _ghost.style.display = DisplayStyle.Flex;
                    _ghost.BringToFront();
                }

                if (_dragging)
                    PositionGhost(pos);
            });

            el.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_dragEl != el || !el.HasPointerCapture(evt.pointerId))
                    return;

                el.ReleasePointer(evt.pointerId);
                _ghost.style.display = DisplayStyle.None;

                if (_dragging)
                {
                    // Dropped onto another player's token → swap; dropped on open turf →
                    // reposition the dragged player (free positioning, task 6.10).
                    int swapSlot = _pitch.NearestOtherToken(evt.position, _dragPlayerId, 0.9f);
                    if (swapSlot >= 0)
                    {
                        AssignRequested?.Invoke(swapSlot, _dragPlayerId);
                    }
                    else
                    {
                        (bool onPitch, float nx, float ny) = _pitch.ToNormalized(evt.position);
                        if (onPitch)
                            RepositionRequested?.Invoke(_dragPlayerId, nx, ny);
                    }
                }
                else
                {
                    _onTap?.Invoke();
                }

                _dragEl = null;
                _dragging = false;
            });
        }

        private void PositionGhost(Vector2 panelPos)
        {
            Rect rootWb = Root.worldBound;
            _ghost.style.left = panelPos.x - rootWb.x + 10f;
            _ghost.style.top = panelPos.y - rootWb.y + 10f;
        }

        private void ApplyResponsive()
        {
            float w = Root.contentRect.width;
            if (w <= 1f)
                return;

            bool stack = w < StackBreakpoint;
            if (stack == _stacked)
                return;
            _stacked = stack;

            _content.style.flexDirection = stack ? FlexDirection.Column : FlexDirection.Row;
            if (stack)
            {
                _pitchWrap.style.marginRight = 0;
                _pitchWrap.style.minHeight = 220;
                _pitchWrap.style.flexGrow = 0;
                _benchColumn.style.width = new Length(100, LengthUnit.Percent);
                _benchColumn.style.minWidth = 0;
                _benchColumn.style.maxHeight = 200;
                _benchColumn.style.marginTop = 6;
            }
            else
            {
                _pitchWrap.style.marginRight = 8;
                _pitchWrap.style.minHeight = StyleKeyword.Null;
                _pitchWrap.style.flexGrow = 1;
                _benchColumn.style.width = new Length(34, LengthUnit.Percent);
                _benchColumn.style.minWidth = 150;
                _benchColumn.style.maxHeight = StyleKeyword.Null;
                _benchColumn.style.marginTop = 0;
            }
        }

        private static Button FooterButton(string text, Action onClick)
        {
            var button = UiKit.MenuButton(text, onClick);
            button.style.width = 140;
            button.style.height = 42;
            button.style.fontSize = 15;
            button.style.marginLeft = 5;
            button.style.marginRight = 5;
            return button;
        }
    }
}
