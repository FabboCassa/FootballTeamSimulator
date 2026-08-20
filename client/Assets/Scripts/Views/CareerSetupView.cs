using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// New-career setup: choose the world (task 11.1 — which nation you play in, how deep its
    /// pyramid runs, and how much of the rest of the planet is loaded), pick a difficulty, then
    /// drill down nation → division → club.
    ///
    /// Two layout rules this screen learned the hard way (task 11.1b):
    ///
    ///   • ONE scroller, and every other block is flexShrink = 0. UI Toolkit shrinks flex children
    ///     before it ever overflows a container, and a Label squashed to zero height still PAINTS its
    ///     text — which is why the first build had the database caption sitting on top of the chips
    ///     and the footer buttons pushed off the bottom. Only the list may shrink, and it carries
    ///     minHeight = 0 so it actually can.
    ///   • The list is one control in three modes (nations, divisions, clubs) rather than three
    ///     stacked lists. Sixty-five nations and a whole pyramid of clubs do not fit on screen
    ///     together, and a second scroller would bring back the double-scrollbar bug task 6.6 fixed.
    ///
    /// Dumb view; the presenter owns generation, scope state, the current mode and selection logic.
    /// </summary>
    public sealed class CareerSetupView
    {
        public event Action<int> ClubSelected;
        public event Action RerollClicked;
        public event Action BackClicked;
        /// <summary>Raised when the user taps a difficulty button (0 = Easy, 1 = Normal, 2 = Hard).</summary>
        public event Action<int> DifficultySelected;
        /// <summary>Raised when the user taps the nation row — the presenter answers with SetNations.</summary>
        public event Action NationPickerRequested;
        /// <summary>Raised when the user picks a nation from the list (its index in the offered list).</summary>
        public event Action<int> NationSelected;
        /// <summary>Raised when the user taps the "playable divisions" row, which cycles its value.</summary>
        public event Action TiersCycled;
        /// <summary>Raised when the user taps a database-size chip (0 = Small, 1 = Medium, 2 = Large).</summary>
        public event Action<int> DatabaseSizeSelected;
        /// <summary>Raised when the user picks a division to browse (its league id).</summary>
        public event Action<int> LeagueSelected;
        /// <summary>Raised by the "back" row at the top of the club or nation list.</summary>
        public event Action BackToLeaguesClicked;

        public VisualElement Root { get; }

        private readonly Label _summaryLabel;
        private readonly ScrollView _list;
        private readonly Button _nationButton;
        private readonly Button _tiersButton;
        private readonly Button[] _databaseChips = new Button[3];
        private readonly Label _databaseDesc;
        private readonly Label _pickLabel;
        private readonly Button[] _difficultyButtons = new Button[3];
        private readonly Label _difficultyDesc;
        private readonly Func<string, string> _tr;

        private static readonly string[] DifficultyKeys = { "difficulty.easy", "difficulty.normal", "difficulty.hard" };
        private static readonly string[] DifficultyDescKeys =
            { "difficulty.desc.easy", "difficulty.desc.normal", "difficulty.desc.hard" };
        private static readonly string[] DatabaseKeys =
            { "career_setup.db.small", "career_setup.db.medium", "career_setup.db.large" };

        private const float ContentWidth = 340f;

        public CareerSetupView(Func<string, string> tr)
        {
            _tr = tr;
            // Task 6.6: a top-anchored page with NO outer scroller — the list below owns the
            // scrolling. Fixes the 6.4 device bugs: double scrollbar and the empty gap above.
            Root = UiKit.Page(UiKit.HubBlue);

            var title = UiKit.Title(tr("career_setup.title"));
            title.style.fontSize = UiKit.FontHeader;
            title.style.marginBottom = 2;
            Fixed(title);
            Root.Add(title);

            _summaryLabel = UiKit.Subtitle(string.Empty);
            _summaryLabel.style.marginBottom = 6;
            _summaryLabel.style.fontSize = 13;
            Fixed(_summaryLabel);
            Root.Add(_summaryLabel);

            // --- the world (task 11.1b) ---------------------------------------------------------
            // Both rows are tappable; the accent border and the ▶ marker say so, and the hint under
            // them says it in words, because "a label that happens to be a button" is exactly the
            // kind of thing a player never discovers.
            _nationButton = TappableRow(() => NationPickerRequested?.Invoke());
            Root.Add(_nationButton);

            _tiersButton = TappableRow(() => TiersCycled?.Invoke());
            Root.Add(_tiersButton);

            Label hint = UiKit.Caption(tr("career_setup.tap_hint"));
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.width = ContentWidth;
            hint.style.marginBottom = 6;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            Fixed(hint);
            Root.Add(hint);

            Label databaseCaption = UiKit.SectionLabel(tr("career_setup.database"));
            databaseCaption.style.width = ContentWidth;
            Root.Add(databaseCaption);

            var databaseRow = new VisualElement();
            databaseRow.style.flexDirection = FlexDirection.Row;
            databaseRow.style.width = ContentWidth;
            Fixed(databaseRow);
            for (int i = 0; i < _databaseChips.Length; i++)
            {
                int size = i;
                Button chip = UiKit.ChipButton(tr(DatabaseKeys[i]), () => DatabaseSizeSelected?.Invoke(size));
                _databaseChips[i] = chip;
                databaseRow.Add(chip);
            }
            Root.Add(databaseRow);

            // One paragraph, not two: what the preset means AND the reminder that the choice is
            // final. Every extra fixed line here is a line stolen from the club list below.
            _databaseDesc = UiKit.HelpText(string.Empty);
            _databaseDesc.style.width = ContentWidth;
            _databaseDesc.style.fontSize = 12;
            _databaseDesc.style.marginBottom = 4;
            Root.Add(_databaseDesc);

            // --- difficulty (task 5.7b) ---------------------------------------------------------
            Label difficultyCaption = UiKit.SectionLabel(tr("career_setup.difficulty"));
            difficultyCaption.style.width = ContentWidth;
            Root.Add(difficultyCaption);

            var difficultyRow = new VisualElement();
            difficultyRow.style.flexDirection = FlexDirection.Row;
            difficultyRow.style.marginBottom = 2;
            Fixed(difficultyRow);
            for (int i = 0; i < _difficultyButtons.Length; i++)
            {
                int level = i;
                Button button = UiKit.MenuButton(tr(DifficultyKeys[i]), () => DifficultySelected?.Invoke(level));
                button.style.width = 108;
                button.style.height = 36;
                button.style.fontSize = 15;
                button.style.marginLeft = 3;
                button.style.marginRight = 3;
                button.style.marginTop = 0;
                button.style.marginBottom = 0;
                Fixed(button);
                _difficultyButtons[i] = button;
                difficultyRow.Add(button);
            }
            Root.Add(difficultyRow);

            _difficultyDesc = UiKit.Caption(string.Empty);
            _difficultyDesc.style.whiteSpace = WhiteSpace.Normal;
            _difficultyDesc.style.width = ContentWidth;
            _difficultyDesc.style.unityTextAlign = TextAnchor.MiddleCenter;
            _difficultyDesc.style.marginBottom = 6;
            Fixed(_difficultyDesc);
            Root.Add(_difficultyDesc);

            _pickLabel = UiKit.SectionLabel(tr("career_setup.pick_league"));
            _pickLabel.style.width = ContentWidth;
            Root.Add(_pickLabel);

            // The ONLY scroller on this screen. flexGrow takes the leftover height, minHeight 0 lets
            // it actually give height back when the window is short — without it the list refuses to
            // shrink and pushes the footer off the bottom.
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _list.style.flexGrow = 1f;
            _list.style.flexShrink = 1f;
            _list.style.minHeight = 0f;
            _list.style.width = ContentWidth;
            // Rows are narrower than the scroller (the scrollbar needs the difference), so centre
            // them or the whole list sits visibly off to the left of everything above it.
            _list.contentContainer.style.alignItems = Align.Center;
            Root.Add(_list);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 8;
            Fixed(buttonRow);
            Button reroll = UiKit.MenuButton(tr("career_setup.reroll"), () => RerollClicked?.Invoke());
            Button back = UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke());
            foreach (Button button in new[] { reroll, back })
            {
                button.style.width = 165;
                button.style.height = 44;
                button.style.fontSize = 15;
                Fixed(button);
            }
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

        /// <summary>The nation row's caption, already formatted by the presenter.</summary>
        public void SetNation(string caption) => _nationButton.text = caption;

        /// <summary>The playable-divisions row's caption, already formatted by the presenter.</summary>
        public void SetTiers(string caption) => _tiersButton.text = caption;

        /// <summary>The line under the title: which divisions this world will run.</summary>
        public void SetSummary(string summary) => _summaryLabel.text = summary;

        /// <summary>Lights the chosen database-size chip (0/1/2) and shows what it means.</summary>
        public void SetDatabaseSize(int size)
        {
            for (int i = 0; i < _databaseChips.Length; i++)
                UiKit.SetChipActive(_databaseChips[i], i == size);

            if (size >= 0 && size < DatabaseKeys.Length)
                _databaseDesc.text = _tr(DatabaseKeys[size] + ".desc") + " " + _tr("career_setup.scope_fixed");
        }

        /// <summary>Division-picking mode: one row per playable division.</summary>
        public void SetLeagues(IReadOnlyList<KeyValuePair<int, string>> leagues)
        {
            _pickLabel.text = _tr("career_setup.pick_league");
            _list.Clear();

            foreach (KeyValuePair<int, string> league in leagues)
            {
                int leagueId = league.Key;
                _list.Add(ListRow(league.Value, () => LeagueSelected?.Invoke(leagueId)));
            }

            _list.scrollOffset = Vector2.zero;
        }

        /// <summary>Club-picking mode: the clubs of ONE division, with a way back up.</summary>
        public void SetClubs(string leagueName, IReadOnlyList<KeyValuePair<int, string>> clubs, bool canGoBack)
        {
            _pickLabel.text = leagueName;
            _list.Clear();

            if (canGoBack)
                _list.Add(ListRow(_tr("career_setup.back_to_leagues"), () => BackToLeaguesClicked?.Invoke()));

            foreach (KeyValuePair<int, string> club in clubs)
            {
                int clubId = club.Key;
                _list.Add(ListRow(club.Value, () => ClubSelected?.Invoke(clubId)));
            }

            _list.scrollOffset = Vector2.zero;
        }

        /// <summary>Nation-picking mode: the same list, filled with nations instead.</summary>
        public void SetNations(IReadOnlyList<string> nations)
        {
            _pickLabel.text = _tr("career_setup.pick_nation");
            _list.Clear();
            _list.Add(ListRow(_tr("career_setup.cancel"), () => BackToLeaguesClicked?.Invoke()));

            for (int i = 0; i < nations.Count; i++)
            {
                int index = i;
                _list.Add(ListRow(nations[i], () => NationSelected?.Invoke(index)));
            }

            _list.scrollOffset = Vector2.zero;
        }

        // ---------------------------------------------------------------- building blocks

        /// <summary>A row of the one list: same size everywhere, so the three modes read alike.</summary>
        private static Button ListRow(string text, Action onClick)
        {
            Button button = UiKit.MenuButton(text, onClick);
            button.style.width = 310;
            button.style.height = 38;
            button.style.fontSize = 15;
            button.style.marginTop = 2;
            button.style.marginBottom = 2;
            Fixed(button);
            return button;
        }

        /// <summary>One of the two tappable setting rows above the list.</summary>
        private static Button TappableRow(Action onClick)
        {
            Button button = UiKit.CycleButton(onClick);
            button.style.width = ContentWidth;
            button.style.height = 36;
            button.style.fontSize = 14;
            button.style.marginBottom = 4;
            UiKit.SetBorder(button, UiKit.Accent, 1);
            Fixed(button);
            return button;
        }

        /// <summary>
        /// Marks an element as part of the fixed frame. UI Toolkit shrinks flex children before it
        /// overflows, and a squashed Label still paints its text over its neighbours — so everything
        /// that is not the list must refuse to shrink.
        /// </summary>
        private static void Fixed(VisualElement element) => element.style.flexShrink = 0f;
    }
}
