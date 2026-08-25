using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A standings row, preformatted by the presenter (task 8.3b).</summary>
    public sealed class StandingRowVm
    {
        public int Pos;
        public string ClubName;
        public int Played, Won, Drawn, Lost, GoalsFor, GoalsAgainst, GoalDifference, Points;
        public bool IsYours;
    }

    /// <summary>A fixture row: id (for the replay), the two clubs, and the score once played.</summary>
    public sealed class SeasonFixtureRowVm
    {
        public string FixtureId;
        public string HomeName;
        public string AwayName;
        public bool Played;
        public int HomeGoals, AwayGoals;
        public bool IsYours;
        /// <summary>Your unplayed fixture in the current round — tappable to open the live match (8.6b).</summary>
        public bool CanPlayLive;
    }

    /// <summary>A matchday: a label + its fixtures.</summary>
    public sealed class FixtureGroupVm
    {
        public string RoundLabel;
        public IReadOnlyList<SeasonFixtureRowVm> Rows;
    }

    /// <summary>
    /// The shared table pieces for the ONLINE competition screens (private league, ranked, season end),
    /// built to the same recipe as the single-player <see cref="LeagueView"/> table: a real header row,
    /// roomy numeric columns, zebra striping and a green tint on your own club. One place, so the online
    /// screens and the single-player league never drift apart again (UI pass on the online flow).
    /// </summary>
    public static class OnlineTableKit
    {
        public const float PosWidth = 34f;
        public const float StatWidth = 42f;
        public const float PointsWidth = 52f;
        public const float RowHeight = 32f;
        public const float SideSlotWidth = 64f;
        public const float ScoreWidth = 74f;

        // --- standings ---------------------------------------------------------------------------

        /// <summary>The sticky header row of a standings table (kept OUTSIDE the scroller).</summary>
        public static VisualElement StandingsHeader(Func<string, string> tr)
        {
            var row = new VisualElement();
            row.AddToClassList("fts-thead");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 28;
            row.style.flexShrink = 0f;
            row.style.paddingLeft = UiKit.SpaceSm;
            row.style.paddingRight = UiKit.SpaceSm;
            row.style.marginBottom = UiKit.SpaceXs;
            if (!UiKit.StylesLoaded)
            {
                row.style.backgroundColor = UiKit.SurfaceAlt;
                UiKit.Round(row, 8);
            }

            row.Add(Cell(tr("league.col.pos"), PosWidth, UiKit.TextMuted, TextAnchor.MiddleLeft, 12, true));
            Label club = Cell(tr("league.col.club"), 0, UiKit.TextMuted, TextAnchor.MiddleLeft, 12, true);
            Grow(club);
            row.Add(club);
            row.Add(Cell(tr("league.col.p"), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 12, true));
            row.Add(Cell(tr("league.col.w"), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 12, true));
            row.Add(Cell(tr("league.col.d"), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 12, true));
            row.Add(Cell(tr("league.col.l"), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 12, true));
            row.Add(Cell(tr("league.col.gf"), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 12, true));
            row.Add(Cell(tr("league.col.ga"), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 12, true));
            row.Add(Cell(tr("league.col.gd"), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 12, true));
            row.Add(Cell(tr("league.col.pts"), PointsWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 12, true));
            return row;
        }

        /// <summary>One standings row; <paramref name="index"/> drives the zebra striping.</summary>
        public static VisualElement StandingsRow(StandingRowVm vm, int index)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = RowHeight;
            row.style.flexShrink = 0f;
            row.style.paddingLeft = UiKit.SpaceSm;
            row.style.paddingRight = UiKit.SpaceSm;
            Stripe(row, vm.IsYours, index);

            Label pos = Cell(vm.Pos.ToString(), PosWidth, UiKit.TextMuted, TextAnchor.MiddleLeft, 13, true);
            row.Add(pos);

            Label club = Cell(vm.ClubName, 0, UiKit.TextPrimary, TextAnchor.MiddleLeft, 14, true);
            Grow(club);
            row.Add(club);

            row.Add(Cell(vm.Played.ToString(), StatWidth, UiKit.TextPrimary, TextAnchor.MiddleRight, 13, false));
            row.Add(Cell(vm.Won.ToString(), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 13, false));
            row.Add(Cell(vm.Drawn.ToString(), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 13, false));
            row.Add(Cell(vm.Lost.ToString(), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 13, false));
            row.Add(Cell(vm.GoalsFor.ToString(), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 13, false));
            row.Add(Cell(vm.GoalsAgainst.ToString(), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 13, false));
            row.Add(Cell(Signed(vm.GoalDifference), StatWidth, UiKit.TextMuted, TextAnchor.MiddleRight, 13, false));

            Label points = Cell(vm.Points.ToString(), PointsWidth, UiKit.Accent, TextAnchor.MiddleRight, 15, true);
            row.Add(points);
            return row;
        }

        // --- schedule ----------------------------------------------------------------------------

        /// <summary>The matchday caption that introduces a block of fixtures inside the scroller.</summary>
        public static Label RoundHeader(string text, bool first)
        {
            Label label = UiKit.SectionLabel(text);
            label.style.marginTop = first ? 0 : UiKit.SpaceSm;
            label.style.marginBottom = UiKit.SpaceXs;
            label.style.paddingLeft = UiKit.SpaceSm;
            return label;
        }

        /// <summary>
        /// One fixture line: home (right-aligned) · score / "vs" · away (left-aligned), with a fixed
        /// slot on both flanks so the score column stays centred whether or not the row carries the
        /// live badge — no more names sliding over each other.
        /// </summary>
        public static VisualElement FixtureRow(
            SeasonFixtureRowVm vm, int index, string vsText, string liveText,
            Action<string> onPlayed, Action<string> onLive)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = RowHeight + 4f;
            row.style.flexShrink = 0f;
            row.style.paddingLeft = UiKit.SpaceSm;
            row.style.paddingRight = UiKit.SpaceSm;
            Stripe(row, vm.IsYours, index);

            bool tappable = (vm.Played && onPlayed != null) || (vm.CanPlayLive && onLive != null);
            row.Add(Slot(SideSlotWidth));

            Label home = Cell(vm.HomeName, 0, UiKit.TextPrimary, TextAnchor.MiddleRight, 14, false);
            Grow(home);
            row.Add(home);

            string mid = vm.Played ? vm.HomeGoals + "–" + vm.AwayGoals : vsText;
            Label score = Cell(
                mid, ScoreWidth,
                vm.Played ? UiKit.TextPrimary : UiKit.TextMuted,
                TextAnchor.MiddleCenter, vm.Played ? 15 : 13, vm.Played);
            row.Add(score);

            Label away = Cell(vm.AwayName, 0, UiKit.TextPrimary, TextAnchor.MiddleLeft, 14, false);
            Grow(away);
            row.Add(away);

            VisualElement trail = Slot(SideSlotWidth);
            if (vm.CanPlayLive && !string.IsNullOrEmpty(liveText))
            {
                Label live = Cell(liveText, SideSlotWidth, UiKit.Accent, TextAnchor.MiddleRight, 12, true);
                trail.Add(live);
            }
            row.Add(trail);

            if (tappable)
            {
                string id = vm.FixtureId;
                if (!string.IsNullOrEmpty(id))
                {
                    if (vm.Played) row.RegisterCallback<ClickEvent>(_ => onPlayed(id));
                    else row.RegisterCallback<ClickEvent>(_ => onLive(id));
                }
            }
            return row;
        }

        // --- helpers -----------------------------------------------------------------------------

        /// <summary>Zebra + "this is your club" tinting, class-driven with an inline fallback.</summary>
        public static void Stripe(VisualElement row, bool isYours, int index)
        {
            row.AddToClassList("fts-trow");
            if (isYours) row.AddToClassList("fts-trow--user");
            else if (index % 2 == 1) row.AddToClassList("fts-trow--zebra");
            if (!UiKit.StylesLoaded)
            {
                row.style.backgroundColor = isYours
                    ? UiKit.Hex(0x1F4A34)
                    : (index % 2 == 1 ? new Color(1f, 1f, 1f, 0.035f) : Color.clear);
                UiKit.Round(row, 6);
            }
        }

        public static Label Cell(string text, float width, Color color, TextAnchor align, int fontSize, bool bold)
        {
            var label = new Label(text ?? string.Empty);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityTextAlign = align;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (width > 0f)
            {
                label.style.width = width;
                label.style.flexShrink = 0f;
            }
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.pickingMode = PickingMode.Ignore; // the row handles the click
            return label;
        }

        private static void Grow(Label label)
        {
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.minWidth = 0f;
        }

        private static VisualElement Slot(float width)
        {
            var slot = new VisualElement();
            slot.style.width = width;
            slot.style.flexShrink = 0f;
            slot.style.flexDirection = FlexDirection.Row;
            slot.style.justifyContent = Justify.FlexEnd;
            slot.style.alignItems = Align.Center;
            slot.pickingMode = PickingMode.Ignore;
            return slot;
        }

        public static string Signed(int value) => value > 0 ? "+" + value : value.ToString();
    }
}
