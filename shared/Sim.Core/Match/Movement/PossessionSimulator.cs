using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The movement half of the match (task 13.1): it plays out the choreography the
    /// <see cref="PossessionPlanner"/> wrote and records where all 23 things on the pitch
    /// were, tick by tick, into a <see cref="PositionStream"/>.
    ///
    /// The ball is never a free-floating point: it is AT SOMEONE'S FEET, IN FLIGHT toward
    /// a team-mate or a goal, or DEAD on a spot somebody is running to collect. Players
    /// carry a velocity and steer toward a target that comes from their formation anchor
    /// bent by what their team is doing — the block pushing up and spreading in
    /// possession, dropping and squeezing without it, the nearest defender closing on the
    /// carrier, a receiver showing for the pass, the scripted shooter attacking his
    /// shooting position, the keeper tracking the ball across his line.
    ///
    /// It consumes NO randomness (every draw was spent in the planner) and only integer
    /// arithmetic, so the stream is bit-identical on .NET, Mono and IL2CPP. It also
    /// cannot touch the result: it is handed the finished report and works around it.
    ///
    /// One instance simulates one match.
    /// </summary>
    public sealed class PossessionSimulator
    {
        /// <summary>How close a player must get to a dead ball to pick it up.</summary>
        private const int PickupDm = 30;

        /// <summary>How long a dribble keeps a carrier sprinting.</summary>
        private const int DribbleSprintTicks = 4;

        private const int Held = 0, Flight = 1, Dead = 2;

        private readonly MatchBalance _cfg;

        private int _n;                 // players per side
        private int[] _px = System.Array.Empty<int>();
        private int[] _py = System.Array.Empty<int>();
        private int[] _vx = System.Array.Empty<int>();
        private int[] _vy = System.Array.Empty<int>();
        private int[] _ax = System.Array.Empty<int>();   // anchors
        private int[] _ay = System.Array.Empty<int>();
        private int[] _forward = System.Array.Empty<int>();
        private int[] _speed = System.Array.Empty<int>();
        private bool[] _keeper = System.Array.Empty<bool>();
        private int[] _sprintUntil = System.Array.Empty<int>();

        private int _ballX, _ballY;
        private int _ballMode;
        private int _ownerSide, _ownerSlot;
        private int _fromX, _fromY, _toX, _toY, _toSide, _toSlot, _flightStart, _flightEnd;
        private int _deadX, _deadY, _deadSide = -1, _deadSlot = -1;
        private bool _possessionHome = true;

        /// <summary>One scripted run: a player attacking a shooting position for a chance to come.</summary>
        private struct Aim
        {
            public int Side, Slot, X, Y, Tick;
        }

        // Approach windows overlap (chances land a minute apart, a run-up lasts longer), so
        // arming ONE shooter at a time silently cancelled the earlier run and left him standing
        // in his own half when the ball arrived. Queue them, steer by the earliest.
        private readonly List<Aim> _aims = new List<Aim>();
        private int[] _aimTick = System.Array.Empty<int>();   // per player: when his chance comes, or none
        private int[] _aimX = System.Array.Empty<int>();
        private int[] _aimY = System.Array.Empty<int>();

        private PositionStream _stream = new PositionStream();

        public PossessionSimulator(MatchBalance cfg)
        {
            _cfg = cfg;
        }

        public PositionStream Generate(
            Lineup home, Lineup away, MatchReport report, IRandomSource rng, int homePossessionPermille)
        {
            int lastTick = 90 * _cfg.TicksPerMinute;
            MatchScript script = new PossessionPlanner(_cfg, rng).Plan(home, away, report, homePossessionPermille, lastTick);

            Setup(home, away, lastTick);

            int step = 0;
            ApplySteps(script, ref step, 0);
            UpdateBall(0);
            WriteFrame(0);

            for (int t = 1; t <= lastTick; t++)
            {
                StepPlayers(t);
                ApplySteps(script, ref step, t);
                UpdateBall(t);
                WriteFrame(t);
            }

            return _stream;
        }

        // ---------------------------------------------------------------------- setup

        private void Setup(Lineup home, Lineup away, int lastTick)
        {
            _n = home.Slots.Count;
            int total = _n * 2;
            int frames = lastTick + 1;

            _px = new int[total];
            _py = new int[total];
            _vx = new int[total];
            _vy = new int[total];
            _ax = new int[total];
            _ay = new int[total];
            _forward = new int[total];
            _speed = new int[total];
            _keeper = new bool[total];
            _sprintUntil = new int[total];
            _aimTick = new int[total];
            _aimX = new int[total];
            _aimY = new int[total];

            for (int s = 0; s < 2; s++)
            {
                Lineup lineup = s == 0 ? home : away;
                PitchPoint[] anchors = MovementGeometry.Anchors(lineup, s == 0, _cfg);
                for (int i = 0; i < _n; i++)
                {
                    int k = s * _n + i;
                    LineupSlot slot = lineup.Slots[i];
                    _ax[k] = anchors[i].X;
                    _ay[k] = anchors[i].Y;
                    _px[k] = anchors[i].X;
                    _py[k] = anchors[i].Y;
                    _forward[k] = MovementGeometry.Forwardness(slot.Role, _cfg);
                    _keeper[k] = slot.Role == PositionRole.Goalkeeper;
                    _speed[k] = _cfg.PlayerSpeedBaseDmPerTick
                        + _cfg.PlayerSpeedPaceDmPerTick * slot.Player.Attributes.Pace / 100;
                    if (_speed[k] < 1) _speed[k] = 1;
                }
            }

            _ballX = Pitch.CenterX;
            _ballY = Pitch.CenterY;
            _ballMode = Dead;
            _deadX = Pitch.CenterX;
            _deadY = Pitch.CenterY;
            _ownerSide = 0;
            _ownerSlot = 0;

            _stream = new PositionStream
            {
                TicksPerMinute = _cfg.TicksPerMinute,
                PlayerCount = _n,
                LastTick = lastTick,
                BallXY = new int[frames * 2],
                HomeXY = new int[frames * _n * 2],
                AwayXY = new int[frames * _n * 2],
                Owner = new int[frames],
                HomePlayerIds = PlayerIds(home),
                AwayPlayerIds = PlayerIds(away),
                HomeShirts = ShirtNumbers.For(home),
                AwayShirts = ShirtNumbers.For(away)
            };
        }

        private static int[] PlayerIds(Lineup lineup)
        {
            var ids = new int[lineup.Slots.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = lineup.Slots[i].Player.Id;
            return ids;
        }

        // ---------------------------------------------------------------------- script

        private void ApplySteps(MatchScript script, ref int index, int tick)
        {
            List<ScriptStep> steps = script.Steps;
            while (index < steps.Count && steps[index].Tick <= tick)
            {
                Apply(steps[index], tick);
                index++;
            }
        }

        private void Apply(ScriptStep s, int tick)
        {
            int side = s.Home ? 0 : 1;
            switch (s.Op)
            {
                case ScriptOp.Kickoff:
                    SetDeadBall(Pitch.CenterX, Pitch.CenterY, side, MostAdvanced(side));
                    _possessionHome = s.Home;
                    Record(tick, BallActionKind.Kickoff, s.Home, _deadSlot, -1);
                    break;

                case ScriptOp.Pass:
                    ApplyPass(s, tick, side);
                    break;

                case ScriptOp.Dribble:
                    if (_ballMode == Held && _ownerSide == side)
                    {
                        _sprintUntil[side * _n + _ownerSlot] = tick + DribbleSprintTicks;
                        Record(tick, BallActionKind.Dribble, s.Home, _ownerSlot, -1);
                    }
                    break;

                case ScriptOp.AimShot:
                    _aims.Add(new Aim { Side = side, Slot = s.Slot, X = s.X, Y = s.Y, Tick = s.Flight });
                    break;

                case ScriptOp.Shot:
                    ApplyShot(s, tick, side);
                    break;

                case ScriptOp.Outcome:
                    ApplyOutcome(s, tick, side);
                    break;

                case ScriptOp.Turnover:
                    ApplyTurnover(s, tick, side);
                    break;

                case ScriptOp.DeadBall:
                    ApplyDeadBall(s, tick, side);
                    break;
            }
        }

        private void ApplyPass(ScriptStep s, int tick, int side)
        {
            int passer = CurrentPasser(side);
            int receiver = s.Target >= 0 && s.Target < _n ? s.Target : ResolveReceiver(side, passer, s.Bias);
            if (receiver == passer) receiver = (passer + 1) % _n;

            int k = side * _n + passer;
            _fromX = _ballMode == Dead ? _deadX : _px[k];
            _fromY = _ballMode == Dead ? _deadY : _py[k];
            _toSide = side;
            _toSlot = receiver;
            _flightStart = tick;
            _flightEnd = tick + (s.Flight > 0 ? s.Flight : 1);
            _ballMode = Flight;
            _deadSide = -1;
            _deadSlot = -1;
            _possessionHome = s.Home;
            _sprintUntil[side * _n + receiver] = _flightEnd;

            Record(tick, LabelPass(s.Kind, side, passer), s.Home, passer, receiver);
        }

        private void ApplyShot(ScriptStep s, int tick, int side)
        {
            int shooter = s.Slot >= 0 && s.Slot < _n ? s.Slot : CurrentPasser(side);
            int k = side * _n + shooter;

            // The shooter has the ball at this exact tick: that is the contract the event
            // timeline is rendered against (ball position == shooter position at M * tpm).
            _ownerSide = side;
            _ownerSlot = shooter;
            _fromX = _px[k];
            _fromY = _py[k];
            _toSide = -1;
            _toSlot = -1;
            _toX = s.X;
            _toY = s.Y;
            _flightStart = tick;
            _flightEnd = tick + (s.Flight > 0 ? s.Flight : 1);
            _ballMode = Flight;
            _deadSide = -1;
            _deadSlot = -1;
            _possessionHome = s.Home;
            DisarmAim(side, shooter);

            Record(tick, BallActionKind.Shot, s.Home, shooter, -1);
        }

        private void ApplyOutcome(ScriptStep s, int tick, int side)
        {
            int defending = 1 - side;
            switch (s.Kind)
            {
                case BallActionKind.Save:
                    // Gathered just in front of the line, not on it, so the keeper is a
                    // step away from it rather than standing in his own net.
                    SetDeadBall(s.X + (s.Home ? -45 : 45), s.Y, defending, KeeperOf(defending));
                    _possessionHome = defending == 0;
                    Record(tick, BallActionKind.Save, defending == 0, _deadSlot, -1);
                    break;

                case BallActionKind.Goal:
                    SetDeadBall(s.X, s.Y, -1, -1);
                    Record(tick, BallActionKind.Goal, s.Home, s.Slot, -1);
                    break;

                default:
                    SetDeadBall(s.X, s.Y, -1, -1);
                    Record(tick, BallActionKind.Miss, s.Home, s.Slot, -1);
                    break;
            }
        }

        private void ApplyTurnover(ScriptStep s, int tick, int side)
        {
            int winner = ResolveNearest(side, _ballX, _ballY, includeKeeper: false);
            _ballMode = Held;
            _ownerSide = side;
            _ownerSlot = winner;
            _deadSide = -1;
            _deadSlot = -1;
            _possessionHome = s.Home;
            Record(tick, s.Kind == BallActionKind.Interception ? BallActionKind.Interception : BallActionKind.Tackle,
                s.Home, winner, -1);
        }

        private void ApplyDeadBall(ScriptStep s, int tick, int side)
        {
            int x, y, taker;
            switch (s.Kind)
            {
                case BallActionKind.GoalKick:
                    int own = MovementGeometry.OwnGoalX(s.Home);
                    x = s.Home ? own + 55 : own - 55;
                    y = Pitch.CenterY;
                    taker = KeeperOf(side);
                    break;

                case BallActionKind.Corner:
                    x = MovementGeometry.AttackedGoalX(s.Home);
                    y = _ballY < Pitch.CenterY ? 0 : Pitch.WidthDm;
                    taker = ResolveNearest(side, x, y, includeKeeper: false);
                    break;

                case BallActionKind.ThrowIn:
                    x = _ballX;
                    y = _ballY < Pitch.CenterY ? 0 : Pitch.WidthDm;
                    taker = ResolveNearest(side, x, y, includeKeeper: false);
                    break;

                default:
                    x = _ballX;
                    y = _ballY;
                    taker = ResolveNearest(side, x, y, includeKeeper: false);
                    break;
            }

            SetDeadBall(Pitch.ClampX(x), Pitch.ClampY(y), side, taker);
            _possessionHome = s.Home;
            Record(tick, s.Kind, s.Home, taker, -1);
        }

        private void SetDeadBall(int x, int y, int side, int slot)
        {
            _ballMode = Dead;
            _deadX = Pitch.ClampX(x);
            _deadY = Pitch.ClampY(y);
            _deadSide = side;
            _deadSlot = slot;
        }

        private void Record(int tick, BallActionKind kind, bool home, int slot, int target) =>
            _stream.Actions.Add(new BallAction(tick, kind, home, slot, target));

        /// <summary>A cross is only a cross from a wide, advanced position; otherwise it is just a pass.</summary>
        private BallActionKind LabelPass(BallActionKind kind, int side, int passer)
        {
            if (kind != BallActionKind.Cross) return kind;

            int k = side * _n + passer;
            bool wide = _py[k] < Pitch.WidthDm / 4 || _py[k] > Pitch.WidthDm * 3 / 4;
            int goalX = MovementGeometry.AttackedGoalX(side == 0);
            bool advanced = MovementGeometry.Distance(_px[k], _py[k], goalX, Pitch.CenterY) < Pitch.LengthDm / 3;
            return wide && advanced ? BallActionKind.Cross : BallActionKind.Pass;
        }

        // ---------------------------------------------------------------------- ball

        private void UpdateBall(int tick)
        {
            if (_ballMode == Flight)
            {
                int tx, ty;
                if (_toSlot >= 0)
                {
                    int k = _toSide * _n + _toSlot;
                    tx = _px[k];
                    ty = _py[k];
                }
                else
                {
                    tx = _toX;
                    ty = _toY;
                }

                int span = _flightEnd - _flightStart;
                int stepIndex = tick - _flightStart;
                _ballX = MovementGeometry.Lerp(_fromX, tx, stepIndex, span);
                _ballY = MovementGeometry.Lerp(_fromY, ty, stepIndex, span);

                if (tick >= _flightEnd)
                {
                    if (_toSlot >= 0)
                    {
                        _ballMode = Held;
                        _ownerSide = _toSide;
                        _ownerSlot = _toSlot;
                    }
                    else
                    {
                        SetDeadBall(_toX, _toY, _deadSide, _deadSlot);
                    }
                }
            }

            if (_ballMode == Dead)
            {
                _ballX = _deadX;
                _ballY = _deadY;

                if (_deadSide >= 0 && _deadSlot >= 0)
                {
                    int k = _deadSide * _n + _deadSlot;
                    if (MovementGeometry.Distance(_px[k], _py[k], _deadX, _deadY) <= PickupDm)
                    {
                        _ballMode = Held;
                        _ownerSide = _deadSide;
                        _ownerSlot = _deadSlot;
                        _deadSide = -1;
                        _deadSlot = -1;
                    }
                }
            }

            if (_ballMode == Held)
            {
                int k = _ownerSide * _n + _ownerSlot;
                _ballX = _px[k];
                _ballY = _py[k];
            }

            _ballX = Pitch.ClampX(_ballX);
            _ballY = Pitch.ClampY(_ballY);
        }

        // ---------------------------------------------------------------------- players

        /// <summary>Drops a run once its shot has been struck.</summary>
        private void DisarmAim(int side, int slot)
        {
            for (int i = _aims.Count - 1; i >= 0; i--)
                if (_aims[i].Side == side && _aims[i].Slot == slot)
                    _aims.RemoveAt(i);
        }

        /// <summary>
        /// Which players are currently making a run at a shooting position, and where. Every
        /// armed runner steers — two chances can be in the air at once — and a player with two
        /// of his own pending takes the nearer one first.
        /// </summary>
        private void ResolveAims(int tick)
        {
            for (int k = 0; k < _aimTick.Length; k++) _aimTick[k] = int.MaxValue;

            for (int i = _aims.Count - 1; i >= 0; i--)
            {
                Aim aim = _aims[i];
                if (aim.Tick < tick)
                {
                    _aims.RemoveAt(i); // its chance came and went
                    continue;
                }

                int k = aim.Side * _n + aim.Slot;
                if (k < 0 || k >= _aimTick.Length || aim.Tick >= _aimTick[k]) continue;

                _aimTick[k] = aim.Tick;
                _aimX[k] = aim.X;
                _aimY[k] = aim.Y;
            }
        }

        private void StepPlayers(int tick)
        {
            ResolveAims(tick);

            int presserHome = _possessionHome ? -1 : NearestOutfield(0, _ballX, _ballY, -1);
            int coverHome = _possessionHome ? -1 : NearestOutfield(0, _ballX, _ballY, presserHome);
            int presserAway = _possessionHome ? NearestOutfield(1, _ballX, _ballY, -1) : -1;
            int coverAway = _possessionHome ? NearestOutfield(1, _ballX, _ballY, presserAway) : -1;

            for (int s = 0; s < 2; s++)
            {
                bool home = s == 0;
                bool inPossession = _possessionHome == home;
                int presser = s == 0 ? presserHome : presserAway;
                int cover = s == 0 ? coverHome : coverAway;

                for (int i = 0; i < _n; i++)
                {
                    int k = s * _n + i;
                    bool sprint = tick <= _sprintUntil[k];
                    int tx, ty;

                    if (_deadSide == s && _deadSlot == i)
                    {
                        tx = _deadX;
                        ty = _deadY;
                        sprint = true;
                    }
                    else if (_keeper[k])
                    {
                        KeeperTarget(home, out tx, out ty);
                    }
                    else if (_aimTick[k] != int.MaxValue)
                    {
                        tx = _aimX[k];
                        ty = _aimY[k];
                        sprint = true;
                    }
                    else if (_ballMode == Held && _ownerSide == s && _ownerSlot == i)
                    {
                        ShapeTarget(s, i, home, inPossession, out tx, out ty);
                        tx += MovementGeometry.Direction(home) * 90;
                    }
                    else if (_ballMode == Flight && _toSide == s && _toSlot == i)
                    {
                        ShapeTarget(s, i, home, inPossession, out tx, out ty);
                        tx += MovementGeometry.Direction(home) * _cfg.SupportRunDm;
                        ty += (_ballY - ty) / 4;
                        sprint = true;
                    }
                    else if (i == presser)
                    {
                        PressTarget(k, _cfg.PressDistanceDm, out tx, out ty);
                        sprint = true;
                    }
                    else if (i == cover)
                    {
                        PressTarget(k, _cfg.PressDistanceDm * 4, out tx, out ty);
                    }
                    else
                    {
                        ShapeTarget(s, i, home, inPossession, out tx, out ty);
                    }

                    Move(k, Pitch.ClampX(tx), Pitch.ClampY(ty), sprint);
                }
            }
        }

        /// <summary>Where a player belongs given his anchor and what his team is doing.</summary>
        private void ShapeTarget(int side, int slot, bool home, bool inPossession, out int tx, out int ty)
        {
            int k = side * _n + slot;
            int dir = MovementGeometry.Direction(home);

            int pull = inPossession ? _cfg.PossessionPullXPercent : _cfg.DefensivePullXPercent;
            tx = _ax[k] + (_ballX - Pitch.CenterX) * pull / 100;
            tx += dir * (inPossession ? _cfg.PossessionPushDm : -_cfg.DefensiveDropDm) * _forward[k] / 1000;

            int offset = _ay[k] - Pitch.CenterY;
            ty = inPossession
                ? Pitch.CenterY + offset * (100 + _cfg.WidthExpandPercent) / 100
                    + (_ballY - Pitch.CenterY) * _cfg.PossessionBallShiftYPercent / 100
                : Pitch.CenterY + offset * _cfg.CompactPercent / 100
                    + (_ballY - Pitch.CenterY) * _cfg.DefensiveBallShiftYPercent / 100;
        }

        /// <summary>A point <paramref name="standOff"/> short of the ball, on the defender's own side of it.</summary>
        private void PressTarget(int k, int standOff, out int tx, out int ty)
        {
            int dx = _px[k] - _ballX;
            int dy = _py[k] - _ballY;
            int d = MovementGeometry.Sqrt(dx * dx + dy * dy);
            if (d <= 0)
            {
                tx = _ballX;
                ty = _ballY;
                return;
            }

            int reach = d < standOff ? d : standOff;
            tx = _ballX + dx * reach / d;
            ty = _ballY + dy * reach / d;
        }

        private void KeeperTarget(bool home, out int tx, out int ty)
        {
            int goalX = MovementGeometry.OwnGoalX(home);
            int distance = _ballX - goalX;
            if (distance < 0) distance = -distance;

            // Sweeper-keeper: he pushes off his line while play is up the other end and
            // drops back onto it as the ball comes at him.
            int far = Pitch.LengthDm / 2;
            int reach = distance < far ? distance : far;

            int depth = _cfg.KeeperDepthDm + _cfg.KeeperRushDm * reach / far;
            tx = home ? depth : Pitch.LengthDm - depth;
            ty = Pitch.CenterY + (_ballY - Pitch.CenterY) * _cfg.KeeperLateralPercent / 100;
            ty = MovementGeometry.Clamp(ty, Pitch.CenterY - 110, Pitch.CenterY + 110);
        }

        /// <summary>Steers one player toward his target: inertia, then a hard speed cap.</summary>
        private void Move(int k, int tx, int ty, bool sprint)
        {
            int speed = sprint ? _speed[k] * _cfg.PlayerSprintPercent / 100 : _speed[k];
            if (speed < 1) speed = 1;

            int dx = tx - _px[k];
            int dy = ty - _py[k];
            int distance = MovementGeometry.Sqrt(dx * dx + dy * dy);

            int wantX = 0, wantY = 0;
            if (distance > 0)
            {
                int reach = distance < speed ? distance : speed;
                wantX = dx * reach / distance;
                wantY = dy * reach / distance;
            }

            int inertia = _cfg.PlayerInertiaPercent;
            _vx[k] = (_vx[k] * inertia + wantX * (100 - inertia)) / 100;
            _vy[k] = (_vy[k] * inertia + wantY * (100 - inertia)) / 100;

            int magnitude = MovementGeometry.Sqrt(_vx[k] * _vx[k] + _vy[k] * _vy[k]);
            if (magnitude > speed)
            {
                _vx[k] = _vx[k] * speed / magnitude;
                _vy[k] = _vy[k] * speed / magnitude;
            }

            _px[k] = Pitch.ClampX(_px[k] + _vx[k]);
            _py[k] = Pitch.ClampY(_py[k] + _vy[k]);
        }

        // ---------------------------------------------------------------------- lookups

        private int CurrentPasser(int side)
        {
            if (_ballMode == Held && _ownerSide == side) return _ownerSlot;
            if (_ballMode == Dead && _deadSide == side && _deadSlot >= 0) return _deadSlot;
            if (_ballMode == Flight && _toSide == side && _toSlot >= 0) return _toSlot;
            return ResolveNearest(side, _ballX, _ballY, includeKeeper: false);
        }

        /// <summary>The best team-mate for a pass with the given direction bias. Pure, so it is deterministic.</summary>
        private int ResolveReceiver(int side, int from, int bias)
        {
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);
            int fk = side * _n + from;

            int best = -1;
            long bestScore = long.MinValue;
            for (int j = 0; j < _n; j++)
            {
                if (j == from) continue;
                int k = side * _n + j;
                if (_keeper[k] && bias != MatchScript.BiasBack) continue;

                int gain = dir * (_px[k] - _px[fk]);
                int distance = MovementGeometry.Distance(_px[k], _py[k], _px[fk], _py[fk]);

                long score;
                switch (bias)
                {
                    case MatchScript.BiasBack:
                        score = -2L * gain - Away(distance, 150);
                        break;
                    case MatchScript.BiasSquare:
                        score = -(long)Away(gain, 0) - Away(distance, 190);
                        break;
                    case MatchScript.BiasLong:
                        score = 3L * gain - Away(distance, 430);
                        break;
                    default:
                        score = 2L * gain - Away(distance, 230);
                        break;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = j;
                }
            }

            return best >= 0 ? best : (from + 1) % _n;
        }

        private static int Away(int value, int of)
        {
            int d = value - of;
            return d < 0 ? -d : d;
        }

        private int ResolveNearest(int side, int x, int y, bool includeKeeper)
        {
            int best = -1, bestDistance = int.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (_keeper[k] && !includeKeeper) continue;

                int d = MovementGeometry.Distance(_px[k], _py[k], x, y);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best >= 0 ? best : 0;
        }

        private int NearestOutfield(int side, int x, int y, int exclude)
        {
            int best = -1, bestDistance = int.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                if (i == exclude) continue;
                int k = side * _n + i;
                if (_keeper[k]) continue;

                int d = MovementGeometry.Distance(_px[k], _py[k], x, y);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best;
        }

        private int KeeperOf(int side)
        {
            for (int i = 0; i < _n; i++)
                if (_keeper[side * _n + i]) return i;
            return 0;
        }

        /// <summary>The most advanced player of a side — the one who takes a kickoff.</summary>
        private int MostAdvanced(int side)
        {
            int best = 0, bestForward = -1;
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (_forward[k] > bestForward)
                {
                    bestForward = _forward[k];
                    best = i;
                }
            }

            return best;
        }

        // ---------------------------------------------------------------------- output

        private void WriteFrame(int tick)
        {
            _stream.BallXY[tick * 2] = _ballX;
            _stream.BallXY[tick * 2 + 1] = _ballY;

            int baseIndex = tick * _n * 2;
            for (int i = 0; i < _n; i++)
            {
                _stream.HomeXY[baseIndex + i * 2] = _px[i];
                _stream.HomeXY[baseIndex + i * 2 + 1] = _py[i];
                _stream.AwayXY[baseIndex + i * 2] = _px[_n + i];
                _stream.AwayXY[baseIndex + i * 2 + 1] = _py[_n + i];
            }

            _stream.Owner[tick] = _ballMode == Held
                ? _stream.OwnerCode(_ownerSide == 0, _ownerSlot)
                : PositionStream.NoOwner;
        }
    }
}
