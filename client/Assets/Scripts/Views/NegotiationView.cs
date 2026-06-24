using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Negotiation dialog (task 5.3) used both when the user BUYS (he raises his offer, the AI
    /// seller accepts/counters or loses patience) and when he SELLS (he names an ask, the AI buyer
    /// accepts/bids or walks). The amount can be typed directly OR nudged with −/+ (proportional
    /// step). Dumb view — the presenter owns the model, sets every label, the patience indicator
    /// and which actions are legal; the view only edits the amount and emits the two action clicks.
    /// </summary>
    public sealed class NegotiationView
    {
        public event Action DecrementClicked;
        public event Action IncrementClicked;
        public event Action<long> AmountTyped;
        public event Action PrimaryClicked;
        public event Action SecondaryClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _title;
        private readonly VisualElement _infoBox;
        private readonly Label _amountCaption;
        private readonly TextField _amountField;
        private readonly Label _amountFormatted;
        private readonly Button _decrement;
        private readonly Button _increment;
        private readonly Label _patience;
        private readonly Button _primary;
        private readonly Button _secondary;
        private readonly Label _status;

        public NegotiationView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 14;
            Root.style.paddingBottom = 14;
            Root.style.paddingLeft = 18;
            Root.style.paddingRight = 18;

            _title = UiKit.Title(tr("negotiation.title"));
            _title.style.fontSize = 26;
            _title.style.marginBottom = 8;
            Root.Add(_title);

            _infoBox = Card();
            Root.Add(_infoBox);

            _amountCaption = SectionLabel(tr("negotiation.your_amount"));
            _amountCaption.style.marginTop = 10;
            Root.Add(_amountCaption);

            var amountRow = new VisualElement();
            amountRow.style.flexDirection = FlexDirection.Row;
            amountRow.style.alignItems = Align.Center;

            _decrement = StepButton("−", () => DecrementClicked?.Invoke());
            amountRow.Add(_decrement);

            _amountField = new TextField();
            _amountField.style.flexGrow = 1f;
            _amountField.style.fontSize = 20;
            _amountField.style.minWidth = 140;
            _amountField.RegisterValueChangedCallback(evt => AmountTyped?.Invoke(ParseDigits(evt.newValue)));
            amountRow.Add(_amountField);

            _increment = StepButton("+", () => IncrementClicked?.Invoke());
            amountRow.Add(_increment);
            Root.Add(amountRow);

            _amountFormatted = new Label(string.Empty);
            _amountFormatted.style.fontSize = 15;
            _amountFormatted.style.color = new Color(0.7f, 0.9f, 0.7f, 1f);
            _amountFormatted.style.marginTop = 2;
            _amountFormatted.style.marginBottom = 6;
            Root.Add(_amountFormatted);

            _patience = new Label(string.Empty);
            _patience.style.fontSize = 12;
            _patience.style.color = new Color(1f, 1f, 1f, 0.6f);
            _patience.style.marginBottom = 6;
            Root.Add(_patience);

            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.flexWrap = Wrap.Wrap;
            actionRow.style.marginBottom = 4;
            _primary = ActionButton(() => PrimaryClicked?.Invoke());
            actionRow.Add(_primary);
            _secondary = ActionButton(() => SecondaryClicked?.Invoke());
            actionRow.Add(_secondary);
            Root.Add(actionRow);

            _status = UiKit.Subtitle(string.Empty);
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.maxWidth = 460;
            _status.style.marginTop = 2;
            Root.Add(_status);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 6;
            var back = UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke());
            back.style.width = 160;
            back.style.height = 44;
            back.style.fontSize = 16;
            footer.Add(back);
            Root.Add(footer);
        }

        public void SetTitle(string text) => _title.text = text;
        public void SetAmountCaption(string text) => _amountCaption.text = text;
        public void SetAmountFormatted(string text) => _amountFormatted.text = text;
        public void SetPatience(string text) => _patience.text = text;
        public void SetStatus(string text) => _status.text = text;

        /// <summary>Sets the editable field without firing the typed callback (used by non-typing updates).</summary>
        public void SetAmountRaw(long value) => _amountField.SetValueWithoutNotify(value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void SetInfo(IReadOnlyList<string> lines)
        {
            _infoBox.Clear();
            foreach (string line in lines)
            {
                var label = new Label(line);
                label.style.fontSize = 14;
                label.style.color = new Color(1f, 1f, 1f, 0.85f);
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginBottom = 2;
                _infoBox.Add(label);
            }
        }

        public void SetPrimary(string text, bool enabled)
        {
            _primary.text = text;
            _primary.SetEnabled(enabled);
            _primary.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetSecondary(string text, bool enabled)
        {
            _secondary.text = text;
            _secondary.SetEnabled(enabled);
            _secondary.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetAdjustEnabled(bool enabled)
        {
            _decrement.SetEnabled(enabled);
            _increment.SetEnabled(enabled);
            _amountField.SetEnabled(enabled);
        }

        private static long ParseDigits(string text)
        {
            long value = 0;
            foreach (char c in text)
            {
                if (c >= '0' && c <= '9')
                    value = value * 10 + (c - '0');
                if (value > 1_000_000_000_000L) // guard against absurd typing
                    return 1_000_000_000_000L;
            }
            return value;
        }

        private static VisualElement Card()
        {
            var box = new VisualElement();
            box.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            box.style.paddingTop = 8;
            box.style.paddingBottom = 8;
            box.style.paddingLeft = 10;
            box.style.paddingRight = 10;
            return box;
        }

        private static Label SectionLabel(string caption)
        {
            var label = new Label(caption);
            label.style.color = new Color(1f, 1f, 1f, 0.7f);
            label.style.fontSize = 13;
            label.style.marginBottom = 4;
            return label;
        }

        private static Button StepButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.width = 52;
            button.style.height = 44;
            button.style.fontSize = 22;
            button.style.marginRight = 8;
            button.style.marginLeft = 8;
            return button;
        }

        private static Button ActionButton(Action onClick)
        {
            var button = new Button(onClick) { text = string.Empty };
            button.style.minWidth = 180;
            button.style.height = 46;
            button.style.fontSize = 15;
            button.style.marginRight = 8;
            button.style.marginTop = 4;
            return button;
        }
    }
}
