using System.Collections.Generic;
using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Keeps the match you WATCH and the match the result model played the same match
    /// (task 13.2) — without scripting a single touch.
    ///
    /// The simulation plays freely: it passes, presses, wins the ball, puts it out, takes
    /// throw-ins. What it may not do is invent a goal, because the score belongs to the 1.4
    /// result model. So the director opens a WINDOW a few minutes before each chance on that
    /// timeline: inside it the attacking side works the ball toward the man who is going to
    /// shoot, and at his minute he strikes it, with the outcome the timeline already decided.
    /// Outside a window nobody shoots — the teams just play.
    ///
    /// It is a nudge, not a puppet string: where the players are, who wins the tackle and how
    /// the ball travels are all the simulation's.
    /// </summary>
    internal sealed class MatchDirector
    {
        internal struct Chance
        {
            public int Tick;
            public bool Home;
            public int Slot;
            public MatchEventType Outcome;
            public int Grace;
        }

        private readonly List<Chance> _chances = new List<Chance>();
        private readonly int _windowTicks;
        private readonly int _lastTick;
        private readonly int _spacingTicks;

        /// <summary>The most a chance can have been held back from its minute.</summary>
        public int SpacingTicks => _spacingTicks;
        private int _index;

        public MatchDirector(MatchReport report, Lineup home, Lineup away, MatchBalance cfg, int lastTick)
        {
            _lastTick = lastTick;
            _windowTicks = cfg.ChanceWindowTicks;

            // A chance belongs to a MINUTE, not to a tick. Firing it on the very first tick of
            // that minute means firing it wherever the ball happens to be; a few ticks of grace
            // let the move arrive, and the clock still reads the same minute.

            // A strike takes a few ticks to be settled and a goal is celebrated after it. Two
            // chances closer together than that would have the second one struck while the
            // first is still in the air — the ball would be in the net, and the frame carrying
            // the first goal would show it somewhere else entirely. So the schedule is spaced
            // ONCE, here, deterministically: a chance that lands too close to the one before it
            // is held back to the first tick on which it can honestly be played.
            // Two chances cannot be settled at once: a strike still in the air when the next is
            // taken is a goal the viewer never sees, because the ball is snapped to the net and
            // struck from there in the same frame. So the schedule is worked out ONCE, here.
            //
            // The separation kept is only what a strike needs to be settled — plus, after a
            // goal, the celebration. Pushing chances later drags the picture away from the
            // clock, so instead the GRACE each chance is allowed (the ticks it may wait for the
            // move to arrive before it is forced) is cut to whatever is left before the next
            // one. A chance in a busy minute is struck promptly; a chance with room takes it.
            int settle = cfg.ShotResolveTicks + 1;
            int earliest = 0;

            foreach (MatchEvent e in report.Events)
            {
                int tick = e.Minute * cfg.TicksPerMinute;
                if (tick < earliest) tick = earliest;
                if (tick > lastTick) tick = lastTick;
                earliest = tick + settle +
                    (e.Type == MatchEventType.Goal ? cfg.GoalCelebrationTicks + cfg.DeadBallTicks : 0);

                bool isHome = e.ClubId == report.HomeClubId;
                _chances.Add(new Chance
                {
                    Tick = tick,
                    Home = isHome,
                    Slot = SlotIndexOf(isHome ? home : away, e.PlayerId),
                    Outcome = e.Type,
                    Grace = cfg.ChanceGraceTicks
                });
            }

            for (int i = 0; i < _chances.Count; i++)
            {
                Chance c = _chances[i];
                int room = i + 1 < _chances.Count
                    ? _chances[i + 1].Tick - c.Tick - settle
                    : cfg.ChanceGraceTicks;
                if (room < 0) room = 0;
                if (room < c.Grace) c.Grace = room;
                if (c.Tick + c.Grace > lastTick) c.Grace = lastTick - c.Tick;
                if (c.Grace < 0) c.Grace = 0;
                _chances[i] = c;
            }

            _spacingTicks = cfg.ShotResolveTicks + 1 + cfg.GoalCelebrationTicks + cfg.DeadBallTicks;
        }

        /// <summary>Drops chances whose moment, grace included, has gone by.</summary>
        public void Advance(int tick)
        {
            while (_index < _chances.Count && _chances[_index].Tick + _chances[_index].Grace < tick) _index++;
        }

        /// <summary>The chance just struck is spent: the next one becomes the one to build to.</summary>
        public void MarkTaken()
        {
            if (_index < _chances.Count) _index++;
        }

        /// <summary>The chance being built toward right now, if a window is open.</summary>
        public bool TryCurrent(int tick, out Chance chance)
        {
            chance = default;
            if (_index >= _chances.Count) return false;

            Chance next = _chances[_index];
            if (next.Tick - tick > _windowTicks) return false;

            chance = next;
            return true;
        }

        /// <summary>The man this side should be working the ball to, or -1.</summary>
        public int PriorityTarget(int tick, bool home)
        {
            return TryCurrent(tick, out Chance c) && c.Home == home ? c.Slot : -1;
        }

        /// <summary>Ticks left before the strike is due, or -1 when no window is open here.</summary>
        public int TicksToChance(int tick, bool home) =>
            TryCurrent(tick, out Chance c) && c.Home == home ? c.Tick - tick : -1;

        /// <summary>True once his minute has come and while the grace lasts.</summary>
        public bool ShotDue(int tick, out Chance chance) =>
            TryCurrent(tick, out chance) && tick >= chance.Tick;

        /// <summary>The last tick on which the strike can still be taken. Now or never.</summary>
        public bool ShotDeadline(int tick) =>
            TryCurrent(tick, out Chance c) &&
            (tick >= c.Tick + c.Grace || tick >= _lastTick);

        private static int SlotIndexOf(Lineup lineup, int playerId)
        {
            for (int i = 0; i < lineup.Slots.Count; i++)
                if (lineup.Slots[i].Player.Id == playerId) return i;
            return 0;
        }
    }
}
