using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>What the bid panel needs to know about the lot it is opening on. Every string is already
    /// localised and formatted by the presenter; the amounts are raw so the panel can do the arithmetic.</summary>
    public sealed class BidPanelVm
    {
        /// <summary>Opaque lot id, echoed back with the confirmed amount.</summary>
        public string Id;
        public string Title;
        /// <summary>0 GK · 1 def · 2 mid · 3 att → the reparto colour; -1 = no chip.</summary>
        public int RoleGroup = -1;
        public string RoleAbbr;
        /// <summary>One line of context, e.g. "Minimo €1.2M · Disponibile €25M".</summary>
        public string Subtitle;
        public long Min;
        public long Max;
        public IReadOnlyList<long> Steps;
        /// <summary>Format for the confirm button, with {0} = the amount ("Offri {0}").</summary>
        public string ConfirmFormat;
        public string MinLabel;
        public string MaxLabel;
        public string CancelLabel;
        /// <summary>Shown instead of the controls when the minimum bid is beyond the available budget.</summary>
        public string OverBudget;
        /// <summary>Where the control OPENS (clamped into [Min, Max]); 0 = at the minimum. A bidder wants
        /// the minimum raise pre-filled, but a SELLER pricing his own player (task 12.2) wants to start at
        /// what the man is worth, not at the floor of the legal band.</summary>
        public long Start;
    }

    /// <summary>
    /// The shared bid control for the online auctions. Bidding to the euro was never the point: this panel
    /// starts at the minimum legal raise and offers four ROUND increments sized to the lot (the presenter
    /// computes them), plus "minimum" and "everything I have left". The amount is always clamped between
    /// the minimum and what the club can actually afford, so an illegal bid cannot be sent from here.
    /// Dumb component: it owns only the amount arithmetic and emits the confirmed value.
    /// </summary>
    public sealed class BidPanel
    {
        public event Action<string, long> Confirmed;   // lot id, amount
        public event Action Cancelled;

        public VisualElement Root { get; }
        public bool IsOpen { get; private set; }

        private readonly Func<long, string> _money;

        private readonly VisualElement _chipSlot;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _amount;
        private readonly Button _down;
        private readonly VisualElement _stepRow;
        private readonly VisualElement _controlRow;
        private readonly Button _minButton;
        private readonly Button _maxButton;
        private readonly Button _confirm;
        private readonly Button _cancel;
        private readonly Label _warning;

        private string _id;
        private long _min, _max, _value;
        private long _firstStep = 25_000;
        private string _confirmFormat = "{0}";

        public BidPanel(Func<long, string> money)
        {
            _money = money ?? (v => v.ToString());

            Root = UiKit.Panel();
            Root.style.display = DisplayStyle.None;

            VisualElement head = UiKit.Row();
            Root.Add(head);
            _chipSlot = new VisualElement();
            _chipSlot.style.flexDirection = FlexDirection.Row;
            _chipSlot.style.flexShrink = 0f;
            head.Add(_chipSlot);
            _title = UiKit.PanelLine(string.Empty);
            _title.style.fontSize = 15;
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.flexGrow = 1f;
            _title.style.flexShrink = 1f;
            head.Add(_title);

            _subtitle = UiKit.Caption(string.Empty);
            _subtitle.style.whiteSpace = WhiteSpace.Normal;
            _subtitle.style.marginBottom = UiKit.SpaceXs;
            Root.Add(_subtitle);

            // The amount, big enough to be the thing you read before tapping.
            VisualElement amountRow = UiKit.Row();
            amountRow.style.marginBottom = UiKit.SpaceXs;
            Root.Add(amountRow);
            _amount = new Label(string.Empty);
            _amount.style.fontSize = 26;
            _amount.style.unityFontStyleAndWeight = FontStyle.Bold;
            _amount.style.color = UiKit.Accent;
            _amount.style.flexGrow = 1f;
            amountRow.Add(_amount);
            _down = UiKit.SmallButton("−", () => Nudge(-1), 52f);
            amountRow.Add(_down);

            _stepRow = UiKit.Toolbar();
            _stepRow.style.marginBottom = UiKit.SpaceXs;
            Root.Add(_stepRow);

            _controlRow = UiKit.Toolbar();
            _controlRow.style.marginBottom = 0;
            Root.Add(_controlRow);
            _minButton = Control(_controlRow, () => SetValue(_min));
            _maxButton = Control(_controlRow, () => SetValue(_max));
            _cancel = Control(_controlRow, () => { Hide(); Cancelled?.Invoke(); });
            _confirm = Control(_controlRow, OnConfirm, 190f);
            UiKit.SetSmallButtonAccent(_confirm, true);

            _warning = UiKit.PanelLine(string.Empty);
            _warning.style.color = UiKit.Danger;
            _warning.style.display = DisplayStyle.None;
            Root.Add(_warning);
        }

        private static Button Control(VisualElement parent, Action onClick, float minWidth = 120f)
        {
            Button b = UiKit.SmallButton(string.Empty, onClick, minWidth);
            b.style.marginLeft = 0;
            b.style.marginRight = 6;
            b.style.marginBottom = 4;
            parent.Add(b);
            return b;
        }

        public void Show(BidPanelVm vm)
        {
            if (vm == null) return;

            _id = vm.Id;
            _min = vm.Min;
            _max = vm.Max < vm.Min ? vm.Min : vm.Max;
            _confirmFormat = string.IsNullOrEmpty(vm.ConfirmFormat) ? "{0}" : vm.ConfirmFormat;

            _chipSlot.Clear();
            if (vm.RoleGroup >= 0)
            {
                VisualElement chip = PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup, 48f);
                chip.style.height = 26;
                _chipSlot.Add(chip);
            }
            _title.text = vm.Title ?? string.Empty;
            _subtitle.text = vm.Subtitle ?? string.Empty;
            _minButton.text = vm.MinLabel ?? string.Empty;
            _maxButton.text = vm.MaxLabel ?? string.Empty;
            _cancel.text = vm.CancelLabel ?? string.Empty;

            _stepRow.Clear();
            _firstStep = 25_000;
            if (vm.Steps != null)
            {
                for (int i = 0; i < vm.Steps.Count; i++)
                {
                    long step = vm.Steps[i];
                    if (i == 0) _firstStep = step;
                    Button b = UiKit.SmallButton("+" + _money(step), () => Add(step), 110f);
                    b.style.marginLeft = 0;
                    b.style.marginRight = 6;
                    b.style.marginBottom = 4;
                    _stepRow.Add(b);
                }
            }

            // A lot whose minimum raise is already past the club's budget: say so instead of offering
            // buttons that can only produce a server error.
            bool affordable = vm.Max >= vm.Min;
            _warning.text = vm.OverBudget ?? string.Empty;
            _warning.style.display = affordable ? DisplayStyle.None : DisplayStyle.Flex;
            _stepRow.style.display = affordable ? DisplayStyle.Flex : DisplayStyle.None;
            _down.SetEnabled(affordable);
            _minButton.SetEnabled(affordable);
            _maxButton.SetEnabled(affordable);
            _confirm.SetEnabled(affordable);

            SetValue(vm.Start > 0 ? vm.Start : _min);
            IsOpen = true;
            Root.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            _id = null;
            IsOpen = false;
            Root.style.display = DisplayStyle.None;
        }

        public void SetBusy(bool busy) => _confirm.SetEnabled(!busy);

        // --- internals -------------------------------------------------------------------------------

        private void Add(long step) => SetValue(_value + step);

        private void Nudge(int direction) => SetValue(_value + direction * _firstStep);

        private void SetValue(long value)
        {
            if (value < _min) value = _min;
            if (value > _max) value = _max;
            _value = value;
            _amount.text = _money(_value);
            _confirm.text = string.Format(_confirmFormat, _money(_value));
        }

        private void OnConfirm()
        {
            if (string.IsNullOrEmpty(_id)) return;
            Confirmed?.Invoke(_id, _value);
        }
    }
}
