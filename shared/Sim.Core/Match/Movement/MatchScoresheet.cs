using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The scoresheet (engine phase 6), and the whole inversion seen from the outside: the
    /// picture no longer illustrates a report that was written before it, it WRITES the report
    /// as it goes. Which is also why "the picture and the result agree on the score" stopped
    /// being a check that could fail and became a tautology — there is only one score now.
    /// </summary>
    internal sealed class MatchScoresheet
    {
        private readonly MatchBalance _cfg;
        private readonly PositionStream _stream;
        private readonly MatchReport _report;
        private readonly int _streamStride;
        private readonly int _lastFrame;

        public MatchScoresheet(MatchBalance cfg, PositionStream stream, MatchReport report, int streamStride, int lastFrame)
        {
            _cfg = cfg;
            _stream = stream;
            _report = report;
            _streamStride = streamStride;
            _lastFrame = lastFrame;
        }

        /// <summary>The minute a tick falls in, as the timeline counts them: 1 to 90.</summary>
        private int MinuteOf(int tick)
        {
            int minute = tick / _cfg.TicksPerMinute + 1;
            return minute < 1 ? 1 : (minute > 90 ? 90 : minute);
        }

        public void AddEvent(int tick, int side, int slot, MatchEventType type)
        {
            int[] ids = side == 0 ? _stream.HomePlayerIds : _stream.AwayPlayerIds;
            _report.Events.Add(new MatchEvent
            {
                Minute = MinuteOf(tick),
                Type = type,
                ClubId = side == 0 ? _report.HomeClubId : _report.AwayClubId,
                PlayerId = slot >= 0 && slot < ids.Length ? ids[slot] : 0
            });
        }

        public void RecordGoal(int tick, int side, int scorer)
        {
            Record(tick, BallActionKind.Goal, side == 0, scorer, -1);
            if (side == 0) _report.HomeGoals++; else _report.AwayGoals++;
            AddEvent(tick, side, scorer, MatchEventType.Goal);
        }

        public void RecordSave(int tick, int keeperSide, int keeperSlot, int shooterSlot)
        {
            Record(tick, BallActionKind.Save, keeperSide == 0, keeperSlot, -1);
            AddEvent(tick, 1 - keeperSide, shooterSlot, MatchEventType.ChanceSaved);
        }

        public void RecordMiss(int tick, int side, int slot)
        {
            Record(tick, BallActionKind.Miss, side == 0, slot, -1);
            AddEvent(tick, side, slot, MatchEventType.ChanceMissed);
        }

        /// <summary>The strike, filed under the frame it left his foot in. See the note in TakeShot.</summary>
        public void RecordStruckAt(int tick, bool home, int slot)
        {
            int frame = tick / _streamStride;
            if (frame > _lastFrame) frame = _lastFrame;
            _stream.Actions.Add(new BallAction(frame, BallActionKind.Shot, home, slot, -1));
        }

        /// <summary>
        /// The frame a MINUTE BOUNDARY falls on. A boundary tick is a whole number of minutes, and
        /// a frame is a whole number of ticks, so this lands exactly on a minute of the stream —
        /// which is what makes a substitute's minutes whole numbers that add up to eleven men for
        /// ninety minutes (engine phase 7).
        /// </summary>
        public int MinuteFrame(int tick)
        {
            int frame = tick / _streamStride;
            if (frame < 0) frame = 0;
            if (frame > _lastFrame) frame = _lastFrame;
            return frame;
        }

        /// <summary>
        /// Files what just happened to the ball, at the FRAME it belongs to. The simulation runs
        /// at 10 Hz and the stream is written at 2 Hz (engine phase 1), so an action's tick has
        /// to be mapped into frame space here — otherwise every consumer of the stream (the
        /// renderer, the analyzer, the tests) would need two clocks and a conversion rule.
        ///
        /// Rounded UP, to the first frame at or AFTER the action, and that is not a detail: a
        /// frame is half a second, and a shot struck at thirty metres a second covers fifteen
        /// metres in one. Filed on the frame BEFORE, a goal is drawn with the ball still short of
        /// the line and a pass with the ball still at the passer's feet — the commentary would be
        /// announcing things the picture had not done yet. Rounding up is monotone, so the order
        /// the actions happened in is kept either way.
        /// </summary>
        public void Record(int tick, BallActionKind kind, bool home, int slot, int target)
        {
            int frame = (tick + _streamStride - 1) / _streamStride;
            if (frame > _lastFrame) frame = _lastFrame;
            _stream.Actions.Add(new BallAction(frame, kind, home, slot, target));
        }
    }
}
