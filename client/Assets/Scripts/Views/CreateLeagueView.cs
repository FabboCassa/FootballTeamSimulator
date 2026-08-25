using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view to create an online league (task 8.1b), on the shared page scaffold: a name field and a
    /// size stepper (2–20, matching the server), with the advance mode fixed to "advance when all ready"
    /// for now, and Back / Create in the footer. No logic — owns the stepper state and exposes getters.
    /// </summary>
    public sealed class CreateLeagueView
    {
        public event Action CreateClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private const int MinSize = 2;
        private const int MaxSize = 20;

        private readonly Label _title;
        private readonly Label _nameCaption;
        private readonly TextField _nameField;
        private readonly Label _sizeCaption;
        private readonly Label _sizeValue;
        private readonly Button _sizeMinus;
        private readonly Button _sizePlus;
        private readonly Label _modeCaption;
        private readonly Button _createButton;
        private readonly Label _status;
        private readonly Button _backButton;

        private int _size = 8;

        public CreateLeagueView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthNarrow);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            _title.style.marginBottom = UiKit.SpaceSm;
            col.Add(_title);

            VisualElement panel = UiKit.Panel();
            col.Add(panel);

            _nameCaption = UiKit.SectionLabel(string.Empty);
            _nameCaption.style.marginTop = 0;
            panel.Add(_nameCaption);
            _nameField = new TextField { maxLength = 120 };
            _nameField.style.minHeight = 34;
            _nameField.style.marginLeft = 0;
            _nameField.style.marginRight = 0;
            _nameField.style.marginBottom = UiKit.SpaceSm;
            panel.Add(_nameField);

            _sizeCaption = UiKit.SectionLabel(string.Empty);
            panel.Add(_sizeCaption);

            VisualElement sizeRow = UiKit.Row();
            panel.Add(sizeRow);
            _sizeMinus = UiKit.SmallButton("−", () => ChangeSize(-1), 48f);
            _sizeMinus.style.marginLeft = 0;
            sizeRow.Add(_sizeMinus);
            _sizeValue = new Label(string.Empty);
            _sizeValue.style.fontSize = 20;
            _sizeValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            _sizeValue.style.color = UiKit.TextPrimary;
            _sizeValue.style.minWidth = 56;
            _sizeValue.style.unityTextAlign = TextAnchor.MiddleCenter;
            sizeRow.Add(_sizeValue);
            _sizePlus = UiKit.SmallButton("+", () => ChangeSize(+1), 48f);
            sizeRow.Add(_sizePlus);

            _modeCaption = UiKit.HelpText(string.Empty);
            _modeCaption.style.marginTop = UiKit.SpaceSm;
            _modeCaption.style.marginBottom = 0;
            panel.Add(_modeCaption);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 0f;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            col.Add(spacer);

            VisualElement footer = UiKit.FooterBar();
            _backButton = UiKit.FooterButton(string.Empty, () => BackClicked?.Invoke());
            footer.Add(_backButton);
            _createButton = UiKit.FooterPrimaryButton(string.Empty, () => CreateClicked?.Invoke());
            footer.Add(_createButton);
            col.Add(footer);

            RefreshSize();
            UpdateTexts();
        }

        public string LeagueName => (_nameField.value ?? string.Empty).Trim();
        public int Size => _size;

        private void ChangeSize(int delta)
        {
            _size = UnityEngine.Mathf.Clamp(_size + delta, MinSize, MaxSize);
            RefreshSize();
        }

        private void RefreshSize() => _sizeValue.text = _size.ToString();

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message;
            _status.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _status.style.display = DisplayStyle.Flex;
        }

        public void SetBusy(bool busy)
        {
            _createButton.SetEnabled(!busy);
            _sizeMinus.SetEnabled(!busy);
            _sizePlus.SetEnabled(!busy);
        }

        public void UpdateTexts()
        {
            _title.text = _tr("create.title");
            _nameCaption.text = _tr("create.name");
            _sizeCaption.text = _tr("create.size");
            _modeCaption.text = _tr("create.mode_all_ready");
            _createButton.text = _tr("create.submit");
            _backButton.text = _tr("common.back");
        }
    }
}
