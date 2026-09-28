namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>What the base brain has a man doing this tick, when it is anything but holding his place.</summary>
        private enum BaseJob
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

        private sealed partial class BaseBrain
        {
            /// <summary>
            /// The job this man has, for the V11 brain, which positions a man with none (or on a
            /// supporting run) itself and hands every other one to <see cref="Move"/>. Nothing is
            /// drawn; the one write, the wall's, is the one <see cref="Move"/> makes itself and is
            /// idempotent.
            /// </summary>
            public BaseJob JobOf(int tick, int side, int slot)
            {
                int k = side * _n + slot;
                if (_sentOff[k]) return BaseJob.SentOff;
                if (_ball.Dead && _ctx.DeadSide == side && _ctx.DeadTaker == slot) return BaseJob.Restart;
                if (_ball.Dead && _ctx.DeadKind == BallActionKind.Kickoff) return BaseJob.Restart;
                if (_freeKickWall.RetreatSpot(side, slot, out _, out _)) return BaseJob.Restart;
                if (_ball.OwnerSide == side && _ball.OwnerSlot == slot) return BaseJob.Ball;
                if (_keeper[k]) return BaseJob.Keeper;
                if (_ball.Free && !_ball.Dead && (_receiver[side] == slot || _chaser[side] == slot)) return BaseJob.Chase;
                if (!_attacking[side] && Pressing(tick, side, slot, out _, out _)) return BaseJob.Press;
                if (_attacking[side] && AlreadySupporting(side, slot)) return BaseJob.Support;
                if (!_attacking[side] && _duty[k] == DutyCover) return BaseJob.Cover;
                if (!_attacking[side] && _duty[k] == DutyMark && _mark[k] >= 0) return BaseJob.Mark;
                return BaseJob.None;
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
