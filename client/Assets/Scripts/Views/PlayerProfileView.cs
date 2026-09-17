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
    /// Player profile (task 4.6), redrawn in task 14.4 on the standard page.
    /// Desktop: an identity card on the left (portrait, role · age · overall, potential, where he
    /// plays, the observation action, season goals, and — for your own players — the condition with
    /// its three "why" lines), the ten attributes on the right as big bars coloured by level.
    /// Phone: the same two cards stacked. Dumb view — every value and string comes from the presenter.
    /// </summary>
    public sealed class PlayerProfileView
    {
        public event Action BackClicked;

        /// <summary>"Put him under observation" / "call the scout off" (task 11.3).</summary>
        public event Action WatchClicked;

        public VisualElement Root { get; }

        private readonly Label _name;
        private readonly Label _subline;
        private readonly Label _potential;
        private readonly Label _reputation;
        private readonly Button _watchButton;
        private readonly Label _seasonGoals;
        private readonly VisualElement _avatarSlot;
        private readonly VisualElement _conditionSection;
        private readonly VisualElement _conditionBlock;
        private readonly VisualElement _attrList;
        private readonly VisualElement _grid;
        private readonly VisualElement _colId;
        private readonly VisualElement _colAttr;

        public PlayerProfileView(Func<string, string> tr)
        {
            PageParts page = UiKit.StandardPage(tr("profile.kicker"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke(), UiKit.WidthMedium);
            Root = page.Root;
            _name = page.Title;

            _grid = new VisualElement();
            _grid.AddToClassList("fts-profile__grid");
            page.Column.Add(_grid);

            // ---- identity
            _colId = UiKit.RaisedCard();
            _colId.AddToClassList("fts-profile__id");
            _grid.Add(_colId);

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            _avatarSlot = new VisualElement();
            _avatarSlot.AddToClassList("fts-profile__avatar");
            _avatarSlot.style.flexShrink = 0f;
            top.Add(_avatarSlot);
            var idText = new VisualElement();
            idText.style.flexShrink = 1f;
            idText.style.minWidth = 0f;
            _subline = new Label(string.Empty);
            _subline.AddToClassList("fts-profile__subline");
            _subline.style.whiteSpace = WhiteSpace.Normal;
            idText.Add(_subline);
            _potential = new Label(string.Empty);
            _potential.AddToClassList("fts-profile__potential");
            _potential.style.whiteSpace = WhiteSpace.Normal;
            idText.Add(_potential);
            top.Add(idText);
            _colId.Add(top);

            _reputation = UiKit.HelpText(string.Empty);
            _reputation.AddToClassList("fts-profile__reputation");
            _reputation.style.display = DisplayStyle.None;
            _colId.Add(_reputation);

            _watchButton = UiKit.GhostButton(string.Empty, () => WatchClicked?.Invoke());
            _watchButton.AddToClassList("fts-profile__watch");
            _watchButton.style.display = DisplayStyle.None;
            _colId.Add(_watchButton);

            _seasonGoals = new Label(string.Empty);
            _seasonGoals.AddToClassList("fts-profile__goals");
            _colId.Add(_seasonGoals);

            _conditionSection = new VisualElement();
            _conditionSection.AddToClassList("fts-profile__condition");
            _conditionSection.Add(UiKit.BlockHead(tr("profile.condition_caption")));
            _conditionBlock = new VisualElement();
            _conditionSection.Add(_conditionBlock);
            _colId.Add(_conditionSection);

            // ---- attributes
            _colAttr = UiKit.OptionCard();
            _colAttr.AddToClassList("fts-profile__attrs");
            _colAttr.Add(UiKit.BlockHead(tr("profile.attributes_caption")));
            _attrList = new VisualElement();
            _colAttr.Add(_attrList);
            _grid.Add(_colAttr);

            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Responsive.Changed += Layout;
                Layout(Responsive.Current);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Responsive.Changed -= Layout);
            Layout(Responsive.Current);
        }

        public void SetIdentity(string name, string subline, string potential)
        {
            _name.text = name ?? string.Empty;
            _subline.text = subline ?? string.Empty;
            _potential.text = potential ?? string.Empty;
        }

        /// <summary>Sets the player's portrait placeholder (a monogram avatar built by the presenter).</summary>
        public void SetAvatar(VisualElement avatar)
        {
            _avatarSlot.Clear();
            if (avatar != null) _avatarSlot.Add(avatar);
        }

        public void SetSeasonGoals(string text) => _seasonGoals.text = text ?? string.Empty;

        /// <summary>The club / division / fame line; empty hides it.</summary>
        public void SetReputation(string text)
        {
            _reputation.text = text ?? string.Empty;
            _reputation.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>The observation button: hidden for your own players, lit when a scout is already on him.</summary>
        public void SetWatchAction(string text, bool visible, bool enabled, bool on)
        {
            _watchButton.text = text ?? string.Empty;
            _watchButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _watchButton.SetEnabled(enabled);
            _watchButton.EnableInClassList("fts-profile__watch--on", on);
        }

        /// <summary>Hides the condition section for non-owned players (you don't know an opponent's form/morale exactly).</summary>
        public void SetConditionVisible(bool visible) =>
            _conditionSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetCondition(ProfileConditionVm vm)
        {
            _conditionBlock.Clear();

            var strip = new VisualElement();
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.alignItems = Align.Center;
            strip.AddToClassList("fts-profile__strip");
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

        private void Layout(Viewport viewport)
        {
            bool two = viewport != Viewport.Mobile;
            _grid.style.flexDirection = two ? FlexDirection.Row : FlexDirection.Column;
            _grid.style.alignItems = two ? Align.FlexStart : Align.Stretch;
            _colId.style.flexGrow = two ? 2f : 0f;
            _colId.style.flexBasis = two ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
            _colAttr.style.flexGrow = two ? 3f : 0f;
            _colAttr.style.flexBasis = two ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
            _colAttr.style.marginLeft = two ? UiKit.SpaceMd : 0f;
            _colAttr.style.marginTop = two ? 0f : UiKit.SpaceMd;
        }

        private static Label CauseLine(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("fts-profile__cause");
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        /// <summary>A row: attribute name, value/range, and a bar tinted by level.</summary>
        private static VisualElement AttributeRow(string name, string text, int barValue)
        {
            int clamped = barValue < 0 ? 0 : (barValue > 100 ? 100 : barValue);

            var row = new VisualElement();
            row.AddToClassList("fts-profile__attr");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var label = new Label(name);
            label.AddToClassList("fts-profile__attrname");
            label.style.flexShrink = 0f;
            row.Add(label);

            var track = new VisualElement();
            track.AddToClassList("fts-profile__track");
            track.style.flexGrow = 1f;
            track.style.overflow = Overflow.Hidden;
            var fill = new VisualElement();
            fill.AddToClassList("fts-profile__fill");
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(clamped);
            fill.style.backgroundColor = clamped >= 75 ? UiKit.Accent : clamped >= 55 ? UiKit.Hex(0x3E86CC) : clamped >= 35 ? UiKit.Warning : UiKit.Danger;
            track.Add(fill);
            row.Add(track);

            var number = new Label(text ?? clamped.ToString());
            number.AddToClassList("fts-profile__attrvalue");
            number.style.unityTextAlign = TextAnchor.MiddleRight;
            number.style.flexShrink = 0f;
            row.Add(number);

            return row;
        }
    }
}
