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
            // Task 14.4: the standard page. Head = kicker, club line, and the three actions that
            // used to be a footer (Auto / Save as the accent CTA / Back). Under it one line that is
            // either the hint or the picked player, then the pitch and the bench side by side.
            PageParts page = UiKit.StandardPage(tr("squad.kicker"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke());
            Root = page.Root;
            Root.AddToClassList("fts-squad");
            _header = page.Title;

            Button auto = UiKit.GhostButton(tr("squad.auto_pick"), () => AutoClicked?.Invoke());
            Button save = UiKit.PrimaryButton(tr("squad.save_lineup"), () => SaveClicked?.Invoke());
            save.AddToClassList("fts-headcta");
            page.Head.Actions.Insert(0, save);
            page.Head.Actions.Insert(0, auto);

            _status = UiKit.HelpText(string.Empty);
            _status.AddToClassList("fts-status");
            page.Column.Add(_status);

            // Selection bar: shows the picked player + a profile shortcut, or a hint.
            _selectionBar = UiKit.Row();
            _selectionBar.AddToClassList("fts-squad__selection");
            _selectionLabel = new Label(string.Empty);
            _selectionLabel.AddToClassList("fts-squad__selectiontext");
            _selectionLabel.style.flexGrow = 1f;
            _selectionLabel.style.flexShrink = 1f;
            _selectionLabel.style.whiteSpace = WhiteSpace.Normal;
            _selectionBar.Add(_selectionLabel);
            _profileButton = UiKit.GhostButton(tr("squad.profile"), () =>
            {
                if (_selectedPlayerId >= 0) ProfileClicked?.Invoke(_selectedPlayerId);
            });
            _selectionBar.Add(_profileButton);
            page.Column.Add(_selectionBar);

            _hint = UiKit.HelpText(tr("squad.pitch_hint"));
            _hint.AddToClassList("fts-squad__hint");
            page.Column.Add(_hint);

            _content = new VisualElement();
            _content.AddToClassList("fts-squad__grid");
            _content.style.flexDirection = FlexDirection.Row;
            page.Column.Add(_content);

            _pitchWrap = UiKit.OptionCard();
            _pitchWrap.AddToClassList("fts-squad__pitchcard");
            var pitchBox = new VisualElement();
            pitchBox.AddToClassList("fts-squad__pitch");
            pitchBox.style.overflow = Overflow.Hidden;
            _pitch = new PitchFormationView(mirror: false);
            pitchBox.Add(_pitch);
            _pitchWrap.Add(pitchBox);
            _content.Add(_pitchWrap);

            _benchColumn = UiKit.OptionCard();
            _benchColumn.AddToClassList("fts-squad__benchcard");
            _benchColumn.Add(UiKit.BlockHead(tr("squad.bench_caption")));
            _benchList = new ScrollView(ScrollViewMode.Vertical);
            _benchList.AddToClassList("fts-squad__bench");
            _benchList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _benchColumn.Add(_benchList);
            _content.Add(_benchColumn);

            _ghost = new Label(string.Empty);
            _ghost.AddToClassList("fts-squad__ghost");
            _ghost.style.position = Position.Absolute;
            _ghost.style.display = DisplayStyle.None;
            _ghost.pickingMode = PickingMode.Ignore;
            _ghost.style.backgroundColor = UiKit.Accent;
            _ghost.style.color = UiKit.TextOnAccent;
            _ghost.style.unityFontStyleAndWeight = FontStyle.Bold;
            UiKit.Round(_ghost, 8);
            Root.Add(_ghost);

            SetSelection(null);
            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Responsive.Changed += Layout;
                Layout(Responsive.Current);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Responsive.Changed -= Layout);
            Layout(Responsive.Current);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetStatus(string text)
        {
            _status.text = text ?? string.Empty;
            _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

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
            // [role chip] [name···] [age] [rating] [condition] — the shared row kit, so the bench
            // reads exactly like every other player list in the game.
            VisualElement row = PlayerRowKit.Row();
            row.AddToClassList("fts-squad__benchrow");
            row.EnableInClassList("fts-squad__benchrow--selected", vm.Selected);
            row.tooltip = vm.Tooltip ?? string.Empty;

            row.Add(PlayerRowKit.RoleChip(vm.Role, vm.RoleGroup));
            VisualElement name = PlayerRowKit.TextCell(vm.Name, 0, TextAnchor.MiddleLeft, grow: true, bold: true);
            PlayerRowKit.SetSelected(name, vm.Selected);
            row.Add(name);

            var age = new Label(vm.Age.ToString());
            age.AddToClassList("fts-squad__age");
            age.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(age);

            var rating = new Label(vm.Rating.ToString());
            rating.AddToClassList("fts-squad__rating");
            rating.style.unityTextAlign = TextAnchor.MiddleCenter;
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

        private void Layout(Viewport viewport)
        {
            bool stack = viewport == Viewport.Mobile;
            _stacked = stack;
            _content.style.flexDirection = stack ? FlexDirection.Column : FlexDirection.Row;
            _content.style.alignItems = stack ? Align.Stretch : Align.FlexStart;
            _pitchWrap.style.flexGrow = stack ? 0f : 3f;
            _pitchWrap.style.flexBasis = stack ? new StyleLength(StyleKeyword.Auto) : new StyleLength(0f);
            _benchColumn.style.flexGrow = stack ? 0f : 2f;
            _benchColumn.style.flexBasis = stack ? new StyleLength(StyleKeyword.Auto) : new StyleLength(0f);
            _benchColumn.style.marginLeft = stack ? 0f : UiKit.SpaceMd;
        }

        private static Button FooterButton(string text, Action onClick) => UiKit.FooterButton(text, onClick);
    }
}
