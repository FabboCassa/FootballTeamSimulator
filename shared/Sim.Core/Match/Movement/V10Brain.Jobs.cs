namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>What the V10 brain has a man doing this tick, when it is anything but holding his place.</summary>
        private enum V10Job
        {
            None = 0,
            SentOff,
            Restart,
            Ball,
            Keeper,
            Chase,
            Press,
            Support,
            Cover,
            Mark
        }

        private sealed partial class V10Brain
        {
            /// <summary>
            /// The branch <see cref="Move"/> would take for this man, for the V11 brain, which
            /// positions a man with no job itself and hands every other one back to <see cref="Move"/>.
            /// The same tests in the same order, and nothing is drawn: V10 never calls it. The one
            /// write, the wall's, is the one <see cref="Move"/> makes itself and is idempotent.
            /// </summary>
            public V10Job JobOf(int tick, int side, int slot)
            {
                int k = side * _n + slot;
                if (_sentOff[k]) return V10Job.SentOff;
                if (_ball.Dead && _ctx.DeadSide == side && _ctx.DeadTaker == slot) return V10Job.Restart;
                if (_ball.Dead && _ctx.DeadKind == BallActionKind.Kickoff) return V10Job.Restart;
                if (_freeKickWall.RetreatSpot(side, slot, out _, out _)) return V10Job.Restart;
                if (_ball.OwnerSide == side && _ball.OwnerSlot == slot) return V10Job.Ball;
                if (_keeper[k]) return V10Job.Keeper;
                if (_ball.Free && !_ball.Dead && (_receiver[side] == slot || _chaser[side] == slot)) return V10Job.Chase;
                if (!_attacking[side] && Pressing(tick, side, slot, out _, out _)) return V10Job.Press;
                if (_attacking[side] && AlreadySupporting(side, slot)) return V10Job.Support;
                if (!_attacking[side] && _duty[k] == DutyCover) return V10Job.Cover;
                if (!_attacking[side] && _duty[k] == DutyMark && _mark[k] >= 0) return V10Job.Mark;
                return V10Job.None;
            }

            /// <summary>Where this side's supporting run is going, for the V11 brain.</summary>
            public void SupportSpot(int side, out int x, out int y)
            {
                x = _supportX[side];
                y = _supportY[side];
            }
        }
    }
}
