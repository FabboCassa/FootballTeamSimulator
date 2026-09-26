using Sim.Core.Match.Movement.Models;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V11Brain
        {
            private readonly V11ActionValuation _valuation;
            private V11Scene _scene = new V11Scene();

            /// <summary>Scene mate index to lineup slot, for the decision being made.</summary>
            private int[] _mateSlot = System.Array.Empty<int>();

            // R4: the man (side * n + slot) who has an open goal in front of him, and since when.
            private int _openCarrier = -1;
            private int _openSince;

            /// <summary>
            /// What this man does with the ball (R3-R5). A dead ball is v10's restart. On the ball he
            /// reads the scene and <see cref="V11ActionValuation"/> chooses: while the tempo's hold
            /// runs he keeps it — unless the goal is open, which he goes at on the tick he sees it
            /// (R4) — and then he takes the dearest of pass, cross, carry, clearance and shot. The
            /// execution, and every draw in it, is the simulator's, the same as v10's.
            /// </summary>
            public void Act(int tick, int side, int slot)
            {
                int k = side * _sim._n + slot;
                if (_sim._sentOff[k]) return;

                MatchBall ball = _sim._ball;
                if (ball.Dead)
                {
                    _v10.Act(tick, side, slot);
                    return;
                }

                if (ball.OwnerSide != side || ball.OwnerSlot != slot) return;

                bool holding = _sim._hold[k] > 0;
                if (holding) _sim._hold[k]--;

                int pressure = _sim.PressurePermille(side, slot);
                Read(side, slot, pressure);
                TrackOpenGoal(tick, k);
                V11Choice choice = _valuation.Choose(_scene, holding);

                switch (choice.Kind)
                {
                    case V11ActionKind.Hold:
                        return;
                    case V11ActionKind.Shot:
                        if (_sim._ctx.ShotLive) _sim.ForceResolveShot(tick);
                        _sim.TakeShot(tick, side, slot);
                        return;
                    case V11ActionKind.Pass:
                    case V11ActionKind.Cross:
                        Pass(tick, side, slot, choice, pressure);
                        return;
                    case V11ActionKind.Clear:
                        _sim.Clear(tick, side, slot);
                        return;
                    case V11ActionKind.RoundKeeper:
                    case V11ActionKind.Drive:
                        _sim.CarryTo(tick, side, slot, U.ClampX(U.Units(choice.XDm)), _sim._ctx.Inside(U.Units(choice.YDm)));
                        return;
                    default:
                        _sim.Carry(tick, side, slot);
                        return;
                }
            }

            /// <summary>
            /// How long this man has had the open goal in front of him. The spell survives the touch
            /// he takes in toward it (the ball loose between his own touches), and ends when the lane
            /// closes, when anybody else touches the ball, or when it goes dead.
            /// </summary>
            private void TrackOpenGoal(int tick, int k)
            {
                if (_scene.CarrierIsKeeper || !_valuation.IsOpenGoal(_scene))
                {
                    if (_openCarrier == k) _openCarrier = -1;
                    _scene.OpenGoalForMs = 0;
                    return;
                }

                if (_openCarrier != k)
                {
                    _openCarrier = k;
                    _openSince = tick;
                }

                _scene.OpenGoalForMs = (tick - _openSince) * 1000 / _sim._cfg.TicksPerSecond;
            }

            /// <summary>Once a tick: an open-goal spell ends when the ball is dead or anybody else has touched it.</summary>
            private void EndOpenGoalSpell()
            {
                if (_openCarrier < 0) return;
                MatchBall ball = _sim._ball;
                int n = _sim._n;
                int toucher = ball.Free ? ball.LastTouchSide * n + ball.LastTouchSlot : ball.OwnerSide * n + ball.OwnerSlot;
                if (ball.Dead || ball.LastTouchSide < 0 || toucher != _openCarrier) _openCarrier = -1;
            }

            private void Pass(int tick, int side, int slot, V11Choice choice, int pressure)
            {
                int k = side * _sim._n + slot;
                int tx = U.ClampX(U.Units(choice.XDm));
                int ty = _sim._ctx.Inside(U.Units(choice.YDm));
                int distance = U.Distance(_sim._px[k], _sim._py[k], tx, ty);
                int force = _sim._ball.ForceToArrive(distance, _sim._arrivalStepU, _sim._maxPassForce, out int _);

                _sim.PlayPass(tick, side, slot, new PassChoice
                {
                    Slot = _mateSlot[choice.Mate],
                    X = tx,
                    Y = ty,
                    Force = force,
                    Completion = choice.SafetyPermille
                }, pressure);
            }

            /// <summary>The scene as the man on the ball sees it, in decimetres.</summary>
            private void Read(int side, int slot, int pressure)
            {
                MatchSimulator sim = _sim;
                int n = sim._n;
                int k = side * n + slot;
                if (_mateSlot.Length != n)
                {
                    _mateSlot = new int[n];
                    _scene = new V11Scene(n);
                }

                V11Scene s = _scene;
                s.Reset();
                bool home = side == 0;
                s.AttacksHighX = home;
                s.BallXDm = U.Dm(sim._ball.X);
                s.BallYDm = U.Dm(sim._ball.Y);
                s.CarrierIsKeeper = sim._keeper[k];
                s.Shooting = sim._skShooting[k];
                s.Technique = sim._skTechnique[k];
                s.Passing = sim._skPassing[k];
                s.VisionPercent = sim._vision[k];
                s.PressurePermille = pressure;
                s.CarryKeepPermille = CarryKeep(side, k);
                s.CarryTouchDm = sim.CarryTouchDm(k, pressure);
                s.CanClear = sim.ClearRoom(side, k, out int landing);
                s.ClearXDm = U.Dm(landing);
                s.ClearYDm = sim._py[k] < U.CenterYU ? 60 : Pitch.WidthDm - 60;
                s.OffsideLineXDm = _lineX[side] + MovementGeometry.Direction(home) * sim._cfg.OffsideMarginDm;
                s.MaxPassDm = U.Dm(sim._maxPassRange);
                s.Instructions = sim._tactics[side].Instructions;
                s.ShotAppetitePercent = sim._tactics[side].ShotAppetitePercent;

                for (int j = 0; j < n; j++)
                {
                    int mk = side * n + j;
                    if (j == slot || sim._sentOff[mk]) continue;
                    _mateSlot[s.AddMate(Actor(mk), sim._keeper[mk])] = j;
                }

                int opponent = 1 - side;
                for (int j = 0; j < n; j++)
                {
                    int ok = opponent * n + j;
                    if (sim._sentOff[ok]) continue;
                    s.AddFoe(Actor(ok), sim._keeper[ok]);
                }
            }

            private PitchActor Actor(int k)
            {
                int perSecond = _sim._cfg.TicksPerSecond;
                return new PitchActor(
                    U.Dm(_sim._px[k]), U.Dm(_sim._py[k]),
                    _sim._vx[k] * perSecond / U.Scale, _sim._vy[k] * perSecond / U.Scale,
                    _sim._maxSpeed[k] * perSecond / U.Scale);
            }

            /// <summary>The odds he keeps it through one touch past the nearest man, as v10 reads the duel.</summary>
            private int CarryKeep(int side, int k)
            {
                MatchSimulator sim = _sim;
                int n = sim._n;
                int opponent = 1 - side;
                int nearest = -1, gap = int.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    int ok = opponent * n + j;
                    if (sim._sentOff[ok]) continue;
                    int distance = U.Distance(sim._px[k], sim._py[k], sim._px[ok], sim._py[ok]);
                    if (distance < gap)
                    {
                        gap = distance;
                        nearest = ok;
                    }
                }

                if (nearest < 0 || gap >= sim._pressureU) return 1000;
                int loss = sim.DuelWinPermille(k, nearest) * sim._cfg.DribbleFlightTicks;
                return 1000 - BallSkill.Clamp(loss, 0, 850);
            }
        }
    }
}
