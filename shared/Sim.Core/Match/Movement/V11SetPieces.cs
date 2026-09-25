using Sim.Core.Config;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>Each side's build-up instruction: how its throw-ins and goal kicks are played on V11.</summary>
        private readonly Tempo[] _tempo = new Tempo[SideCount];

        /// <summary>How close to goal a free kick gets a wall. V10 keeps its shooting range.</summary>
        private int WallRangeDm => _cfg.Brain == MatchBrainVersion.V11 ? _cfg.SetPieceRangeDm : _cfg.MaxShootRangeDm;

        private void SetTempo(MatchTactics? tactics)
        {
            _tempo[0] = tactics != null ? tactics.Home.Tactic.Instructions.Tempo : Tempo.Normal;
            _tempo[1] = tactics != null ? tactics.Away.Tactic.Instructions.Tempo : Tempo.Normal;
        }

        /// <summary>
        /// The V11 set pieces (watchable-match spec, R6). Every dead ball V11 plays its own way is
        /// decided here; the rest is left to the brain it sits in.
        ///
        /// - A free kick within <see cref="MatchBalance.SetPieceRangeDm"/> faces a wall and is shot
        ///   or crossed by value (<see cref="SetPiecePlan.ChooseFreeKick"/>); a cross waits for its
        ///   men to take up near-post, far-post and edge-of-box spots level with the offside line.
        /// - A corner waits for the same three roles (<see cref="CornerRoles"/>) and is swung at
        ///   the near-post or the far-post man.
        /// - A penalty taker walks back to the start of his run-up and runs in: the strike comes
        ///   at the end of a run the stream can see.
        /// - A goal kick or a throw-in is played at once, short or long as Tempo says.
        ///
        /// A set piece is taken once its men have been in place for a moment, or when the wait
        /// runs out, so the picture shows the structure before the kick.
        /// </summary>
        private sealed partial class V11SetPieces
        {
            private readonly MatchSimulator _sim;
            private readonly MatchBalance _cfg;

            // Per match, bound at Begin.
            private MatchBall _ball = null!;
            private MatchContext _ctx = null!;
            private int _n;
            private int[] _px = System.Array.Empty<int>();
            private int[] _py = System.Array.Empty<int>();
            private CornerRole[] _roles = System.Array.Empty<CornerRole>();
            private int[] _skillA = System.Array.Empty<int>();
            private int[] _skillB = System.Array.Empty<int>();
            private bool[] _eligible = System.Array.Empty<bool>();
            private int[] _xs = System.Array.Empty<int>();
            private int[] _ys = System.Array.Empty<int>();

            // The plan for the dead ball in hand, keyed on its whistle.
            private int _planAt;
            private BallActionKind _planKind;
            private int _planSide;
            private FreeKickOption _freeKick;
            private bool _hasRoles;
            private bool _lowY;
            private int _placedAt;
            private int _runUpFor;

            public V11SetPieces(MatchSimulator sim)
            {
                _sim = sim;
                _cfg = sim._cfg;
            }

            public void Begin()
            {
                _ball = _sim._ball;
                _ctx = _sim._ctx;
                _n = _sim._n;
                _px = _sim._px;
                _py = _sim._py;
                _roles = new CornerRole[_n];
                _skillA = new int[_n];
                _skillB = new int[_n];
                _eligible = new bool[_n];
                _xs = new int[_n];
                _ys = new int[_n];
                _planAt = int.MinValue;
                _runUpFor = int.MinValue;
            }

            /// <summary>The taker's decision on a dead ball. True when V11 has dealt with this man this tick.</summary>
            public bool Act(int tick, int side, int slot)
            {
                if (!_ball.Dead || _ctx.DeadSide != side || _ctx.DeadTaker != slot) return false;
                Plan();

                switch (_ctx.DeadKind)
                {
                    case BallActionKind.Penalty:
                        // Not until he has walked back and started his run.
                        return _runUpFor != _ctx.DeadAt;
                    case BallActionKind.GoalKick:
                    case BallActionKind.ThrowIn:
                        if (!OnTheBall(tick, side, slot)) return true;
                        PlayRestart(tick, side, slot);
                        return true;
                    case BallActionKind.Corner:
                        if (!OnTheBall(tick, side, slot) || !Ready(tick, side)) return true;
                        _sim.BeginRestart(tick, side, slot);
                        Deliver(tick, side, slot, PostTarget());
                        return true;
                    case BallActionKind.FreeKick when _freeKick != FreeKickOption.None:
                        if (!OnTheBall(tick, side, slot) || !Ready(tick, side)) return true;
                        _sim.BeginRestart(tick, side, slot);
                        if (_freeKick == FreeKickOption.DirectShot)
                        {
                            if (_ctx.ShotLive) _sim.ForceResolveShot(tick);
                            _sim.TakeShot(tick, side, slot);
                        }
                        else
                        {
                            Deliver(tick, side, slot, PostTarget());
                        }

                        return true;
                    default:
                        return false;
                }
            }

            /// <summary>Where a man goes on a dead ball. True when V11 has moved him this tick.</summary>
            public bool Move(int tick, int side, int slot)
            {
                if (!_ball.Dead || _ctx.DeadSide != side) return false;
                Plan();
                int k = side * _n + slot;

                if (_ctx.DeadKind == BallActionKind.Penalty && slot == _ctx.DeadTaker)
                {
                    RunUp(tick, side, k);
                    return true;
                }

                if (!_hasRoles || _roles[slot] == CornerRole.None || slot == _ctx.DeadTaker) return false;
                RoleSpot(side, _roles[slot], out int x, out int y);
                RunTo(k, x, y);
                return true;
            }

            /// <summary>
            /// To his set-piece spot at full pace and stopping ON it: a mark like the wall's, but
            /// one a man runs to, because the box fills in seconds and a walk would not get there.
            /// </summary>
            private void RunTo(int k, int tx, int ty)
            {
                MatchSimulator s = _sim;
                s._stepFromX[k] = _px[k];
                s._stepFromY[k] = _py[k];

                int dx = tx - _px[k], dy = ty - _py[k];
                int gap = U.Length(dx, dy);
                int step = s._maxSpeed[k] < 1 ? 1 : s._maxSpeed[k];
                if (gap <= step)
                {
                    _px[k] = U.ClampX(tx);
                    _py[k] = U.ClampY(ty);
                }
                else
                {
                    _px[k] = U.ClampX(_px[k] + (int)((long)dx * step / gap));
                    _py[k] = U.ClampY(_py[k] + (int)((long)dy * step / gap));
                }

                s._vx[k] = 0;
                s._vy[k] = 0;
                s._stepToX[k] = _px[k];
                s._stepToY[k] = _py[k];
            }

            /// <summary>The whistle and the spot he is due to take it from: past the pause, and on the ball.</summary>
            private bool OnTheBall(int tick, int side, int slot)
            {
                int k = side * _n + slot;
                return tick >= _ctx.DeadAt && U.Distance(_px[k], _py[k], _ball.X, _ball.Y) <= _sim._kickU;
            }

            /// <summary>
            /// The penalty taker's run-up: to a mark behind the spot first, and only once he is on
            /// it and the referee has waited his pause does he run in — at a jog, which is what
            /// makes the run last long enough to be seen.
            /// </summary>
            private void RunUp(int tick, int side, int k)
            {
                if (_runUpFor == _ctx.DeadAt)
                {
                    _sim.WalkTo(k, _ball.X, _ball.Y);
                    return;
                }

                int dir = MovementGeometry.Direction(side == 0);
                int markX = U.ClampX(_ball.X - dir * U.Units(_cfg.PenaltyRunUpDm));
                int markY = _ctx.Inside(_ball.Y + U.Units(_cfg.PenaltyRunUpDm) / 4);
                _sim.WalkTo(k, markX, markY);

                if (tick >= _ctx.DeadAt && U.Distance(_px[k], _py[k], markX, markY) <= U.Units(5))
                    _runUpFor = _ctx.DeadAt;
            }
        }
    }
}
