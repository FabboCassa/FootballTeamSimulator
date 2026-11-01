namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V11Brain
        {
            /// <summary>
            /// Free-ball spec R4: the man a through ball is meant for (<see cref="V11PassTarget"/>)
            /// runs on to where it is going, behind the line, and takes it there in his stride,
            /// rather than cutting back to meet it earliest on its way, which would throw away the
            /// line it beat. The run is timed to the ball: no faster than gets him there as it
            /// arrives, so he does not sprint past the spot and turn back for it. He attacks the ball
            /// instead (the chase, where he meets it earliest) while it is contested on its way there
            /// (<see cref="Contested"/>), and once it has reached its aim or will die short of it.
            /// A lead pass he meets earliest, on his way: let run on too, it took a tenth of the low
            /// block's time on the ball in its own third, the space DefensiveDutyTests measures
            /// (issue #180). Nothing moves the ball: he has it only through a touch within reach,
            /// and a man first to it has it.
            /// </summary>
            private bool RunsOnto(int side, int slot)
            {
                MatchSimulator sim = _sim;
                if (sim._receiver[side] != slot || !sim._runOn[side]) return false;

                MatchBall ball = sim._ball;
                int ax = sim._receiveX[side], ay = sim._receiveY[side];
                int ticks = TicksToReach(ball, ax, ay, sim._cfg.TicksOfMs(sim._cfg.RunOnLookaheadMs));
                if (ticks <= 0 || Contested(1 - side, ax, ay)) return false;

                int k = side * sim._n + slot;
                int gap = U.Distance(sim._px[k], sim._py[k], ax, ay);
                sim.SteerVia(k, ax, ay, sprint: true, -1, -1, pace: (gap + ticks - 1) / ticks);
                return true;
            }

            /// <summary>True when the man of <paramref name="side"/> going for the ball meets it before it reaches (ax, ay).</summary>
            private bool Contested(int side, int ax, int ay)
            {
                int chaser = _base.ChaserOf(side);
                if (chaser < 0 || _sim._sentOff[side * _sim._n + chaser] || ChasesAPassOutOfReach(side, chaser)) return false;
                if (!_base.InterceptFor(side, chaser, out int ix, out int iy)) return false;

                MatchBall ball = _sim._ball;
                return (long)(ax - ix) * ball.Vx + (long)(ay - iy) * ball.Vy > 0;
            }

            /// <summary>Ticks until the rolling ball reaches the aim; 0 once it has, -1 if it stops short of it or is not there within the horizon.</summary>
            private static int TicksToReach(MatchBall ball, int ax, int ay, int horizon)
            {
                int x = ball.X, y = ball.Y, vx = ball.Vx, vy = ball.Vy;
                for (int ticks = 0; ticks <= horizon; ticks++)
                {
                    if (vx == 0 && vy == 0) return -1;
                    if ((long)(ax - x) * vx + (long)(ay - y) * vy <= 0) return ticks;
                    ball.StepPrediction(ref x, ref y, ref vx, ref vy);
                }

                return -1;
            }
        }
    }
}
