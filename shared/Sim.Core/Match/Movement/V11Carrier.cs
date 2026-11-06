namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>
        /// The man (side * n + slot) the V11 brain last sent running with the ball, -1 for nobody.
        /// </summary>
        private int _v11Carrier = -1;

        /// <summary>
        /// The man (side * n + slot) with a choice to make and his ball ahead of him out of reach, -1
        /// for nobody: he goes and gets it at full pace, uncapped (free-ball spec R3).
        /// </summary>
        private int _closeIn = -1;

        /// <summary>
        /// A man running with the ball is slower than one running without it (V11 only): he is
        /// capped at <see cref="Config.MatchBalance.V11CarrierSpeedPercent"/> of his top speed while
        /// the ball is his — at his feet, or knocked ahead and not yet touched by anybody else.
        /// At full speed a carrier ran through a flat back line that could never turn and catch
        /// him, and walked in on the keeper a hundred times a match.
        /// </summary>
        private int CarrierTop(int k, int top)
        {
            if (k == _closeIn || _ball.Dead || _ball.LastTouchSide < 0) return top;
            int toucher = _ball.Free
                ? _ball.LastTouchSide * _n + _ball.LastTouchSlot
                : _ball.OwnerSide * _n + _ball.OwnerSlot;
            if (toucher != k) return top;

            int percent = k == _v11Carrier ? _cfg.V11CarrierSpeedPercent : 100;
            if (CarrierBlocked(k) && percent > _cfg.V11CarrierBlockedSpeedPercent) percent = _cfg.V11CarrierBlockedSpeedPercent;
            int capped = top * percent / 100;
            return capped < 1 ? 1 : capped;
        }

        /// <summary>Issue #206: an outfielder goal-side of this man, close enough to stop his run (see V11CarrierBlockedRangeDm).</summary>
        private bool CarrierBlocked(int k)
        {
            int side = k / _n;
            if (_keeper[k]) return false;
            int gx = U.Units(MovementGeometry.AttackedGoalX(side == 0)) - _px[k], gy = U.CenterYU - _py[k];
            int toGoal = U.Length(gx, gy);
            if (toGoal <= 0 || toGoal > U.Units(_cfg.V11CarrierBlockedRangeDm)) return false;
            int halfWidth = U.Units(_cfg.V11CarrierBlockedHalfWidthDm);
            if (gy > halfWidth || -gy > halfWidth) return false;

            int reach = U.Units(_cfg.V11CarrierBlockedDm);
            int opponent = 1 - side;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (_sentOff[ok] || _keeper[ok]) continue;
                int dx = _px[ok] - _px[k], dy = _py[ok] - _py[k];
                int gap = U.Length(dx, dy);
                if (gap > reach) continue;
                if (((long)dx * gx + (long)dy * gy) * 1000 >= (long)_cfg.V11CarrierBlockedConePermille * gap * toGoal) return true;
            }

            return false;
        }
    }
}
