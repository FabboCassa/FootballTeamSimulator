using System;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view to create an online league (task 8.1b): a name field and a size stepper (2–20,
    /// matching the server), with the advance mode fixed to "advance when all ready" for now. No
    /// logic — owns the size stepper state and exposes getters; the presenter drives the API.
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

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.PageColumn(UiKit.WidthNarrow, grow: false);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            col.Add(_title);

            var card = UiKit.Card();
            col.Add(card);

            _nameCaption = UiKit.Caption(string.Empty);
            card.Add(_nameCaption);
            _nameField = new TextField { maxLength = 120 };
            _nameField.style.marginBottom = UiKit.SpaceSm;
            _nameField.style.minHeight = 40;
            card.Add(_nameField);

            _sizeCaption = UiKit.Caption(string.Empty);
            _sizeCaption.style.marginTop = UiKit.SpaceSm;
            card.Add(_sizeCaption);

            var sizeRow = UiKit.Row();
            card.Add(sizeRow);
            _sizeMinus = UiKit.MenuButton("−", () => ChangeSize(-1));
            _sizeMinus.style.width = 56;
            sizeRow.Add(_sizeMinus);
            _sizeValue = UiKit.Subtitle(string.Empty);
            _sizeValue.style.marginLeft = UiKit.SpaceMd;
            _sizeValue.style.marginRight = UiKit.SpaceMd;
            sizeRow.Add(_sizeValue);
            _sizePlus = UiKit.MenuButton("+", () => ChangeSize(+1));
            _sizePlus.style.width = 56;
            sizeRow.Add(_sizePlus);

            _modeCaption = UiKit.Caption(string.Empty);
            _modeCaption.style.marginTop = UiKit.SpaceSm;
            _modeCaption.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_modeCaption);

            _createButton = UiKit.PrimaryButton(string.Empty, () => CreateClicked?.Invoke());
            _createButton.style.marginTop = UiKit.SpaceMd;
            card.Add(_createButton);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            card.Add(_status);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceMd;
            col.Add(_backButton);

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
