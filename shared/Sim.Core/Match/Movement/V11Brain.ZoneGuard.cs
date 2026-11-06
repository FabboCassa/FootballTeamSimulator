namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V11Brain
        {
            // Issue #206, per side: the man closing the receiver of a pass into the central zone (-1
            // for none) and where he goes (units), read once a tick in UpdateTeams.
            private readonly int[] _zoneCloser = { -1, -1 };
            private readonly int[] _zoneX = new int[SideCount];
            private readonly int[] _zoneY = new int[SideCount];

            /// <summary>
            /// Defending the central zone (issue #206): with a pass on its way to an opponent who will
            /// meet it in the zone, the outfielder nearest that point is sent to close it from the
            /// goal side, so the receiver is pressed on arrival. One man: the others keep their places,
            /// so the centre-backs hold the zone rather than follow men out of it.
            /// </summary>
            private void UpdateZoneGuard()
            {
                MatchBall ball = _sim._ball;
                for (int side = 0; side < SideCount; side++)
                {
                    _zoneCloser[side] = -1;
                    if (ball.Dead) continue;

                    // A man already on the ball is the press's (its instructions included); this is the pass.
                    int foe = 1 - side;
                    if (!ball.Free || _sim._receiver[foe] < 0) continue;

                    // Where he meets it, which a man coming to the ball does short of its aim.
                    if (!_base.InterceptFor(foe, _sim._receiver[foe], out int tx, out int ty))
                    {
                        tx = _sim._receiveX[foe];
                        ty = _sim._receiveY[foe];
                    }

                    bool home = side == 0;
                    int threatX = U.Dm(tx), threatY = U.Dm(ty);
                    if (!_roleJobs.InGuardedZone(home, threatX, threatY)) continue;

                    int closer = _sim._ctx.NearestTo(side, tx, ty, false);
                    _roleJobs.ZoneClose(home, threatX, threatY, out int x, out int y);
                    _zoneCloser[side] = closer;
                    _zoneX[side] = U.ClampX(U.Units(x));
                    _zoneY[side] = _sim._ctx.Inside(U.Units(y));
                }
            }

            /// <summary>Whether this man is the one closing the central zone, and where to (units).</summary>
            private bool ZoneCloser(int side, int slot, out int x, out int y)
            {
                x = _zoneX[side];
                y = _zoneY[side];
                return _zoneCloser[side] == slot;
            }
        }
    }
}
