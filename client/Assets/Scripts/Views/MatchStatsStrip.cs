using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// The match's figures while it is being watched: a possession bar in the two kit colours and
    /// a wrapping row of counts — shots, on target, fouls, cards, corners.
    ///
    /// Dumb, like every view here: it is handed a <see cref="MatchLiveStats"/> and draws it. What
    /// it is handed is the match SO FAR (the renderer counts as playback crosses the stream), so
    /// the strip can sit on screen throughout without giving away a goal nobody has watched yet.
    ///
    /// One strip rather than a column of rows, because this sits above the pitch and every pixel it
    /// takes is a pixel of football: two lines at phone width, and the counts wrap rather than
    /// scroll.
    /// </summary>
    public sealed class MatchStatsStrip
    {
        private static readonly Color BarColor = UiKit.SurfaceDeep;
        private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.10f);

        private readonly Label _homePossession;
        private readonly Label _awayPossession;
        private readonly VisualElement _homeBar;
        private readonly VisualElement _awayBar;
        private readonly Counter[] _counters;

        public VisualElement Root { get; }

        /// <summary>One "home — label — away" triplet of the counts row.</summary>
        private readonly struct Counter
        {
            public readonly Label Home;
            public readonly Label Away;
            public readonly Func<MatchLiveStats, int> ReadHome;
            public readonly Func<MatchLiveStats, int> ReadAway;

            public Counter(Label home, Label away,
                Func<MatchLiveStats, int> readHome, Func<MatchLiveStats, int> readAway)
            {
                Home = home;
                Away = away;
                ReadHome = readHome;
                ReadAway = readAway;
            }
        }

        public MatchStatsStrip(Func<string, string> tr, Color homeColor, Color awayColor)
        {
            Root = new VisualElement();
            Root.style.backgroundColor = BarColor;
            Root.style.paddingTop = 6;
            Root.style.paddingBottom = 6;
            Root.style.paddingLeft = 12;
            Root.style.paddingRight = 12;

            // --- possession ------------------------------------------------------------------
            var possessionRow = new VisualElement();
            possessionRow.style.flexDirection = FlexDirection.Row;
            possessionRow.style.alignItems = Align.Center;

            _homePossession = ValueLabel(homeColor);
            _homePossession.style.minWidth = 44;
            _homePossession.style.unityTextAlign = TextAnchor.MiddleLeft;
            possessionRow.Add(_homePossession);

            var track = new VisualElement();
            track.style.flexGrow = 1f;
            track.style.flexDirection = FlexDirection.Row;
            track.style.height = 8;
            track.style.backgroundColor = TrackColor;
            track.style.borderTopLeftRadius = 4;
            track.style.borderTopRightRadius = 4;
            track.style.borderBottomLeftRadius = 4;
            track.style.borderBottomRightRadius = 4;
            track.style.overflow = Overflow.Hidden;
            track.style.marginLeft = 8;
            track.style.marginRight = 8;

            _homeBar = new VisualElement();
            _homeBar.style.backgroundColor = homeColor;
            _homeBar.style.width = Length.Percent(50);
            _homeBar.style.flexShrink = 0f;
            track.Add(_homeBar);

            _awayBar = new VisualElement();
            _awayBar.style.backgroundColor = awayColor;
            _awayBar.style.flexGrow = 1f;
            track.Add(_awayBar);

            possessionRow.Add(track);

            _awayPossession = ValueLabel(awayColor);
            _awayPossession.style.minWidth = 44;
            _awayPossession.style.unityTextAlign = TextAnchor.MiddleRight;
            possessionRow.Add(_awayPossession);

            Root.Add(possessionRow);

            var possessionCaption = CaptionLabel(tr("match.stats.possession"));
            possessionCaption.style.unityTextAlign = TextAnchor.MiddleCenter;
            possessionCaption.style.marginTop = 1;
            Root.Add(possessionCaption);

            // --- the counts ------------------------------------------------------------------
            var counts = new VisualElement();
            counts.style.flexDirection = FlexDirection.Row;
            counts.style.flexWrap = Wrap.Wrap;
            counts.style.justifyContent = Justify.Center;
            counts.style.marginTop = 4;

            _counters = new[]
            {
                AddCounter(counts, tr("match.stats.shots"), s => s.Home.Shots, s => s.Away.Shots),
                AddCounter(counts, tr("match.stats.on_target"), s => s.Home.ShotsOnTarget, s => s.Away.ShotsOnTarget),
                AddCounter(counts, tr("match.stats.corners"), s => s.Home.Corners, s => s.Away.Corners),
                AddCounter(counts, tr("match.stats.fouls"), s => s.Home.Fouls, s => s.Away.Fouls),
                AddCounter(counts, tr("match.stats.yellows"), s => s.Home.YellowCards, s => s.Away.YellowCards),
            };

            Root.Add(counts);
            Set(default);
        }

        /// <summary>Redraws the strip from the figures of the match so far.</summary>
        public void Set(MatchLiveStats stats)
        {
            int home = stats.HomePossessionPercent;
            _homePossession.text = home + "%";
            _awayPossession.text = (100 - home) + "%";
            _homeBar.style.width = Length.Percent(home);

            foreach (Counter c in _counters)
            {
                c.Home.text = c.ReadHome(stats).ToString();
                c.Away.text = c.ReadAway(stats).ToString();
            }
        }

        public void SetVisible(bool visible) =>
            Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        private Counter AddCounter(
            VisualElement parent, string caption,
            Func<MatchLiveStats, int> readHome, Func<MatchLiveStats, int> readAway)
        {
            var item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            item.style.marginLeft = 7;
            item.style.marginRight = 7;

            Label home = ValueLabel(Color.white);
            Label label = CaptionLabel(caption);
            label.style.marginLeft = 5;
            label.style.marginRight = 5;
            Label away = ValueLabel(Color.white);

            item.Add(home);
            item.Add(label);
            item.Add(away);
            parent.Add(item);

            return new Counter(home, away, readHome, readAway);
        }

        private static Label ValueLabel(Color color)
        {
            var label = new Label("0");
            label.style.color = color;
            label.AddToClassList("fts-t-body");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Label CaptionLabel(string text)
        {
            var label = new Label(text);
            label.style.color = new Color(1f, 1f, 1f, 0.55f);
            label.AddToClassList("fts-t-small");
            return label;
        }
    }
}
