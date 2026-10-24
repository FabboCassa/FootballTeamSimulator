using Sim.Core.Match;
using Sim.Core.Match.Broadcast;

namespace Fts.Presenters
{
    /// <summary>
    /// The loc key of a timeline entry. Every key takes the same arguments — minute, player, club —
    /// so the result and watch screens format any event the same way; a shout names no player.
    /// A substitution's toast also takes the man going off as its fourth argument.
    /// </summary>
    public static class MatchEventKeys
    {
        public static string For(MatchMoment m)
        {
            switch (m.Kind)
            {
                case MomentKind.YellowCard: return "match.event.yellowcard";
                case MomentKind.RedCard: return "match.event.redcard";
                case MomentKind.Penalty: return "match.event.penalty";
                case MomentKind.Substitution: return "match.event.substitution";
                default: return For(m.Event);
            }
        }

        public static string For(MatchEvent e)
        {
            switch (e.Type)
            {
                case MatchEventType.Goal: return "match.event.goal";
                case MatchEventType.ChanceSaved: return "match.event.saved";
                case MatchEventType.Shout: return ShoutKey(e.Shout);
                default: return "match.event.missed";
            }
        }

        private static string ShoutKey(TouchlineShout shout)
        {
            switch (shout)
            {
                case TouchlineShout.PressHigh: return "match.event.shout.press_high";
                case TouchlineShout.KeepBall: return "match.event.shout.keep_ball";
                case TouchlineShout.AllForward: return "match.event.shout.all_forward";
                case TouchlineShout.Encourage: return "match.event.shout.encourage";
                default: return "match.event.shout.concentrate";
            }
        }
    }
}
