using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Shared builder for columned player rows (task 6.9): instead of one big rectangle per row,
    /// each field (name, role, age, OVR, …) is its own small rounded "cell" rectangle, and the
    /// role cell is tinted by reparto — GK yellow, defence green, midfield blue, attack red — so
    /// the same table look and colour language is reused on every screen that lists players
    /// (Rosa, Osservatori, Supporto, Mercato). Dumb view helper: it takes a role-GROUP int
    /// (0 GK · 1 def · 2 mid · 3 att) the presenter computes from the Sim.Core role, so no
    /// Sim.Core reference leaks in here.
    /// </summary>
    public static class PlayerRowKit
    {
        public const float RowHeight = 40f;

        // Reparto colours (user-chosen, task 6.9).
        private static readonly Color GkColor  = UiKit.Hex(0xE7B23A); // portiere · giallo
        private static readonly Color DefColor = UiKit.Hex(0x43A85F); // difesa · verde
        private static readonly Color MidColor = UiKit.Hex(0x3E86CC); // centrocampo · azzurro
        private static readonly Color AttColor = UiKit.Hex(0xD25550); // attacco · rosso

        public static Color RoleColor(int group) =>
            group == 0 ? GkColor : group == 1 ? DefColor : group == 2 ? MidColor : AttColor;

        /// <summary>A transparent row that holds the individual cell rectangles.</summary>
        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 5;
            return row;
        }

        /// <summary>An empty cell rectangle (surface box); add custom content into it.</summary>
        public static VisualElement Cell(float width, bool grow = false)
        {
            var cell = new VisualElement();
            cell.style.height = RowHeight;
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.alignItems = Align.Center;
            cell.style.justifyContent = Justify.Center;
            cell.style.paddingLeft = 8;
            cell.style.paddingRight = 8;
            cell.style.marginRight = 5;
            cell.style.backgroundColor = UiKit.Surface;
            UiKit.Round(cell, UiKit.RadiusSm);
            if (grow)
            {
                cell.style.flexGrow = 1f;
                cell.style.flexShrink = 1f;
            }
            else
            {
                cell.style.width = width;
                cell.style.flexShrink = 0f;
            }
            return cell;
        }

        /// <summary>A text cell rectangle (grow = the flexible name column).</summary>
        public static VisualElement TextCell(
            string text, float width, TextAnchor align, bool grow = false, bool bold = false, Color? color = null)
        {
            VisualElement cell = Cell(width, grow);
            cell.style.justifyContent =
                align == TextAnchor.MiddleLeft ? Justify.FlexStart :
                align == TextAnchor.MiddleRight ? Justify.FlexEnd : Justify.Center;

            var label = new Label(text ?? string.Empty);
            label.style.fontSize = 13;
            label.style.color = color ?? UiKit.TextPrimary;
            label.style.unityTextAlign = align;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            if (grow) label.style.flexShrink = 1f;
            cell.Add(label);
            return cell;
        }

        /// <summary>The coloured role cell (reparto colour + role abbreviation).</summary>
        public static VisualElement RoleChip(string abbr, int group, float width = 52f)
        {
            VisualElement cell = Cell(width);
            cell.style.backgroundColor = RoleColor(group);

            var label = new Label(abbr ?? string.Empty);
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            // Dark text on the bright yellow GK chip, white on the others.
            label.style.color = group == 0 ? UiKit.Hex(0x231A00) : Color.white;
            cell.Add(label);
            return cell;
        }

        /// <summary>Tints a cell as selected (e.g. the picked player on the Support screen).</summary>
        public static void SetSelected(VisualElement cell, bool selected)
        {
            cell.style.backgroundColor = selected ? UiKit.Hex(0x2F4A72) : UiKit.Surface;
        }
    }
}
