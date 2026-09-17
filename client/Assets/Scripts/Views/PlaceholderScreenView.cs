using System;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>Generic placeholder screen with a title and a Back button (centred page, task 14.4).</summary>
    public sealed class PlaceholderScreenView
    {
        public event Action BackClicked;

        public VisualElement Root { get; }

        public PlaceholderScreenView(string title, Func<string, string> tr)
        {
            PageParts page = UiKit.CenterPage(string.Empty, title);
            Root = page.Root;
            VisualElement card = UiKit.OptionCard();
            card.Add(EmptyState.Build("overview", tr("placeholder.note")));
            page.Column.Add(card);
            page.Column.Add(UiKit.GhostButton(tr("common.back"), () => BackClicked?.Invoke()));
        }
    }
}
