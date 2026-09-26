using System.Collections.Generic;
using Fts.Services.Localization;
using Fts.Views;
using Sim.Core.Match;

namespace Fts.Presenters
{
    /// <summary>
    /// The shout row of the in-match panel (spec R11, issue #44), shared by the watched match and
    /// the two live screens: it paints the five buttons from the bench's <see cref="ShoutBoard"/>
    /// and remembers the one the coach picked until the presenter sends it with Apply — as part of
    /// the same input as a substitution or an instruction change.
    /// </summary>
    public sealed class ShoutPicker
    {
        private readonly InMatchPanel _panel;
        private readonly ILocalizationService _loc;
        private ShoutBoard _board;

        public ShoutPicker(InMatchPanel panel, ILocalizationService loc)
        {
            _panel = panel;
            _loc = loc;
        }

        /// <summary>The shout to send with the next Apply, or None.</summary>
        public TouchlineShout Pending { get; private set; }

        /// <summary>Paints the row for <paramref name="board"/>; a pick the bench can no longer make is dropped.</summary>
        public void Show(ShoutBoard board)
        {
            _board = board;
            if (!board.CanShout) Pending = TouchlineShout.None;
            Paint();
        }

        /// <summary>Picks the shout at <paramref name="index"/>, or un-picks it when it is already picked.</summary>
        public void Toggle(int index)
        {
            if (!_board.CanShout || index < 0 || index >= ShoutBoard.Choices.Count) return;

            TouchlineShout shout = ShoutBoard.Choices[index];
            Pending = Pending == shout ? TouchlineShout.None : shout;
            Paint();
        }

        public void Clear() => Pending = TouchlineShout.None;

        /// <summary>The loc key of a shout's short name (buttons, plan rules).</summary>
        public static string NameKey(TouchlineShout shout)
        {
            switch (shout)
            {
                case TouchlineShout.PressHigh: return "match.shout.press_high";
                case TouchlineShout.KeepBall: return "match.shout.keep_ball";
                case TouchlineShout.AllForward: return "match.shout.all_forward";
                case TouchlineShout.Encourage: return "match.shout.encourage";
                default: return "match.shout.concentrate";
            }
        }

        private void Paint()
        {
            var labels = new List<string>(ShoutBoard.Choices.Count);
            int picked = -1;
            for (int i = 0; i < ShoutBoard.Choices.Count; i++)
            {
                labels.Add(_loc.Tr(NameKey(ShoutBoard.Choices[i])));
                if (ShoutBoard.Choices[i] == Pending) picked = i;
            }

            _panel.SetShouts(labels, picked, _board.CanShout);
            _panel.SetShoutStatus(StatusText());
        }

        private string StatusText()
        {
            if (_board.Active != TouchlineShout.None)
                return _loc.Tr("inmatch.shout.active", _loc.Tr(NameKey(_board.Active)),
                    _board.EffectMinutesLeft, _board.CooldownMinutesLeft);
            if (!_board.CanShout)
                return _loc.Tr("inmatch.shout.cooldown", _board.CooldownMinutesLeft);
            return _loc.Tr(Pending == TouchlineShout.None ? "inmatch.shout.ready" : "inmatch.shout.pending");
        }
    }
}
