using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class BaseBrain
        {
            // Whether each side is in the window right after losing the ball (V11's defence transition).
            private readonly bool[] _justLostIt = new bool[SideCount];

            /// <summary>The V11 brain says, once a tick, which side has just lost the ball.</summary>
            public void SetJustLostIt(int side, bool justLostIt) => _justLostIt[side] = justLostIt;

            private DefendingAxesBalance Axes => _cfg.DefendingAxes;

            private int PressTriggerShiftDm(int side) =>
                DefendingAxes.PressTriggerShiftDm(Axes, _tactics[side].Instructions, _justLostIt[side]);

            private int SecondPressShiftDm(int side) =>
                DefendingAxes.SecondPressShiftDm(Axes, _tactics[side].Instructions, _justLostIt[side]);

            private long PressReach(int side, long reach) =>
                DefendingAxes.PressReach(Axes, _tactics[side].Instructions, _justLostIt[side], reach);

            private long PressDirectionReach(int side, long reach, bool wide) =>
                DefendingAxes.PressDirectionReach(Axes, _tactics[side].Instructions, wide, reach);

            private int BackLinePushDm(int side) => DefendingAxes.BackLinePushDm(Axes, _tactics[side].Instructions);

            private int Markers(int side) => DefendingAxes.Markers(Axes, _tactics[side].Instructions, _cfg.MaxMarkers);

            private int MarkZoneDepthDm(int side) =>
                DefendingAxes.MarkZoneDepthDm(Axes, _tactics[side].Instructions, _cfg.MarkOwnThirdDepthDm);

            /// <summary>The point a marker keeps his man from: the goal, or part-way from it toward the ball.</summary>
            private void MarkAim(int side, int goalX, out int x, out int y)
            {
                int ballSide = DefendingAxes.MarkBallSidePercent(Axes, _tactics[side].Instructions);
                x = goalX + (_ball.X - goalX) * ballSide / 100;
                y = U.CenterYU + (_ball.Y - U.CenterYU) * ballSide / 100;
            }

            private int MarkDistanceDm(int side) =>
                DefendingAxes.MarkDistanceDm(Axes, _tactics[side].Instructions, _cfg.MarkDistanceDm);
        }
    }
}
