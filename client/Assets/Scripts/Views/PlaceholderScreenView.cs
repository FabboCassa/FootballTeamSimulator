using System;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>Generic placeholder screen with a title and a Back button.</summary>
    public sealed class PlaceholderScreenView
    {
        public event Action BackClicked;

        public VisualElement Root { get; }

        public PlaceholderScreenView(string title, Func<string, string> tr)
        {
            Root = UiKit.Screen(UiKit.PanelGray);
            Root.Add(UiKit.Title(title));
            Root.Add(UiKit.Subtitle(tr("placeholder.note")));
            Root.Add(UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke()));
        }
    }
}
