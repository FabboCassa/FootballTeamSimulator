using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// The database bench (task 11.3, dev only): three buttons and a log.
    ///
    /// Its whole reason to exist is that the shipped default database size has to be decided by a
    /// measurement taken on the WEAKEST target — a WebGL build and a mid-range Android phone — and
    /// there is no way to take that measurement from a desktop harness. So the measurement comes
    /// with the game, behind a dev flag, and prints numbers you can read off the screen (and out of
    /// the player log) wherever the build runs.
    ///
    /// Dumb view: the presenter runs the bench and hands over already-formatted lines.
    /// </summary>
    public sealed class WorldBenchView
    {
        public event Action<int> PresetClicked;
        public event Action ClearClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly Label _help;
        private readonly ScrollView _log;
        private readonly List<Button> _buttons = new List<Button>();

        public WorldBenchView(Func<string, string> tr)
        {
            Root = UiKit.ScreenRoot();

            var col = UiKit.PageColumn(UiKit.WidthMedium);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.ScreenTitle(tr("bench.title"));
            _header.style.flexShrink = 0f;
            col.Add(_header);

            _help = UiKit.HelpText(tr("bench.help"));
            _help.style.flexShrink = 0f;
            col.Add(_help);

            var buttons = UiKit.Row();
            buttons.style.flexShrink = 0f;
            buttons.style.flexWrap = Wrap.Wrap;
            buttons.style.marginBottom = UiKit.SpaceSm;
            AddButton(buttons, tr("bench.small"), 0);
            AddButton(buttons, tr("bench.medium"), 1);
            AddButton(buttons, tr("bench.large"), 2);

            Button clear = UiKit.SmallButton(tr("bench.clear"), () => ClearClicked?.Invoke(), 120f);
            clear.style.marginRight = 6;
            clear.style.marginBottom = 4;
            _buttons.Add(clear);
            buttons.Add(clear);
            col.Add(buttons);

            VisualElement panel = UiKit.Panel(grow: true);
            panel.style.minHeight = 0f;
            _log = UiKit.ListScroll();
            _log.style.minHeight = 0f;
            panel.Add(_log);
            col.Add(panel);

            VisualElement footer = UiKit.FooterBar();
            footer.Add(UiKit.FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);
        }

        public void SetHeader(string text) => _header.text = text;

        /// <summary>Locks the buttons while a preset is being generated — the run blocks the frame.</summary>
        public void SetBusy(bool busy)
        {
            foreach (Button button in _buttons)
                button.SetEnabled(!busy);
        }

        public void SetLines(IReadOnlyList<string> lines)
        {
            _log.Clear();
            if (lines == null)
                return;

            foreach (string line in lines)
            {
                var label = new Label(line);
                label.style.fontSize = 12;
                label.style.color = UiKit.TextPrimary;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginBottom = 4;
                _log.Add(label);
            }
        }

        private void AddButton(VisualElement row, string text, int preset)
        {
            Button button = UiKit.SmallButton(text, () => PresetClicked?.Invoke(preset), 150f);
            button.style.marginRight = 6;
            button.style.marginBottom = 4;
            _buttons.Add(button);
            row.Add(button);
        }
    }
}
