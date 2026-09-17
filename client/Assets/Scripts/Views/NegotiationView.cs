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
            // Task 14.4: the standard page. The deal facts sit on a card; the amount is the page's
            // raised card — a big field with − / + either side, the formatted figure under it and the
            // offers left — and the two actions are a CTA and a ghost, full width on a phone.
            PageParts page = UiKit.StandardPage(tr("negotiation.kicker"), tr("negotiation.title"), tr("common.back"),
                () => BackClicked?.Invoke(), UiKit.WidthNarrow + 200f);
            Root = page.Root;
            _title = page.Title;
            VisualElement col = page.Column;

            _infoBox = UiKit.OptionCard();
            _infoBox.AddToClassList("fts-nego__info");
            col.Add(_infoBox);

            VisualElement amountCard = UiKit.RaisedCard();
            amountCard.AddToClassList("fts-nego__amount");
            _amountCaption = UiKit.SectionLabel(tr("negotiation.your_amount"));
            _amountCaption.style.marginTop = 0;
            amountCard.Add(_amountCaption);

            var amountRow = new VisualElement();
            amountRow.style.flexDirection = FlexDirection.Row;
            amountRow.style.alignItems = Align.Center;
            _decrement = StepButton("\u2212", () => DecrementClicked?.Invoke());
            amountRow.Add(_decrement);
            _amountField = new TextField();
            _amountField.AddToClassList("fts-search");
            _amountField.AddToClassList("fts-nego__field");
            _amountField.style.flexGrow = 1f;
            _amountField.RegisterValueChangedCallback(evt => AmountTyped?.Invoke(ParseDigits(evt.newValue)));
            amountRow.Add(_amountField);
            _increment = StepButton("+", () => IncrementClicked?.Invoke());
            amountRow.Add(_increment);
            amountCard.Add(amountRow);

            _amountFormatted = new Label(string.Empty);
            _amountFormatted.AddToClassList("fts-nego__formatted");
            UiKit.UseDisplayFont(_amountFormatted);
            _amountFormatted.style.unityTextAlign = TextAnchor.MiddleCenter;
            amountCard.Add(_amountFormatted);

            _patience = new Label(string.Empty);
            _patience.AddToClassList("fts-nego__patience");
            _patience.style.unityTextAlign = TextAnchor.MiddleCenter;
            amountCard.Add(_patience);

            var actionRow = new VisualElement();
            actionRow.AddToClassList("fts-nego__actions");
            _primary = UiKit.PrimaryButton(string.Empty, () => PrimaryClicked?.Invoke());
            _primary.AddToClassList("fts-nego__primary");
            actionRow.Add(_primary);
            _secondary = UiKit.GhostButton(string.Empty, () => SecondaryClicked?.Invoke());
            _secondary.AddToClassList("fts-nego__secondary");
            actionRow.Add(_secondary);
            amountCard.Add(actionRow);
            col.Add(amountCard);

            _status = new Label(string.Empty);
            _status.AddToClassList("fts-nego__status");
            _status.style.whiteSpace = WhiteSpace.Normal;
            col.Add(_status);
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
                label.AddToClassList("fts-nego__line");
                label.style.whiteSpace = WhiteSpace.Normal;
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

        private static Button StepButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("fts-nego__step");
            return button;
        }
    }
}
