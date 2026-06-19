using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// New-career setup: shows the generated league and lets the user pick a club.
    /// Dumb view; the presenter owns generation and selection logic.
    /// </summary>
    public sealed class CareerSetupView
    {
        public event Action<int> ClubSelected;
        public event Action RerollClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _leagueLabel;
        private readonly ScrollView _clubList;

        public CareerSetupView(Func<string, string> tr)
        {
            Root = UiKit.Screen(UiKit.HubBlue);
            Root.Add(UiKit.Title(tr("career_setup.title")));

            _leagueLabel = UiKit.Subtitle(string.Empty);
            Root.Add(_leagueLabel);
            Root.Add(UiKit.Subtitle(tr("career_setup.pick_club")));

            _clubList = new ScrollView();
            _clubList.style.maxHeight = new Length(45f, LengthUnit.Percent);
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
