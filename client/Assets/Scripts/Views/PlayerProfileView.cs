using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// One attribute row: a name, a display string and a 1-100 bar value. For an owned player
    /// <see cref="Text"/> is the exact value; for a scouted player it's a range like "54–72"
    /// and <see cref="BarValue"/> is the estimate (band centre).
    /// </summary>
    public sealed class AttrRowVm
    {
        public string Name;
        public string Text;   // exact value or "min–max"
        public int BarValue;  // 1-100 fill (exact value, or the estimate for a range)
    }

    /// <summary>
    /// The player's live condition, formatted by the presenter (task 4.2 wording):
    /// the strip glyphs plus the three "why" lines shown in full on the profile
    /// (transparency = anti-frustration, ARCHITECTURE.md §4.4).
    /// </summary>
    public sealed class ProfileConditionVm
    {
        public int Fitness;
        public string FormArrow;
        public string MoraleFace;
        public string FitnessLine;
        public string FormLine;
        public string MoraleLine;
    }

    /// <summary>
    /// Player profile (task 4.6): a pushed detail screen opened from the Squad roster.
    /// Shows the full 10-attribute breakdown, live condition with the 4.2 "why" lines,
    /// role/age/overall/potential and season goals. Dumb view — every value and every
    /// string is computed by the presenter; this only lays things out and paints bars.
    /// Market value is intentionally NOT shown here (task 6.7 feedback: money lives only on
    /// the Market screen); scouted ranges, contract and appearances/ratings land with 5.4/5.6.
    /// </summary>
    public sealed class PlayerProfileView
    {
        private static readonly Color BarTrackColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color AttrBarColor = new Color(0.45f, 0.70f, 0.95f);
        private static readonly Color SectionColor = new Color(1f, 1f, 1f, 0.7f);

        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _name;
        private readonly Label _subline;
        private readonly Label _potential;
        private readonly Label _seasonGoals;
        private readonly VisualElement _avatarSlot;
        private readonly VisualElement _conditionSection;
        private readonly VisualElement _conditionBlock;
        private readonly ScrollView _attrList;

        public PlayerProfileView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;

            var col = UiKit.CenteredColumn(680f);
            col.style.flexGrow = 1f;
            Root.Add(col);

            // Portrait placeholder (task 6.8), centred above the name.
            _avatarSlot = new VisualElement();
            _avatarSlot.style.alignItems = Align.Center;
            _avatarSlot.style.justifyContent = Justify.Center;
            _avatarSlot.style.marginBottom = 6;
            col.Add(_avatarSlot);

            _name = UiKit.Header(string.Empty);
            _name.style.unityTextAlign = TextAnchor.MiddleCenter;
            _name.style.marginBottom = 2;
            col.Add(_name);

            _subline = UiKit.Subtitle(string.Empty);
            _subline.style.marginBottom = 4;
            col.Add(_subline);

            _potential = new Label(string.Empty);
            _potential.style.color = SectionColor;
            _potential.style.fontSize = 13;
            _potential.style.unityTextAlign = TextAnchor.MiddleCenter;
            _potential.style.marginBottom = 10;
            col.Add(_potential);

            var body = new ScrollView();
            body.style.flexGrow = 1f;
            body.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(body);

            _conditionSection = new VisualElement();
            _conditionSection.Add(SectionLabel(tr("profile.condition_caption")));
            _conditionBlock = new VisualElement();
            _conditionBlock.style.marginBottom = 12;
            _conditionSection.Add(_conditionBlock);
            body.Add(_conditionSection);

            body.Add(SectionLabel(tr("profile.attributes_caption")));
            _attrList = new ScrollView();
            body.Add(_attrList);

            _seasonGoals = new Label(string.Empty);
            _seasonGoals.style.color = SectionColor;
            _seasonGoals.style.fontSize = 14;
            _seasonGoals.style.marginTop = 10;
            body.Add(_seasonGoals);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 10;
            var back = UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke());
            back.style.width = 150;
            back.style.height = 44;
            back.style.fontSize = 16;
            footer.Add(back);
            footer.style.flexShrink = 0f;
            col.Add(footer);
        }

        public void SetIdentity(string name, string subline, string potential)
        {
            _name.text = name;
            _subline.text = subline;
            _potential.text = potential;
        }

        /// <summary>Sets the player's portrait placeholder (a monogram avatar built by the presenter).</summary>
        public void SetAvatar(VisualElement avatar)
        {
            _avatarSlot.Clear();
            if (avatar != null) _avatarSlot.Add(avatar);
        }

        public void SetSeasonGoals(string text) => _seasonGoals.text = text;

        /// <summary>Hides the condition section for non-owned players (you don't know an opponent's form/morale exactly).</summary>
        public void SetConditionVisible(bool visible) =>
            _conditionSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetCondition(ProfileConditionVm vm)
        {
            _conditionBlock.Clear();

            var strip = new VisualElement();
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.alignItems = Align.Center;
            strip.style.height = 28;
            ConditionStrip.Append(strip, vm.FormArrow, vm.MoraleFace, vm.Fitness);
            _conditionBlock.Add(strip);

            _conditionBlock.Add(CauseLine(vm.FitnessLine));
            _conditionBlock.Add(CauseLine(vm.FormLine));
            _conditionBlock.Add(CauseLine(vm.MoraleLine));
        }

        public void SetAttributes(IReadOnlyList<AttrRowVm> rows)
        {
            _attrList.Clear();
            foreach (AttrRowVm vm in rows)
                _attrList.Add(AttributeRow(vm.Name, vm.Text, vm.BarValue));
        }

        private static Label CauseLine(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.color = new Color(1f, 1f, 1f, 0.85f);
            label.style.fontSize = 13;
            label.style.marginTop = 2;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Label SectionLabel(string caption)
        {
            var label = new Label(caption);
            label.style.color = SectionColor;
            label.style.fontSize = 13;
            label.style.marginBottom = 4;
            label.style.marginTop = 2;
            return label;
        }

        /// <summary>A row: attribute name on the left, a value/range text, then a 1-100 bar.</summary>
        private static VisualElement AttributeRow(string name, string text, int barValue)
        {
            int clamped = barValue < 0 ? 0 : (barValue > 100 ? 100 : barValue);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 26;
            row.style.marginBottom = 2;

            var label = new Label(name);
            label.style.width = 110;
            label.style.fontSize = 13;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(label);

            var number = new Label(text ?? clamped.ToString());
            number.style.width = 56;
            number.style.fontSize = 13;
            number.style.unityTextAlign = TextAnchor.MiddleRight;
            number.style.marginRight = 8;
            row.Add(number);

            var track = new VisualElement();
            track.style.flexGrow = 1f;
            track.style.height = 8;
            track.style.backgroundColor = BarTrackColor;
            track.style.borderTopLeftRadius = 3;
            track.style.borderTopRightRadius = 3;
            track.style.borderBottomLeftRadius = 3;
            track.style.borderBottomRightRadius = 3;

            var fill = new VisualElement();
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(clamped);
            fill.style.backgroundColor = AttrBarColor;
            fill.style.borderTopLeftRadius = 3;
            fill.style.borderBottomLeftRadius = 3;
            track.Add(fill);
            row.Add(track);

            return row;
        }
    }
}
