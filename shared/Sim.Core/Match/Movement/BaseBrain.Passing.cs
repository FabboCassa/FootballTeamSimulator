namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class BaseBrain
        {
            /// <summary>
            /// Can this ball be cut out — and by how much? For each opponent, where he would meet
            /// the line of the pass, and how far past that point the ball has already rolled by the
            /// time he gets there. Opponents behind the passer are ignored: they are chasing it, not
            /// intercepting it. The answer is odds rather than a yes or a no, because a lane a man
            /// reaches half a second late is not the same pass as one nobody is near.
            /// </summary>
            private int LaneCompletion(int side, int fromX, int fromY, int toX, int toY, int force)
            {
                int opponent = 1 - side;
                int dx = toX - fromX, dy = toY - fromY;
                int length = U.Length(dx, dy);
                if (length <= 0) return 0;

                int receiverSpace = U.Units(_cfg.ReceiverSpaceDm);
                int worst = 1000;
                for (int j = 0; j < _n; j++)
                {
                    int ok = opponent * _n + j;
                    if (_sentOff[ok]) continue;
                    long along = ((long)(_px[ok] - fromX) * dx + (long)(_py[ok] - fromY) * dy) / length;
                    if (along <= 0) continue;                       // behind the ball: he is chasing, not cutting it out

                    // The last stretch belongs to the receiver — a man marked at three metres can
                    // still be passed to, he shields it, and his marker has to TACKLE him for it.
                    if (along > length - receiverSpace) continue;

                    int meetX = fromX + (int)((long)dx * along / length);
                    int meetY = fromY + (int)((long)dy * along / length);

                    int reach = U.Distance(_px[ok], _py[ok], meetX, meetY) - _interceptU;
                    if (reach < 0) return _cfg.CutOutCompletionPermille;

                    // Asked the other way round — how far has the ball got by the time he is there —
                    // this is one lookup in the ball's roll table instead of a search for the tick on
                    // which it arrives. Same question, and it is asked for every opponent on every
                    // candidate pass, which at 10 Hz is where the passing model's cost lives.
                    int manTicks = reach / _maxSpeed[ok] + _cfg.PassReactionTicks;
                    int slack = BallSkill.LaneCompletionPermille(
                        (_ball.RangeInTicks(force, manTicks) - (int)along) / U.Scale, _cfg);
                    if (slack < worst) worst = slack;
                }

                return worst;
            }

            /// <summary>Yes or no, for the support grid, which only wants to know if a lane is open.</summary>
            private bool PassSafe(int side, int fromX, int fromY, int toX, int toY, int force)
                => LaneCompletion(side, fromX, fromY, toX, toY, force) >= _cfg.ClearLaneCompletionPermille;
        }
    }
}
