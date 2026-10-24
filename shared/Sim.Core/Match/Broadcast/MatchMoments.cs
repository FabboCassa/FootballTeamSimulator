using System;
using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>What a toast announces.</summary>
    public enum MomentKind
    {
        Goal = 0,
        Saved = 1,
        Missed = 2,
        Shout = 3,
        YellowCard = 4,
        RedCard = 5,
        Penalty = 6,
        Substitution = 7
    }

    /// <summary>
    /// One key moment of a watched match, on the stream frame it happens on and stamped with the
    /// minute the clock shows there. <see cref="Event"/> is the timeline entry behind a chance or a
    /// shout and null for what only the stream records (a card, a penalty, a substitution).
    /// </summary>
    public readonly struct MatchMoment
    {
        public MatchMoment(int frame, int minute, MomentKind kind, bool home, int playerId, MatchEvent? evt, SlotChange change)
        {
            Frame = frame;
            Minute = minute;
            Kind = kind;
            Home = home;
            PlayerId = playerId;
            Event = evt;
            Change = change;
        }

        public int Frame { get; }
        public int Minute { get; }
        public MomentKind Kind { get; }
        public bool Home { get; }

        /// <summary>The shooter, the man booked, the penalty taker or the substitute coming on; 0 when nobody.</summary>
        public int PlayerId { get; }

        public MatchEvent? Event { get; }

        /// <summary>The slot change of a <see cref="MomentKind.Substitution"/>; default otherwise.</summary>
        public SlotChange Change { get; }
    }

    /// <summary>
    /// The toast schedule of a match (spec believable-match R10): every timeline entry placed on the
    /// frame of the ball action it resolved on — the goal, the save, the miss or the block — rather
    /// than at the end of its minute, plus the cards, penalties and substitutions the stream records.
    /// An entry with no action of its own (a shout, a report from the fast model) falls back to the
    /// first frame of its recorded minute. Pure, built once per report.
    /// </summary>
    public static class MatchMoments
    {
        public static MatchMoment[] Of(MatchReport? report)
        {
            if (report == null) return Array.Empty<MatchMoment>();

            PositionStream? s = report.Positions;
            int fpm = s != null && s.TicksPerMinute > 0 ? s.TicksPerMinute : 0;
            List<BallAction> actions = fpm > 0 && s!.Actions != null ? s.Actions : new List<BallAction>();
            var moments = new List<MatchMoment>(report.Events.Count + 8);
            var used = new bool[actions.Count];

            foreach (MatchEvent e in report.Events)
            {
                bool home = e.ClubId == report.HomeClubId;
                MomentKind kind = KindOf(e.Type);
                int at = kind == MomentKind.Shout ? -1 : OutcomeOf(actions, used, e.Minute, fpm, kind, home);
                if (at >= 0)
                {
                    used[at] = true;
                    int frame = actions[at].Tick;
                    moments.Add(new MatchMoment(frame, MatchClock.MinuteOf(frame, fpm), kind, home, e.PlayerId, e, default));
                }
                else
                {
                    moments.Add(new MatchMoment(MatchClock.FirstFrameOf(e.Minute, fpm), e.Minute, kind, home, e.PlayerId, e, default));
                }
            }

            foreach (BallAction a in actions)
            {
                MomentKind kind;
                switch (a.Kind)
                {
                    case BallActionKind.YellowCard: kind = MomentKind.YellowCard; break;
                    case BallActionKind.RedCard: kind = MomentKind.RedCard; break;
                    case BallActionKind.Penalty: kind = MomentKind.Penalty; break;
                    default: continue;
                }

                moments.Add(new MatchMoment(a.Tick, MatchClock.MinuteOf(a.Tick, fpm), kind, a.Home,
                    OccupantAt(s!, a.Home, a.Slot, a.Tick), null, default));
            }

            if (fpm > 0 && s!.Changes != null)
                foreach (SlotChange c in s.Changes)
                    moments.Add(new MatchMoment(c.Frame, MatchClock.MinuteOf(c.Frame, fpm), MomentKind.Substitution,
                        c.Home, c.OnPlayerId, null, c));

            return InPlaybackOrder(moments);
        }

        private static MomentKind KindOf(MatchEventType type)
        {
            switch (type)
            {
                case MatchEventType.Goal: return MomentKind.Goal;
                case MatchEventType.ChanceSaved: return MomentKind.Saved;
                case MatchEventType.Shout: return MomentKind.Shout;
                default: return MomentKind.Missed;
            }
        }

        /// <summary>
        /// The first unclaimed outcome of this chance in its minute. The upper bound is inclusive
        /// because an outcome is filed on the first frame at or after its tick, which for the last
        /// tick of a minute is the first frame of the next one; minute 90 also holds stoppage time.
        /// </summary>
        private static int OutcomeOf(List<BallAction> actions, bool[] used, int minute, int fpm, MomentKind kind, bool home)
        {
            int from = MatchClock.FirstFrameOf(minute, fpm);
            int to = minute >= BroadcastPlayback.FullTimeMinute ? int.MaxValue : from + fpm;
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction a = actions[i];
                if (a.Tick > to) break;
                if (used[i] || a.Tick < from) continue;
                if (Resolves(kind, home, a)) return i;
            }

            return -1;
        }

        private static bool Resolves(MomentKind kind, bool home, BallAction a)
        {
            switch (kind)
            {
                case MomentKind.Goal: return a.Kind == BallActionKind.Goal && a.Home == home;
                case MomentKind.Saved: return a.Kind == BallActionKind.Save && a.Home != home;
                default:
                    return (a.Kind == BallActionKind.Miss && a.Home == home)
                        || (a.Kind == BallActionKind.Block && a.Home != home);
            }
        }

        /// <summary>
        /// Who held <paramref name="slot"/> at <paramref name="frame"/>: the stream's ids name the last
        /// man in a slot, so a later substitution's outgoing player is the one who was there.
        /// </summary>
        private static int OccupantAt(PositionStream s, bool home, int slot, int frame)
        {
            if (s.Changes != null)
                foreach (SlotChange c in s.Changes)
                    if (c.Home == home && c.Slot == slot && c.Frame > frame)
                        return c.OffPlayerId;

            int[] ids = home ? s.HomePlayerIds : s.AwayPlayerIds;
            return ids != null && slot >= 0 && slot < ids.Length ? ids[slot] : 0;
        }

        /// <summary>Sorted by frame; moments on the same frame keep the order they were added in.</summary>
        private static MatchMoment[] InPlaybackOrder(List<MatchMoment> moments)
        {
            var order = new int[moments.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => moments[a].Frame != moments[b].Frame
                ? moments[a].Frame.CompareTo(moments[b].Frame)
                : a.CompareTo(b));

            var sorted = new MatchMoment[moments.Count];
            for (int i = 0; i < order.Length; i++) sorted[i] = moments[order[i]];
            return sorted;
        }
    }
}
