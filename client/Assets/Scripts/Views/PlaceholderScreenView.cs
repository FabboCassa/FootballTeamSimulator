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
            // Task 6.9: navy background + a section Header (the shell top bar gives context).
            Root = UiKit.Screen(UiKit.Background);
            Root.Add(UiKit.Header(title));
            Root.Add(UiKit.Subtitle(tr("placeholder.note")));
            Root.Add(UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke()));
        }
    }
}
