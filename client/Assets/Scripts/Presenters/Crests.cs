using Fts.Services;
using Fts.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Presenter-side bridge (task 6.8) that turns a club's <see cref="ClubVisual"/> (Services)
    /// into a ready-to-add view element (<see cref="CrestRenderer"/> / <see cref="PlayerAvatarView"/>
    /// from Views). Presenters can reference both layers; the dumb views only ever receive the
    /// finished VisualElement, so they stay free of Sim.Core / Services types. Centralizes the
    /// argument order so every screen renders crests and portraits identically.
    /// </summary>
    public static class Crests
    {
        /// <summary>A club crest badge. <paramref name="mask"/> must match the surface behind it so
        /// the even-odd silhouette clip shows no halo (rows sit on <see cref="UiKit.Surface"/>).</summary>
        public static CrestRenderer Badge(ClubVisual v, float size, string initials, Color mask)
        {
            return new CrestRenderer(size, v.Shape, v.Pattern,
                v.Primary, v.Secondary, v.Accent, v.Emblem, mask, initials);
        }

        /// <summary>Crest badge on the default row surface.</summary>
        public static CrestRenderer Badge(ClubVisual v, float size, string initials) =>
            Badge(v, size, initials, UiKit.Surface);

        /// <summary>A player monogram "portrait" in his club's kit colours (task 6.8 placeholder).</summary>
        public static PlayerAvatarView Avatar(ClubVisual v, float size, string playerName)
        {
            return new PlayerAvatarView(size, v.Primary, v.Accent, v.Emblem,
                PlayerAvatarView.InitialsOf(playerName));
        }
    }
}
