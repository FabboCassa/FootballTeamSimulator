using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the ranked lineup editor (Phase 9.2), on the shared page scaffold: the formation
    /// cycle on top, the starting XI and the bench as two scrolling lists of rows (tap a slot, then a
    /// bench player), and Back / Save in the footer. No logic — the presenter formats the rows, holds the
    /// assignment, and builds the Sim.Core LineupPlan on Save.
    /// </summary>
    public sealed class RankedLineupView
    {
        public event Action FormationClicked;
        public event Action SaveClicked;
        public event Action BackClicked;
        public event Action<int> SlotClicked;    // slot index (0..10)
        public event Action<int> PlayerClicked;  // bench player externalId

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Button _formationButton;
        private readonly Label _slotsCaption;
        private readonly ScrollView _slots;
        private readonly Label _benchCaption;
        private readonly ScrollView _bench;
        private readonly Label _status;
        private readonly Button _saveButton;
        private readonly Button _backButton;

        /// <summary>One lineup slot: its role label + the assigned player's label + whether it's selected.</summary>
        public readonly struct SlotVm
        {
            public readonly string RoleLabel;
            public readonly string PlayerLabel;
            public readonly bool Selected;
            public SlotVm(string roleLabel, string playerLabel, bool selected)
            {
                RoleLabel = roleLabel;
                PlayerLabel = playerLabel;
                Selected = selected;
            }
        }

        /// <summary>One bench player: its externalId + a preformatted label.</summary>
        public readonly struct BenchVm
        {
            public readonly int PlayerId;
            public readonly string Label;
            public BenchVm(int playerId, string label) { PlayerId = playerId; Label = label; }
        }

        public RankedLineupView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthMedium);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            _title.style.marginBottom = UiKit.SpaceSm;
            col.Add(_title);

            VisualElement formationPanel = UiKit.Panel();
            col.Add(formationPanel);
            _formationButton = UiKit.CycleButton(() => FormationClicked?.Invoke());
            _formationButton.style.marginBottom = 0;
            formationPanel.Add(_formationButton);

            VisualElement xiPanel = UiKit.Panel(grow: true);
            col.Add(xiPanel);
            _slotsCaption = UiKit.SectionLabel(string.Empty);
            _slotsCaption.style.marginTop = 0;
            _slotsCaption.style.whiteSpace = WhiteSpace.Normal;
            xiPanel.Add(_slotsCaption);
            _slots = UiKit.ListScroll();
            xiPanel.Add(_slots);

            VisualElement benchPanel = UiKit.Panel(grow: true);
            col.Add(benchPanel);
            _benchCaption = UiKit.SectionLabel(string.Empty);
            _benchCaption.style.marginTop = 0;
            benchPanel.Add(_benchCaption);
            _bench = UiKit.ListScroll();
            benchPanel.Add(_bench);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 0f;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            VisualElement footer = UiKit.FooterBar();
            _backButton = UiKit.FooterButton(string.Empty, () => BackClicked?.Invoke());
            footer.Add(_backButton);
            _saveButton = UiKit.FooterPrimaryButton(string.Empty, () => SaveClicked?.Invoke());
            footer.Add(_saveButton);
            col.Add(footer);

            UpdateTexts();
        }

        public void SetFormation(string label) => _formationButton.text = label;

        public void SetSlots(IReadOnlyList<SlotVm> slots)
        {
            _slots.Clear();
            if (slots == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                int index = i;
                SlotVm s = slots[i];

                VisualElement row = UiKit.RowCard(44f);
                UiKit.SetRowCardSelected(row, s.Selected);

                var role = new Label(s.RoleLabel ?? string.Empty);
                role.style.fontSize = 12;
                role.style.unityFontStyleAndWeight = FontStyle.Bold;
                role.style.color = UiKit.TextMuted;
                role.style.width = 56;
                role.style.flexShrink = 0f;
                role.pickingMode = PickingMode.Ignore;
                row.Add(role);

                var player = new Label(s.PlayerLabel ?? string.Empty);
                player.style.fontSize = 14;
                player.style.color = UiKit.TextPrimary;
                player.style.flexGrow = 1f;
                player.style.flexShrink = 1f;
                player.style.minWidth = 0f;
                player.style.whiteSpace = WhiteSpace.NoWrap;
                player.style.overflow = Overflow.Hidden;
                player.style.textOverflow = TextOverflow.Ellipsis;
                player.pickingMode = PickingMode.Ignore;
                row.Add(player);

                row.RegisterCallback<ClickEvent>(_ => SlotClicked?.Invoke(index));
                _slots.Add(row);
            }
        }

        public void SetBench(IReadOnlyList<BenchVm> bench)
        {
            _bench.Clear();
            if (bench == null) return;
            foreach (BenchVm b in bench)
            {
                int id = b.PlayerId;

                VisualElement row = UiKit.RowCard(44f);

                var label = new Label(b.Label ?? string.Empty);
                label.style.fontSize = 14;
                label.style.color = UiKit.TextPrimary;
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.minWidth = 0f;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.pickingMode = PickingMode.Ignore;
                row.Add(label);

                var arrow = new Label("↑");
                arrow.style.fontSize = 16;
                arrow.style.color = UiKit.TextMuted;
                arrow.style.marginLeft = UiKit.SpaceSm;
                arrow.style.flexShrink = 0f;
                arrow.pickingMode = PickingMode.Ignore;
                row.Add(arrow);

                row.RegisterCallback<ClickEvent>(_ => PlayerClicked?.Invoke(id));
                _bench.Add(row);
            }
        }

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message;
            _status.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _status.style.display = DisplayStyle.Flex;
        }

        public void ClearStatus() => _status.style.display = DisplayStyle.None;

        public void SetBusy(bool busy)
        {
            _formationButton.SetEnabled(!busy);
            _saveButton.SetEnabled(!busy);
        }

        public void UpdateTexts()
        {
            _title.text = _tr("ranked.lineup.title");
            _slotsCaption.text = _tr("ranked.lineup.xi_caption");
            _benchCaption.text = _tr("ranked.lineup.bench_caption");
            _saveButton.text = _tr("ranked.lineup.save");
            _backButton.text = _tr("common.back");
        }
    }
}
