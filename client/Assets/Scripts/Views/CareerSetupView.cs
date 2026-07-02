using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// New-career setup: shows the generated league, a difficulty selector, and lets the user pick
    /// a club. Dumb view; the presenter owns generation, difficulty state and selection logic.
    /// </summary>
    public sealed class CareerSetupView
    {
        public event Action<int> ClubSelected;
        public event Action RerollClicked;
        public event Action BackClicked;
        /// <summary>Raised when the user taps a difficulty button (0 = Easy, 1 = Normal, 2 = Hard).</summary>
        public event Action<int> DifficultySelected;

        public VisualElement Root { get; }

        private readonly Label _leagueLabel;
        private readonly ScrollView _clubList;
        private readonly Button[] _difficultyButtons = new Button[3];
        private readonly Label _difficultyDesc;
        private readonly Func<string, string> _tr;

        private static readonly string[] DifficultyKeys = { "difficulty.easy", "difficulty.normal", "difficulty.hard" };
        private static readonly string[] DifficultyDescKeys =
            { "difficulty.desc.easy", "difficulty.desc.normal", "difficulty.desc.hard" };

        public CareerSetupView(Func<string, string> tr)
        {
            _tr = tr;
            // Task 6.6: a top-anchored page with NO outer scroller — the club list below owns
            // the scrolling. Fixes the 6.4 device bugs: double scrollbar (outer page + inner
            // list) and the big empty gap above the vertically-centred content.
            Root = UiKit.Page(UiKit.HubBlue);
            var title = UiKit.Title(tr("career_setup.title"));
            title.style.fontSize = UiKit.FontHeader;
            title.style.marginBottom = UiKit.SpaceSm;
            Root.Add(title);

            _leagueLabel = UiKit.Subtitle(string.Empty);
            Root.Add(_leagueLabel);

            // Difficulty selector (task 5.7b): three buttons, the chosen one highlighted.
            Root.Add(UiKit.Subtitle(tr("career_setup.difficulty")));
            var difficultyRow = new VisualElement();
            difficultyRow.style.flexDirection = FlexDirection.Row;
            difficultyRow.style.marginBottom = 4;
            for (int i = 0; i < _difficultyButtons.Length; i++)
            {
                int level = i;
                var button = UiKit.MenuButton(tr(DifficultyKeys[i]), () => DifficultySelected?.Invoke(level));
                button.style.width = 112;
                button.style.height = 40;
                button.style.fontSize = 16;
                button.style.marginLeft = 3;
                button.style.marginRight = 3;
                _difficultyButtons[i] = button;
                difficultyRow.Add(button);
            }
            Root.Add(difficultyRow);

            _difficultyDesc = UiKit.Subtitle(string.Empty);
            _difficultyDesc.style.fontSize = 14;
            Root.Add(_difficultyDesc);

            Root.Add(UiKit.Subtitle(tr("career_setup.pick_club")));

            // The ONLY scroller on this screen: fills the remaining height between the header
            // block above and the button row below (flexGrow), scrolls when the league is long.
            _clubList = new ScrollView(ScrollViewMode.Vertical);
            _clubList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _clubList.style.flexGrow = 1f;
            _clubList.style.width = 340;
            Root.Add(_clubList);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 12;
            var reroll = UiKit.MenuButton(tr("career_setup.reroll"), () => RerollClicked?.Invoke());
            var back = UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke());
            reroll.style.width = 165;
            back.style.width = 165;
            buttonRow.Add(reroll);
            buttonRow.Add(back);
            Root.Add(buttonRow);
        }

        /// <summary>Highlights the chosen difficulty button (0/1/2) and shows its description.</summary>
        public void SetDifficulty(int level)
        {
            for (int i = 0; i < _difficultyButtons.Length; i++)
            {
                bool selected = i == level;
                _difficultyButtons[i].style.backgroundColor = selected
                    ? new StyleColor(UiKit.AccentDark)
                    : new StyleColor(StyleKeyword.Null);
                _difficultyButtons[i].style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            }

            if (level >= 0 && level < DifficultyDescKeys.Length)
                _difficultyDesc.text = _tr(DifficultyDescKeys[level]);
        }

        public void SetLeague(string leagueName, IReadOnlyList<KeyValuePair<int, string>> clubs)
        {
            _leagueLabel.text = leagueName;
            _clubList.Clear();

            foreach (var club in clubs)
            {
                int clubId = club.Key;
                var button = UiKit.MenuButton(club.Value, () => ClubSelected?.Invoke(clubId));
                button.style.width = 300;
                button.style.height = 40;
                button.style.fontSize = 16;
                _clubList.Add(button);
            }
        }
    }
}
