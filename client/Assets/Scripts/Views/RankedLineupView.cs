using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the ranked lineup editor (Phase 9.2): a formation cycle, the 11 chosen slots (tap a slot
    /// to select it) and the bench (tap a player to put them in the selected slot). No logic — the presenter
    /// formats the rows, holds the assignment, and builds the Sim.Core LineupPlan on Save.
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
        private readonly VisualElement _slots;
        private readonly Label _benchCaption;
        private readonly VisualElement _bench;
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

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.PageColumn(UiKit.WidthMedium);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            col.Add(_title);

            _formationButton = UiKit.PrimaryButton(string.Empty, () => FormationClicked?.Invoke());
            col.Add(_formationButton);

            _slotsCaption = UiKit.Subtitle(string.Empty);
            _slotsCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_slotsCaption);
            _slots = new VisualElement();
            col.Add(_slots);

            _benchCaption = UiKit.Subtitle(string.Empty);
            _benchCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_benchCaption);
            _bench = new VisualElement();
            col.Add(_bench);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            _saveButton = UiKit.PrimaryButton(string.Empty, () => SaveClicked?.Invoke());
            _saveButton.style.marginTop = UiKit.SpaceMd;
            col.Add(_saveButton);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceSm;
            col.Add(_backButton);

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
                var s = slots[i];
                var button = UiKit.MenuButton($"{s.RoleLabel}   {s.PlayerLabel}", () => SlotClicked?.Invoke(index));
                button.style.marginBottom = UiKit.SpaceXs;
                if (s.Selected) button.style.backgroundColor = UiKit.AccentDark;
                _slots.Add(button);
            }
        }

        public void SetBench(IReadOnlyList<BenchVm> bench)
        {
            _bench.Clear();
            if (bench == null) return;
            foreach (var b in bench)
            {
                int id = b.PlayerId;
                var button = UiKit.MenuButton(b.Label, () => PlayerClicked?.Invoke(id));
                button.style.marginBottom = UiKit.SpaceXs;
                _bench.Add(button);
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
