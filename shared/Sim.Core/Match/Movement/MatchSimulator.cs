using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The visual match engine (task 13.2): twenty-two agents and a ball, playing.
    ///
    /// It replaces the choreographer of 13.1, which wrote a script in tick-space and made
    /// the players act it out. That could never look like football, because nobody in it
    /// decided anything: the ball's owner was assigned rather than won, a pass happened
    /// because the script said so rather than because one was on, and nothing knew the ball
    /// had gone out. Here the model is the one the literature settles on for a believable
    /// 2D match (Buckland, *Programming Game AI by Example*, ch. 4): each player has a home
    /// region from the formation, a small set of states, and steering; each team has a
    /// brain that picks who chases, who supports and who marks; and the ball is an object
    /// with velocity and friction, so a pass can be read and cut out, and crossing a line
    /// IS the throw-in.
    ///
    /// The score still belongs to the 1.4 result model — see <see cref="MatchDirector"/>,
    /// which works the ball toward the scripted shooter and lets him strike it on his
    /// minute. Everything else emerges.
    ///
    /// Deterministic: integer arithmetic in sixteenths of a decimetre, a fixed iteration
    /// order, and every random draw taken from the seeded source in that same order.
    /// </summary>
    public sealed class MatchSimulator
    {
        private const int SideCount = 2;
        private const int MaxSupporters = 3;

        private readonly MatchBalance _cfg;

        private IRandomSource _rng = null!;
        private MatchBall _ball = new MatchBall();
        private MatchDirector _director = null!;
        private PositionStream _stream = new PositionStream();

        private int _n;
        private int[] _px = System.Array.Empty<int>();
        private int[] _py = System.Array.Empty<int>();
        private int[] _vx = System.Array.Empty<int>();
        private int[] _vy = System.Array.Empty<int>();
        private int[] _maxSpeed = System.Array.Empty<int>();
        private int[] _accel = System.Array.Empty<int>();
        private int[] _baseX = System.Array.Empty<int>();   // permille along the pitch
        private int[] _baseY = System.Array.Empty<int>();   // permille across it
        private int[] _forward = System.Array.Empty<int>();
        private bool[] _keeper = System.Array.Empty<bool>();
        private int[] _hold = System.Array.Empty<int>();
        private int[] _mark = System.Array.Empty<int>();

        private readonly MovementTactics[] _tactics = new MovementTactics[SideCount];
        private readonly bool[] _attacking = new bool[SideCount];
        private readonly int[] _chaser = new int[SideCount];
        private readonly int[] _receiver = new int[SideCount];
        private readonly int[] _receiveX = new int[SideCount];
        private readonly int[] _receiveY = new int[SideCount];
        private readonly int[] _supportX = new int[SideCount];
        private readonly int[] _supportY = new int[SideCount];
        private readonly int[] _supporters = new int[SideCount * MaxSupporters];

        /// <summary>The man whose chance is coming: the other side marks him a shade less tightly.</summary>
        private readonly int[] _looseMan = new int[SideCount];

        // Dead ball: what it is, who takes it, and when it may be taken.
        private BallActionKind _deadKind;
        private int _deadSide = -1;
        private int _deadTaker = -1;
        private int _deadAt;

        // A scripted strike in flight, and what the timeline says it becomes.
        private bool _shotLive;
        private bool _shotHome;
        private int _shotSlot;
        private MatchEventType _shotOutcome;

        /// <summary>No second challenge for a moment after one is won, or the ball ping-pongs.</summary>
        private int _tackleLock;

        /// <summary>When a live strike must have become a goal, a save or a ball out of play.</summary>
        private int _shotExpires;

        /// <summary>How often the director had to hand the shooter the ball. Diagnostic.</summary>
        private int _forcedShots;
        private readonly bool[] _driving = new bool[SideCount];
        private readonly bool[] _urgent = new bool[SideCount];
        private readonly int[] _second = new int[SideCount];
        private int _releasedBy = -1;
        private int _releasedUntil = -1;

        private int _controlU, _kickU, _separationU, _interceptU;
        private int _maxPassForce, _maxShootForce, _shootRangeU;

        public MatchSimulator(MatchBalance cfg)
        {
            _cfg = cfg;
        }

        /// <summary>How many scripted strikes needed the ball handing to the shooter (diagnostic).</summary>
        public int ForcedShots => _forcedShots;

        // ------------------------------------------------------------------ entry point

        public PositionStream Generate(
            Lineup home, Lineup away, MatchReport report, IRandomSource rng,
            MatchTactics? tactics, int homePossessionPermille)
        {
            _rng = rng;
            int lastTick = 90 * _cfg.TicksPerMinute;

            Setup(home, away, report, tactics, lastTick);
            WriteFrame(0);

            for (int t = 1; t <= lastTick; t++)
            {
                Tick(t);

                // The whistle. A strike taken on the last tick has no frame left to fly in, so
                // it is settled here — otherwise a ninetieth-minute winner is on the timeline
                // and nowhere in the picture.
                if (t == lastTick && _shotLive) ForceResolveShot(t);

                WriteFrame(t);
            }

            return _stream;
        }

        private void Tick(int tick)
        {
            _director.Advance(tick);
            UpdateTeams(tick);

            // His minute has come. By now the window should have worked the ball to him; if it
            // has not, he is given it — the one place this engine overrides the play, and the
            // number to watch.
            if (_director.ShotDue(tick, out MatchDirector.Chance due))
            {
                int side = due.Home ? 0 : 1;
                bool deadline = _director.ShotDeadline(tick);

                // Who actually strikes it. The timeline says WHO the chance belongs to, and the
                // toast will name him; the picture has to show the ball leaving a real foot in
                // a real position. So the named man takes it when he is on the ball, and
                // otherwise it is struck by whoever of his side is nearest to it — the ball is
                // never carried to him, because a ball crossing the pitch on its own is a far
                // louder lie than the one nobody can see.
                int striker = due.Slot;
                int k = side * _n + striker;
                if (U.Distance(_px[k], _py[k], _ball.X, _ball.Y) > _kickU)
                    striker = _ball.OwnerSide == side
                        ? _ball.OwnerSlot
                        : NearestTo(side, _ball.X, _ball.Y, includeKeeper: false);

                // Chances can be a minute apart while a strike takes longer than that to run its
                // course. The one already in the air has to be called first — and the new one
                // then waits a tick, because settling a goal puts the ball in the net and a
                // strike struck from there is a goal frame with the ball nowhere near the line.
                if (_shotLive) ForceResolveShot(tick);

                if (deadline || Shootable(side, striker))
                {
                    if (!Shootable(side, striker)) _forcedShots++;
                    if (_ball.Dead) { _ball.Dead = false; _deadTaker = -1; }

                    TakeShot(tick, side, striker, due.Outcome, due.Slot);
                    _director.MarkTaken();
                }
            }

            for (int side = 0; side < SideCount; side++)
                for (int slot = 0; slot < _n; slot++)
                    Act(tick, side, slot);

            for (int side = 0; side < SideCount; side++)
                for (int slot = 0; slot < _n; slot++)
                    Move(tick, side, slot);

            _ball.Advance();
            ResolveControl(tick);
            ResolveOutOfPlay(tick);
            ResolveStuckShot(tick);

            // Celebration over: the ball goes back to the centre spot for the kickoff.
            if (_ball.Dead && _deadKind == BallActionKind.Goal && tick >= _deadAt)
            {
                int conceding = _deadSide;
                _ball.Place(U.CenterXU, U.CenterYU);
                _deadKind = BallActionKind.Kickoff;
                _deadTaker = MostAdvanced(conceding);
                _deadAt = tick + _cfg.DeadBallTicks;
                Record(tick, BallActionKind.Kickoff, conceding == 0, _deadTaker, -1);
            }
        }

        // ------------------------------------------------------------------ setup

        private void Setup(Lineup home, Lineup away, MatchReport report, MatchTactics? tactics, int lastTick)
        {
            _n = home.Slots.Count;
            int total = _n * SideCount;
            int frames = lastTick + 1;

            _px = new int[total];
            _py = new int[total];
            _vx = new int[total];
            _vy = new int[total];
            _maxSpeed = new int[total];
            _accel = new int[total];
            _baseX = new int[total];
            _baseY = new int[total];
            _forward = new int[total];
            _keeper = new bool[total];
            _hold = new int[total];
            _mark = new int[total];

            _controlU = U.Units(_cfg.ControlRadiusDm);
            _kickU = U.Units(_cfg.KickRangeDm);
            _separationU = U.Units(_cfg.SeparationRadiusDm);
            _interceptU = U.Units(_cfg.InterceptReachDm);
            _maxPassForce = U.Units(_cfg.MaxPassForceDmPerTick);
            _maxShootForce = U.Units(_cfg.MaxShootForceDmPerTick);
            _shootRangeU = U.Units(_cfg.MaxShootRangeDm);

            _tactics[0] = MovementTactics.From(tactics?.Home, _cfg);
            _tactics[1] = MovementTactics.From(tactics?.Away, _cfg);

            for (int side = 0; side < SideCount; side++)
            {
                Lineup lineup = side == 0 ? home : away;
                var roles = new List<PositionRole>(_n);
                for (int i = 0; i < _n; i++) roles.Add(lineup.Slots[i].Role);

                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    LineupSlot slot = lineup.Slots[i];
                    SlotPosition? custom = slot.Position;

                    _baseX[k] = custom.HasValue ? custom.Value.XPermille : FormationGeometry.AnchorX(roles[i], _cfg);
                    _baseY[k] = custom.HasValue ? custom.Value.YPermille : FormationGeometry.AnchorY(roles, i, _cfg);
                    _forward[k] = FormationGeometry.AnchorX(roles[i], _cfg);
                    _keeper[k] = roles[i] == PositionRole.Goalkeeper;

                    int pace = slot.Player.Attributes.Pace;
                    _maxSpeed[k] = U.Units(_cfg.PlayerSpeedBaseDmPerTick + _cfg.PlayerSpeedPaceDmPerTick * pace / 100);
                    if (_maxSpeed[k] < U.Scale) _maxSpeed[k] = U.Scale;
                    _accel[k] = _maxSpeed[k] * _cfg.PlayerAccelPercent / 100;
                    if (_accel[k] < 1) _accel[k] = 1;
                    _mark[k] = -1;

                    HomeSpot(side, i, attacking: false, out int hx, out int hy);
                    _px[k] = hx;
                    _py[k] = hy;
                }

                _chaser[side] = -1;
                _receiver[side] = -1;
                for (int s = 0; s < MaxSupporters; s++) _supporters[side * MaxSupporters + s] = -1;
            }

            _attacking[0] = true;
            _attacking[1] = false;

            _director = new MatchDirector(report, home, away, _cfg, lastTick);

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

            // Kick off: ball on the centre spot, the home side's most advanced man to take it.
            _ball.Place(U.CenterXU, U.CenterYU);
            _ball.Dead = true;
            _ball.LastTouchSide = 1;
            _deadKind = BallActionKind.Kickoff;
            _deadSide = 0;
            _deadTaker = MostAdvanced(0);
            _deadAt = _cfg.DeadBallTicks;
            Record(0, BallActionKind.Kickoff, true, _deadTaker, -1);
        }

        private static int[] PlayerIds(Lineup lineup)
        {
            var ids = new int[lineup.Slots.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = lineup.Slots[i].Player.Id;
            return ids;
        }

        // ------------------------------------------------------------------ team brain

        private void UpdateTeams(int tick)
        {
            _looseMan[0] = -1;
            _looseMan[1] = -1;
            for (int side = 0; side < SideCount; side++)
            {
                _attacking[side] = _ball.OwnerSide == side
                    || (_ball.Free && _ball.LastTouchSide == side);

                _chaser[side] = NearestToBall(side, includeKeeper: false);
                _second[side] = SecondNearestToBall(side, _chaser[side]);

                // During his window the man the chance is for goes for a loose ball himself.
                int priority = _director.PriorityTarget(tick, side == 0);
                if (priority >= 0 && _ball.Free && !_ball.Dead
                    && U.Distance(_px[side * _n + priority], _py[side * _n + priority], _ball.X, _ball.Y)
                       < U.Units(_cfg.ChanceChaseRangeDm))
                    _chaser[side] = priority;

                _looseMan[side] = priority;
                _driving[side] = priority >= 0;

                // The last stretch before his minute. From here the side that the timeline says
                // is about to have a chance plays like a side about to have one: it hunts the
                // ball back and it goes forward with it. Without this the strike arrives with
                // the ball still in midfield and has to be forced from forty metres.
                int left = _director.TicksToChance(tick, side == 0);
                _urgent[side] = left >= 0 && left <= _cfg.ChanceUrgencyTicks;

                if (_attacking[side]) UpdateSupport(tick, side);
                else AssignMarks(side);
            }
        }

        /// <summary>
        /// The supporting run. A grid of spots in the attacking half is scored on whether the
        /// man on the ball could find it, whether a goal could be struck from it, and whether
        /// it is a comfortable distance away; the best of them is where the attackers run.
        /// This is what a viewer reads as a pattern of play rather than as milling about.
        /// </summary>
        private void UpdateSupport(int tick, int side)
        {
            if (tick % _cfg.SupportRecalcTicks == 0 || _supportX[side] == 0)
                FindSupportSpot(side);

            int carrier = _ball.OwnerSide == side ? _ball.OwnerSlot : -1;
            int wanted = _tactics[side].Supporters;
            if (wanted > MaxSupporters) wanted = MaxSupporters;

            for (int s = 0; s < MaxSupporters; s++) _supporters[side * MaxSupporters + s] = -1;

            // The most advanced men who are neither on the ball nor chasing it go and support.
            for (int s = 0; s < wanted; s++)
            {
                int best = -1, bestScore = int.MinValue;
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    if (_keeper[k] || i == carrier || i == _chaser[side]) continue;
                    if (AlreadySupporting(side, i)) continue;

                    int distance = U.Distance(_px[k], _py[k], _supportX[side], _supportY[side]);
                    int score = _forward[k] * 4 - distance / U.Scale;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }

                _supporters[side * MaxSupporters + s] = best;
            }
        }

        private bool AlreadySupporting(int side, int slot)
        {
            for (int s = 0; s < MaxSupporters; s++)
                if (_supporters[side * MaxSupporters + s] == slot) return true;
            return false;
        }

        private void FindSupportSpot(int side)
        {
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);
            int goalX = U.Units(MovementGeometry.AttackedGoalX(home));

            int columns = _cfg.SupportSpotColumns, rows = _cfg.SupportSpotRows;
            int fromX = home ? U.CenterXU : U.Units(60);
            int spanX = home ? U.LengthU - U.Units(60) - fromX : U.CenterXU - fromX;

            int carrierX = _ball.X, carrierY = _ball.Y;
            int bestX = U.CenterXU + dir * U.Units(200), bestY = U.CenterYU, bestScore = int.MinValue;

            for (int c = 0; c < columns; c++)
            {
                int x = fromX + spanX * (c + 1) / (columns + 1);
                for (int r = 0; r < rows; r++)
                {
                    int y = U.Units(80) + (U.WidthU - U.Units(160)) * r / (rows - 1);

                    int score = 0;
                    if (PassSafe(side, carrierX, carrierY, x, y, _maxPassForce)) score += 200;
                    if (U.Distance(x, y, goalX, U.CenterYU) < _shootRangeU
                        && PassSafe(side, x, y, goalX, U.CenterYU, _maxShootForce)) score += 150;

                    int gap = U.Distance(x, y, carrierX, carrierY);
                    int ideal = U.Units(_cfg.SupportIdealDistanceDm);
                    int off = gap > ideal ? gap - ideal : ideal - gap;
                    score += 120 - off / U.Scale;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            _supportX[side] = bestX;
            _supportY[side] = bestY;
        }

        /// <summary>
        /// Who picks up whom. Defenders take the most advanced opponents first and each
        /// opponent is taken once — the naive "everybody marks his nearest" leaves three men
        /// on one opponent and nobody on the rest, which is exactly what a crowd looks like.
        /// </summary>
        private void AssignMarks(int side)
        {
            int opponent = 1 - side;
            var taken = new bool[_n];

            for (int i = 0; i < _n; i++) _mark[side * _n + i] = -1;

            // Our men, deepest first.
            for (int pass = 0; pass < _n; pass++)
            {
                int me = -1, meDepth = int.MaxValue;
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    if (_keeper[k] || _mark[k] >= 0) continue;
                    if (_forward[k] < meDepth)
                    {
                        meDepth = _forward[k];
                        me = i;
                    }
                }

                if (me < 0) break;

                int best = -1, bestScore = int.MinValue;
                for (int j = 0; j < _n; j++)
                {
                    int ok = opponent * _n + j;
                    if (_keeper[ok] || taken[j]) continue;

                    // Danger first (how far up he is), then how near he is to me.
                    int distance = U.Distance(_px[side * _n + me], _py[side * _n + me], _px[ok], _py[ok]);
                    int score = _forward[ok] * 3 - distance / U.Scale;
                    if (j == _looseMan[opponent]) score -= 600;   // he has found a yard
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = j;
                    }
                }

                _mark[side * _n + me] = best;
                if (best >= 0) taken[best] = true;
            }
        }

        // ------------------------------------------------------------------ acting

        /// <summary>The one decision a player makes per tick: what to do with the ball if he has it.</summary>
        private void Act(int tick, int side, int slot)
        {
            int k = side * _n + slot;

            // Putting a dead ball back in play.
            if (_ball.Dead)
            {
                if (_deadSide == side && _deadTaker == slot && tick >= _deadAt
                    && U.Distance(_px[k], _py[k], _ball.X, _ball.Y) <= _kickU)
                    TakeRestart(tick, side, slot);
                return;
            }

            if (_ball.OwnerSide != side || _ball.OwnerSlot != slot) return;

            if (_hold[k] > 0)
            {
                _hold[k]--;
                return;
            }

            // The chance is his: once he is in the half he is attacking he does not give it
            // back, he goes. Deep in his own half he is still just a footballer — carrying it
            // out from there is how a whole match turns into one long dribble.
            if (_director.PriorityTarget(tick, side == 0) == slot)
            {
                bool inAttackingHalf = side == 0 ? _px[k] > U.CenterXU : _px[k] < U.CenterXU;
                if (inAttackingHalf)
                {
                    Carry(tick, side, slot);
                    return;
                }
            }

            // Look up. A pass if one is on, otherwise carry it, otherwise get rid of it.
            if (TryPass(tick, side, slot)) return;

            if (UnderPressure(side, slot) && !_keeper[k])
            {
                Clear(tick, side, slot);
                return;
            }

            Carry(tick, side, slot);
        }

        private void Carry(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);

            int targetX = _px[k] + dir * U.Units(_cfg.DribbleDistanceDm);
            int targetY = _py[k] + (U.CenterYU - _py[k]) / 6;
            int distance = U.Distance(_px[k], _py[k], U.ClampX(targetX), U.ClampY(targetY));

            // He knocks it ahead and runs onto it. The push has to be long enough that one
            // touch covers real ground: knocking it a couple of metres every other tick is the
            // same picture with the feed full of "dribble, dribble, dribble".
            _ball.Kick(side, slot, U.ClampX(targetX) - _px[k], U.ClampY(targetY) - _py[k],
                MatchBall.ForceForTicks(distance, _cfg.DribbleFlightTicks, _maxPassForce));

            int hold = HoldTicks(side);
            _hold[k] = hold < _cfg.DribbleFlightTicks ? _cfg.DribbleFlightTicks : hold;
            Record(tick, BallActionKind.Dribble, home, slot, -1);
        }

        private void Clear(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);

            int targetY = _py[k] < U.CenterYU ? U.Units(60) : U.WidthU - U.Units(60);
            int targetX = U.ClampX(_px[k] + dir * U.Units(400));

            _ball.Kick(side, slot, targetX - _px[k], targetY - _py[k], _maxPassForce);
            Release(tick, side, slot);
            Record(tick, BallActionKind.Clearance, home, slot, -1);
        }

        private void TakeRestart(int tick, int side, int slot)
        {
            _ball.Dead = false;
            Collect(side, slot);
            _hold[side * _n + slot] = 1;

            // A corner is swung into the box; everything else is played out normally.
            if (_deadKind == BallActionKind.Corner)
            {
                bool home = side == 0;
                int goalX = U.Units(MovementGeometry.AttackedGoalX(home));
                int aimX = goalX - MovementGeometry.Direction(home) * U.Units(90);
                int aimY = U.CenterYU + (_rng.NextInt(-120, 121) * U.Scale);
                int distance = U.Distance(_ball.X, _ball.Y, aimX, U.ClampY(aimY));
                _ball.Kick(side, slot, aimX - _ball.X, U.ClampY(aimY) - _ball.Y,
                    MatchBall.ForceForTicks(distance, 4, _maxPassForce));
                Record(tick, BallActionKind.Cross, home, slot, -1);
            }
        }

        /// <summary>
        /// Is this a strike anyone would recognise as one? The ball has to be alive, inside
        /// shooting range of the goal being attacked, and at the feet of the man about to hit
        /// it. Until all three hold the move is still being built and the chance waits.
        /// </summary>
        private bool Shootable(int side, int slot)
        {
            if (_ball.Dead) return false;
            if (_ball.OwnerSide >= 0 && _ball.OwnerSide != side) return false;

            int goalX = U.Units(MovementGeometry.AttackedGoalX(side == 0));
            if (U.Distance(_ball.X, _ball.Y, goalX, U.CenterYU) > U.Units(_cfg.ShootableRangeDm))
                return false;

            int k = side * _n + slot;
            return U.Distance(_px[k], _py[k], _ball.X, _ball.Y) <= _controlU;
        }

        private void TakeShot(int tick, int side, int slot, MatchEventType outcome, int creditSlot)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            int goalX = U.Units(MovementGeometry.AttackedGoalX(home));
            int half = U.Units(MovementGeometry.GoalHalfWidthDm);

            int aimX = goalX;
            int aimY;
            switch (outcome)
            {
                case MatchEventType.Goal:
                    aimY = U.CenterYU + _rng.NextInt(-(half - U.Units(8)), half - U.Units(7));
                    break;
                case MatchEventType.ChanceSaved:
                    // Straight at the keeper: the save is him getting a hand to it, not the
                    // ball rolling over his line while he watches.
                    int gk = (1 - side) * _n + KeeperOf(1 - side);
                    aimX = _px[gk];
                    aimY = _py[gk];
                    break;
                default:
                    int wide = _rng.NextInt(0, 2) == 0 ? -1 : 1;
                    aimY = U.CenterYU + wide * (half + _rng.NextInt(U.Units(12), U.Units(150)));
                    break;
            }

            aimY = U.ClampY(aimY);

            // Struck as hard as a shot is struck: it gets there, and it gets there quickly.
            // It leaves from the BALL, so nothing jumps; the striker is on it by construction.
            _ball.Kick(side, slot, aimX - _ball.X, aimY - _ball.Y, _maxShootForce);

            _shotLive = true;
            _shotHome = home;
            _shotSlot = creditSlot;          // the timeline's man: his name goes on it
            _shotOutcome = outcome;
            _shotExpires = tick + _cfg.ShotResolveTicks;
            Record(tick, BallActionKind.Shot, home, creditSlot, -1);
        }

        /// <summary>How long a ball should spend in the air: short ones snap, long ones travel.</summary>
        /// <summary>
        /// How long the ball should be in the air. Roughly a tick per seven metres, and then
        /// however many more it takes for a ball struck as hard as it can be struck to actually
        /// GET there: capping the flight instead used to reject every ball over about
        /// twenty-five metres, which left a side that could only pass sideways.
        /// </summary>
        private int FlightTicks(int distanceU)
        {
            int ticks = distanceU / U.Units(70) + 2;
            if (ticks < 2) ticks = 2;
            if (ticks > MaxFlightTicks) ticks = MaxFlightTicks;
            while (ticks < MaxFlightTicks && MatchBall.RangeInTicks(_maxPassForce, ticks) < distanceU)
                ticks++;
            return ticks;
        }

        private const int MaxFlightTicks = 12;

        private int HoldTicks(int side)
        {
            MovementTactics t = _tactics[side];
            int min = t.HoldTicksMin < 1 ? 1 : t.HoldTicksMin;
            int max = t.HoldTicksMax < min ? min : t.HoldTicksMax;
            int held = _rng.NextInt(min, max + 1);
            return _urgent[side] ? 1 : held;
        }

        private bool UnderPressure(int side, int slot)
        {
            int k = side * _n + slot;
            int opponent = 1 - side;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (U.Distance(_px[k], _py[k], _px[ok], _py[ok]) < U.Units(_cfg.PressureRadiusDm)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ passing

        /// <summary>
        /// The best pass is the one an opponent cannot reach and that leaves the ball as far
        /// forward as possible. Three targets are tried per team-mate — at him, and either
        /// side of him — because a ball played into his path is often on when a ball to his
        /// feet is not.
        /// </summary>
        private bool TryPass(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);
            int priority = _director.PriorityTarget(tick, home);

            int bestSlot = -1, bestX = 0, bestY = 0;
            long bestScore = long.MinValue;

            for (int j = 0; j < _n; j++)
            {
                if (j == slot) continue;
                int rk = side * _n + j;
                int gap = U.Distance(_px[k], _py[k], _px[rk], _py[rk]);
                if (gap < U.Units(_cfg.MinPassDm)) continue;
                if (gap > MatchBall.RangeOf(_maxPassForce)) continue;

                int lead = U.Units(_cfg.PassLeadDm);
                for (int option = 0; option < 3; option++)
                {
                    int tx = _px[rk], ty = _py[rk];
                    if (option == 1) tx += dir * lead;
                    else if (option == 2) ty += _py[rk] < U.CenterYU ? -lead : lead;

                    tx = U.ClampX(tx);
                    ty = U.ClampY(ty);

                    int distance = U.Distance(_px[k], _py[k], tx, ty);
                    if (distance < U.Units(_cfg.MinPassDm)) continue;

                    // Only a ball that ARRIVES about when it is meant to. Beyond that the force
                    // saturates, the pass is struck as hard as it can be and simply runs on —
                    // a fifty-metre ball nobody asked for.
                    int flight = FlightTicks(distance);
                    if (distance > MatchBall.RangeInTicks(_maxPassForce, flight)) continue;

                    int force = MatchBall.ForceForTicks(distance, flight, _maxPassForce);
                    bool safe = PassSafe(side, _px[k], _py[k], tx, ty, force);

                    // A ball to the man the chance belongs to is worth risking — that is what
                    // a team does when someone is in on goal, and without it the pass is never
                    // "on" and he has to be handed the ball at the last moment instead.
                    if (!safe && j != priority) continue;

                    int bias = _tactics[side].ForwardBias;
                    if (_urgent[side]) bias = bias * _cfg.ChanceForwardPercent / 100;
                    long score = (long)dir * (tx - _px[k]) / U.Scale * bias / 10;
                    score -= distance / U.Scale;                       // keep it sensible
                    if (!safe) score -= 300;                           // a risk, taken knowingly
                    if (_keeper[rk]) score -= 400;                     // only if nothing else is on
                    if (j == priority) score += 900;                   // the man the chance is for
                    if (AlreadySupporting(side, j)) score += 250;      // the man who made the run

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSlot = j;
                        bestX = tx;
                        bestY = ty;
                    }
                }
            }

            if (bestSlot < 0) return false;

            int passDistance = U.Distance(_px[k], _py[k], bestX, bestY);
            int passForce = MatchBall.ForceForTicks(passDistance, FlightTicks(passDistance), _maxPassForce);
            _ball.Kick(side, slot, bestX - _px[k], bestY - _py[k], passForce);
            Release(tick, side, slot);

            _receiver[side] = bestSlot;
            _receiveX[side] = bestX;
            _receiveY[side] = bestY;

            BallActionKind kind = passDistance > U.Units(_cfg.LongBallFromDm)
                ? BallActionKind.LongBall
                : IsCross(side, k) ? BallActionKind.Cross : BallActionKind.Pass;
            Record(tick, kind, home, slot, bestSlot);
            return true;
        }

        /// <summary>
        /// He has played it: for the next couple of ticks the ball is not his to pick up again.
        /// Without this a short pass is reclaimed by the man who struck it on the very next
        /// tick — the ball never travels, and what you watch is twenty-two men fidgeting.
        /// </summary>
        private void Release(int tick, int side, int slot)
        {
            _releasedBy = side * _n + slot;
            _releasedUntil = tick + _cfg.ReleaseLockTicks;
        }

        private bool IsCross(int side, int k)
        {
            bool home = side == 0;
            int goalX = U.Units(MovementGeometry.AttackedGoalX(home));
            bool wide = _py[k] < U.WidthU / 4 || _py[k] > U.WidthU * 3 / 4;
            return wide && U.Distance(_px[k], _py[k], goalX, U.CenterYU) < U.Units(Pitch.LengthDm / 3);
        }

        /// <summary>
        /// Can this ball be cut out? For each opponent, where he would meet the line of the
        /// pass, and whether he gets there before the ball does. Opponents behind the passer
        /// are ignored — they are chasing it, not intercepting it.
        /// </summary>
        private bool PassSafe(int side, int fromX, int fromY, int toX, int toY, int force)
        {
            int opponent = 1 - side;
            int dx = toX - fromX, dy = toY - fromY;
            int length = U.Length(dx, dy);
            if (length <= 0) return false;

            int receiverSpace = U.Units(_cfg.ReceiverSpaceDm);
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                long along = ((long)(_px[ok] - fromX) * dx + (long)(_py[ok] - fromY) * dy) / length;
                if (along <= 0) continue;                       // behind the ball: he is chasing, not cutting it out

                // The last stretch belongs to the receiver. A man marked at three metres can
                // still be passed to — he shields it, and his marker has to TACKLE him for it.
                // Judging that stretch as an interception makes every pass on the pitch
                // "unsafe" and the ball never leaves the first man who gets it.
                if (along > length - receiverSpace) continue;

                int meetX = fromX + (int)((long)dx * along / length);
                int meetY = fromY + (int)((long)dy * along / length);

                int ballTicks = MatchBall.TicksToCover((int)along, force);
                if (ballTicks >= MatchBall.Unreachable) return false;   // it will not even get there

                int reach = U.Distance(_px[ok], _py[ok], meetX, meetY) - _interceptU;
                if (reach < 0) return false;
                int manTicks = reach / _maxSpeed[ok] + _cfg.PassReactionTicks;

                if (manTicks <= ballTicks) return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ movement

        private void Move(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            bool sprint = false;
            int tx, ty;

            if (_ball.Dead && _deadSide == side && _deadTaker == slot)
            {
                tx = _ball.X;
                ty = _ball.Y;
                sprint = true;
            }
            else if (_ball.OwnerSide == side && _ball.OwnerSlot == slot)
            {
                // On the ball he goes FORWARD, drifting off the touchline toward the middle.
                // Sending him back to his position instead is how a defender ends up carrying
                // the ball into his own corner.
                tx = _px[k] + MovementGeometry.Direction(home) * U.Units(160);
                ty = _py[k] + (U.CenterYU - _py[k]) / 8;
                sprint = true;
            }
            else if (_keeper[k])
            {
                KeeperSpot(side, out tx, out ty);
            }
            else if (_ball.Free && !_ball.Dead && _receiver[side] == slot)
            {
                tx = _receiveX[side];
                ty = _receiveY[side];
                sprint = true;
            }
            else if (_ball.Free && !_ball.Dead && _chaser[side] == slot)
            {
                InterceptSpot(k, out tx, out ty);
                sprint = true;
            }
            else if (!_attacking[side] && Pressing(side, slot))
            {
                PressSpot(k, out tx, out ty);
                sprint = true;
            }
            else if (_director.PriorityTarget(tick, home) == slot)
            {
                // His chance is coming. While the ball is still being won back he comes short to
                // get involved; the moment it is up the pitch he stops showing for it and
                // attacks the space in front of goal, on the side the ball is coming from —
                // which is what puts the strike inside the box instead of on the halfway line.
                bool ballIsUp = home ? _ball.X > U.CenterXU : _ball.X < U.CenterXU;
                if (ballIsUp)
                {
                    int goalX = U.Units(MovementGeometry.AttackedGoalX(home));
                    int back = U.Units(_cfg.ChanceShotSpotDm);
                    tx = home ? goalX - back : goalX + back;
                    ty = U.CenterYU + (_ball.Y - U.CenterYU) / 2;
                }
                else
                {
                    HomeSpot(side, slot, attacking: true, out int hx, out int hy);
                    tx = hx + (_ball.X - hx) * _cfg.ChanceDropPercent / 100;
                    ty = hy + (_ball.Y - hy) * _cfg.ChanceDropPercent / 100;
                }
                sprint = true;
            }
            else if (_attacking[side] && AlreadySupporting(side, slot))
            {
                tx = _supportX[side];
                ty = _supportY[side];
                sprint = true;
            }
            else if (!_attacking[side] && _mark[k] >= 0)
            {
                MarkSpot(side, k, out tx, out ty);
            }
            else
            {
                HomeSpot(side, slot, _attacking[side], out tx, out ty);
            }

            Steer(k, U.ClampX(tx), U.ClampY(ty), sprint);
        }

        private void Steer(int k, int tx, int ty, bool sprint)
        {
            int top = sprint ? _maxSpeed[k] * _cfg.PlayerSprintPercent / 100 : _maxSpeed[k];
            if (top < 1) top = 1;

            int dx = tx - _px[k], dy = ty - _py[k];
            int distance = U.Length(dx, dy);
            int wanted = distance < top ? distance : top;
            U.Scaled(dx, dy, wanted, out int wx, out int wy);

            Separate(k, ref wx, ref wy, top);

            int ax = wx - _vx[k], ay = wy - _vy[k];
            U.Cap(ref ax, ref ay, _accel[k]);
            _vx[k] += ax;
            _vy[k] += ay;
            U.Cap(ref _vx[k], ref _vy[k], top);

            _px[k] = U.ClampX(_px[k] + _vx[k]);
            _py[k] = U.ClampY(_py[k] + _vy[k]);
        }

        /// <summary>Keeps team-mates off each other, so eleven men never stand in one heap.</summary>
        private void Separate(int k, ref int wx, ref int wy, int top)
        {
            int side = k / _n;
            int pushX = 0, pushY = 0;

            for (int j = 0; j < _n; j++)
            {
                int other = side * _n + j;
                if (other == k) continue;

                int dx = _px[k] - _px[other], dy = _py[k] - _py[other];
                int distance = U.Length(dx, dy);
                if (distance >= _separationU || distance <= 0) continue;

                int strength = (_separationU - distance) * top / _separationU;
                U.Scaled(dx, dy, strength, out int sx, out int sy);
                pushX += sx;
                pushY += sy;
            }

            wx += pushX * _cfg.SeparationStrengthPercent / 100;
            wy += pushY * _cfg.SeparationStrengthPercent / 100;
            U.Cap(ref wx, ref wy, top);
        }

        private void HomeSpot(int side, int slot, bool attacking, out int x, out int y)
        {
            int k = side * _n + slot;
            MovementTactics t = _tactics[side];
            bool home = side == 0;

            int permX = _baseX[k];
            if (!_keeper[k])
            {
                permX += attacking ? t.AttackShiftPermille : -t.DefendShiftPermille;
                if (attacking && _driving[side]) permX += _cfg.ChanceDriveShiftPermille;
            }

            int permY = 500 + (_baseY[k] - 500) * t.WidthPercent / 100;

            permX = MovementGeometry.Clamp(permX, 20, 970);
            permY = MovementGeometry.Clamp(permY, 30, 970);

            int px = permX * U.LengthU / 1000;
            int py = permY * U.WidthU / 1000;
            if (!home)
            {
                px = U.LengthU - px;
                py = U.WidthU - py;
            }

            // The whole block slides toward the ball's side of the pitch.
            py += (_ball.Y - py) * _cfg.BlockBallShiftPercent / 100;

            x = U.ClampX(px);
            y = U.ClampY(py);
        }

        private void KeeperSpot(int side, out int x, out int y)
        {
            bool home = side == 0;
            int goalX = U.Units(MovementGeometry.OwnGoalX(home));
            int dx = _ball.X - goalX;
            int distance = dx < 0 ? -dx : dx;

            int far = U.LengthU / 2;
            int reach = distance < far ? distance : far;
            int depth = U.Units(_cfg.KeeperDepthDm) + U.Units(_cfg.KeeperRushDm) * reach / far;

            x = home ? depth : U.LengthU - depth;
            y = U.CenterYU + (_ball.Y - U.CenterYU) * _cfg.KeeperLateralPercent / 100;
            y = MovementGeometry.Clamp(y, U.CenterYU - U.Units(110), U.CenterYU + U.Units(110));

            // In his own box and nearest to it, he comes and claims it.
            if (_ball.Free && !_ball.Dead
                && MovementGeometry.InOwnBox(home, U.Dm(_ball.X), U.Dm(_ball.Y))
                && NearestToBall(side, includeKeeper: true) == KeeperOf(side))
            {
                x = _ball.X;
                y = _ball.Y;
            }
        }

        private void InterceptSpot(int k, out int x, out int y)
        {
            // Run to where the ball will be, not to where it is.
            for (int ahead = 1; ahead <= 8; ahead++)
            {
                _ball.Future(ahead, out int bx, out int by);
                int reach = U.Distance(_px[k], _py[k], U.ClampX(bx), U.ClampY(by));
                if (reach <= _maxSpeed[k] * _cfg.PlayerSprintPercent / 100 * ahead)
                {
                    x = U.ClampX(bx);
                    y = U.ClampY(by);
                    return;
                }
            }

            _ball.Future(8, out int fx, out int fy);
            x = U.ClampX(fx);
            y = U.ClampY(fy);
        }

        private int SecondNearestToBall(int side, int excluded)
        {
            int best = -1, bestDistance = int.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (i == excluded || _keeper[k]) continue;
                int d = U.Distance(_px[k], _py[k], _ball.X, _ball.Y);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }

            return best;
        }

        private bool Pressing(int side, int slot)
        {
            if (_ball.OwnerSide == side || _ball.Dead) return false;

            int k = side * _n + slot;
            if (_chaser[side] != slot)
            {
                // A second man closes in while a chance is being built: one presser is easy to
                // play around, and the ball has to be won before his minute comes.
                if (!_urgent[side] || _keeper[k]) return false;
                if (_second[side] != slot) return false;
            }

            HomeSpot(side, slot, attacking: false, out int hx, out int hy);
            int reach = _tactics[side].PressReachU;
            if (_urgent[side]) reach = reach * _cfg.ChancePressPercent / 100;
            return U.Distance(hx, hy, _ball.X, _ball.Y) <= reach;
        }

        private void PressSpot(int k, out int x, out int y)
        {
            int dx = _px[k] - _ball.X, dy = _py[k] - _ball.Y;
            int distance = U.Length(dx, dy);
            if (distance <= 0)
            {
                x = _ball.X;
                y = _ball.Y;
                return;
            }

            int standOff = U.Units(_cfg.PressDistanceDm);
            int reach = distance < standOff ? distance : standOff;
            x = _ball.X + (int)((long)dx * reach / distance);
            y = _ball.Y + (int)((long)dy * reach / distance);
        }

        private void MarkSpot(int side, int k, out int x, out int y)
        {
            int opponent = 1 - side;
            int ok = opponent * _n + _mark[k];
            bool home = side == 0;
            int goalX = U.Units(MovementGeometry.OwnGoalX(home));

            // Goal-side of his man.
            int dx = goalX - _px[ok], dy = U.CenterYU - _py[ok];
            U.Scaled(dx, dy, U.Units(_cfg.MarkDistanceDm), out int gx, out int gy);
            x = _px[ok] + gx;
            y = _py[ok] + gy;
        }

        // ------------------------------------------------------------------ resolution

        /// <summary>
        /// A player gains the ball: it goes to HIS feet, this tick. Leaving it where it was
        /// and waiting for the next tick's glue is what puts the ball a couple of metres from
        /// the man the frame says is carrying it.
        /// </summary>
        private void Collect(int side, int slot)
        {
            _ball.Take(side, slot);
            _ball.X = _px[side * _n + slot];
            _ball.Y = _py[side * _n + slot];
        }

        private void ResolveControl(int tick)
        {
            if (_ball.Dead) return;

            if (!_ball.Free)
            {
                // The carrier keeps it glued to his feet; a challenge can still take it.
                int owner = _ball.OwnerSide * _n + _ball.OwnerSlot;
                _ball.X = _px[owner];
                _ball.Y = _py[owner];

                int taker = -1, takerSide = -1, best = int.MaxValue;
                int other = 1 - _ball.OwnerSide;
                for (int j = 0; j < _n; j++)
                {
                    int ok = other * _n + j;
                    int distance = U.Distance(_px[ok], _py[ok], _ball.X, _ball.Y);
                    if (distance < _controlU && distance < best)
                    {
                        best = distance;
                        taker = j;
                        takerSide = other;
                    }
                }

                int tacklePercent = _cfg.TacklePercentPerTick;
                if (taker >= 0 && _urgent[takerSide]) tacklePercent = tacklePercent * _cfg.ChanceTacklePercent / 100;

                if (taker >= 0 && tick >= _tackleLock && _rng.NextInt(0, 100) < tacklePercent)
                {
                    Collect(takerSide, taker);
                    _hold[takerSide * _n + taker] = HoldTicks(takerSide);
                    _receiver[takerSide] = -1;
                    _receiver[1 - takerSide] = -1;
                    _tackleLock = tick + _cfg.TackleLockTicks;
                    Record(tick, BallActionKind.Tackle, takerSide == 0, taker, -1);
                }

                return;
            }

            // A strike the timeline has already settled is nobody's to cut out: a goal is
            // going in, a miss is going wide, and a save belongs to the keeper it was aimed at.
            int keeperOnly = -1;
            if (_shotLive)
            {
                if (_shotOutcome != MatchEventType.ChanceSaved) return;
                keeperOnly = _shotHome ? 1 : 0;
            }

            // Loose ball: the nearest man inside control range takes it. The side whose chance
            // is coming reaches a stride further — the one thumb on the scale this engine
            // allows itself, and the reason the timeline's chance arrives with the ball at the
            // right feet instead of having to be handed to a man forty metres out.
            int bestSide = -1, bestSlot = -1, bestDistance = int.MaxValue;
            for (int side = 0; side < SideCount; side++)
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    if (keeperOnly >= 0 && (side != keeperOnly || !_keeper[k])) continue;

                    if (k == _releasedBy && tick < _releasedUntil) continue;

                    // The extra stride is for a ball nobody owns — a loose ball, a clearance, a
                    // ball cut out. It is NOT a licence to pluck a team-mate's pass out of the
                    // air a metre after it leaves his foot, which turns the move into a huddle.
                    bool ownPassInFlight = _receiver[side] >= 0 && _receiver[side] != i;
                    int reach = _controlU;
                    if (_urgent[side] && !_keeper[k] && !ownPassInFlight)
                        reach += U.Units(_cfg.ChanceReachBonusDm);

                    int distance = U.Distance(_px[k], _py[k], _ball.X, _ball.Y);
                    if (distance >= reach) continue;

                    // Scored on how far INSIDE his own reach he is, so the extra stride is a
                    // tie-break in a challenge rather than a licence to hoover up the pitch.
                    int slack = reach - distance;
                    if (bestSide < 0 || slack > bestDistance)
                    {
                        bestDistance = slack;
                        bestSide = side;
                        bestSlot = i;
                    }
                }

            if (bestSide < 0) return;

            bool wasShot = _shotLive;
            bool interception = _ball.LastTouchSide >= 0 && _ball.LastTouchSide != bestSide;

            Collect(bestSide, bestSlot);
            _hold[bestSide * _n + bestSlot] = HoldTicks(bestSide);
            _receiver[0] = -1;
            _receiver[1] = -1;
            if (interception) _tackleLock = tick + _cfg.TackleLockTicks;

            if (wasShot && _shotOutcome == MatchEventType.ChanceSaved && bestSide != (_shotHome ? 0 : 1))
            {
                _shotLive = false;
                Record(tick, BallActionKind.Save, bestSide == 0, bestSlot, -1);
                return;
            }

            if (!interception) return;

            // Not every ball a defender gets to is a ball he controls. Sometimes he just gets
            // something on it and it runs away — which is where loose balls, and the throw-ins
            // and corners that follow them, come from.
            if (_rng.NextInt(0, 100) < _cfg.DeflectPercent)
            {
                bool home = bestSide == 0;
                int dir = MovementGeometry.Direction(home);
                int awayY = _ball.Y < U.CenterYU ? -1 : 1;
                _ball.Kick(bestSide, bestSlot,
                    dir * U.Units(120) + _rng.NextInt(-60, 61) * U.Scale,
                    awayY * U.Units(_rng.NextInt(60, 240)),
                    _maxPassForce * _cfg.DeflectForcePercent / 100);
                _hold[bestSide * _n + bestSlot] = 0;
                Record(tick, BallActionKind.Clearance, home, bestSlot, -1);
                return;
            }

            Record(tick, BallActionKind.Interception, bestSide == 0, bestSlot, -1);
        }

        private void ResolveOutOfPlay(int tick)
        {
            if (_ball.Dead || !_ball.Free) return;

            int half = U.Units(MovementGeometry.GoalHalfWidthDm);

            // Nothing to resolve while the ball is still on the pitch. This test used to be
            // missing under the goal rule below, so every scripted goal was awarded on the tick
            // it was struck and the ball was snapped to the line from wherever it had been —
            // the strike had no flight at all, and from thirty metres out it read as a teleport.
            bool over = _ball.X < 0 || _ball.X > U.LengthU || _ball.Y < 0 || _ball.Y > U.WidthU;
            if (!over) return;

            // A strike the timeline calls a goal IS a goal the moment it leaves the pitch.
            // Judging it on the posts as well loses one in five of them to a goal kick, and a
            // goal the viewer never sees is the worst thing this engine can do.
            if (_shotLive && _shotOutcome == MatchEventType.Goal)
            {
                ForceResolveShot(tick);
                return;
            }

            // Over a touchline: a throw-in from the spot it actually crossed.
            if (_ball.Y < 0 || _ball.Y > U.WidthU)
            {
                int line = _ball.Y < 0 ? 0 : U.WidthU;
                _ball.CrossingOfY(line, out int outX);
                Restart(tick, BallActionKind.ThrowIn, 1 - _ball.LastTouchSide, U.ClampX(outX), line);
                return;
            }

            if (_ball.X >= 0 && _ball.X <= U.LengthU) return;

            bool overHomeGoal = _ball.X < 0;                 // the goal the HOME side defends
            int defending = overHomeGoal ? 0 : 1;
            int attacking = 1 - defending;
            int goalX = overHomeGoal ? 0 : U.LengthU;

            // Judged ON the goal line, not wherever the ball had got to by the end of the tick.
            _ball.CrossingOfX(goalX, out int crossY);
            bool betweenPosts = crossY > U.CenterYU - half && crossY < U.CenterYU + half;

            if (_shotLive && _shotOutcome == MatchEventType.ChanceMissed)
            {
                _shotLive = false;
                Record(tick, BallActionKind.Miss, attacking == 0, _shotSlot, -1);
            }
            else if (_shotLive && _shotOutcome == MatchEventType.ChanceSaved)
            {
                // Turned round the post or over the bar rather than gathered — still a save.
                _shotLive = false;
                Record(tick, BallActionKind.Save, defending == 0, KeeperOf(defending), -1);
            }
            else if (betweenPosts)
            {
                // On target but not a goal on the timeline: the keeper kept it out.
                _shotLive = false;
                Record(tick, BallActionKind.Save, defending == 0, KeeperOf(defending), -1);
            }

            if (_ball.LastTouchSide == defending)
                Restart(tick, BallActionKind.Corner, attacking, goalX, crossY < U.CenterYU ? 0 : U.WidthU);
            else
                Restart(tick, BallActionKind.GoalKick, defending,
                    overHomeGoal ? U.Units(55) : U.LengthU - U.Units(55), U.CenterYU);
        }

        /// <summary>
        /// A strike that neither crossed a line nor was gathered — it hit somebody, or pulled
        /// up short. It still has to become the thing the timeline says it was, or the ball
        /// stays "live" and nobody is allowed to touch it for the rest of the match.
        /// </summary>
        private void ResolveStuckShot(int tick)
        {
            if (!_shotLive || tick < _shotExpires) return;
            ForceResolveShot(tick);
        }

        private void ForceResolveShot(int tick)
        {
            if (!_shotLive) return;

            int attacking = _shotHome ? 0 : 1;
            int defending = 1 - attacking;
            _shotLive = false;

            switch (_shotOutcome)
            {
                case MatchEventType.Goal:
                    ScoreGoal(tick, attacking, defending);
                    break;

                case MatchEventType.ChanceSaved:
                    Record(tick, BallActionKind.Save, defending == 0, KeeperOf(defending), -1);
                    GoalKick(tick, defending);
                    break;

                default:
                    Record(tick, BallActionKind.Miss, _shotHome, _shotSlot, -1);
                    GoalKick(tick, defending);
                    break;
            }
        }

        /// <summary>
        /// The ball ends up IN THE NET and stays there while it is celebrated: a goal frame
        /// showing the ball already back on the centre spot is a goal the viewer never sees.
        /// The kickoff is set up when the celebration is over.
        /// </summary>
        private void ScoreGoal(int tick, int attacking, int defending)
        {
            int goalX = U.Units(MovementGeometry.AttackedGoalX(attacking == 0));
            Record(tick, BallActionKind.Goal, attacking == 0, _shotSlot, -1);

            _ball.Place(goalX, U.CenterYU);
            _ball.Dead = true;
            _ball.OwnerSide = -1;
            _ball.OwnerSlot = -1;
            _ball.LastTouchSide = attacking;
            _receiver[0] = -1;
            _receiver[1] = -1;

            _deadKind = BallActionKind.Goal;      // celebrating; the kickoff follows
            _deadSide = defending;
            _deadTaker = -1;
            _deadAt = tick + _cfg.GoalCelebrationTicks;
        }

        private void GoalKick(int tick, int defending)
        {
            int spot = defending == 0 ? U.Units(55) : U.LengthU - U.Units(55);
            Restart(tick, BallActionKind.GoalKick, defending, spot, U.CenterYU);
        }

        private void Restart(int tick, BallActionKind kind, int side, int x, int y)
        {
            _shotLive = false;
            _ball.Place(U.ClampX(x), U.ClampY(y));
            _ball.Dead = true;
            _ball.OwnerSide = -1;
            _ball.OwnerSlot = -1;
            _receiver[0] = -1;
            _receiver[1] = -1;

            _deadKind = kind;
            _deadSide = side;
            _deadTaker = kind == BallActionKind.GoalKick
                ? KeeperOf(side)
                : NearestTo(side, _ball.X, _ball.Y, includeKeeper: false);
            _deadAt = tick + _cfg.DeadBallTicks;

            Record(tick, kind, side == 0, _deadTaker, -1);
        }

        // ------------------------------------------------------------------ lookups

        private int NearestToBall(int side, bool includeKeeper) =>
            NearestTo(side, _ball.X, _ball.Y, includeKeeper);

        private int NearestTo(int side, int x, int y, bool includeKeeper)
        {
            int best = -1, bestDistance = int.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (_keeper[k] && !includeKeeper) continue;

                int distance = U.Distance(_px[k], _py[k], x, y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best >= 0 ? best : 0;
        }

        private int KeeperOf(int side)
        {
            for (int i = 0; i < _n; i++)
                if (_keeper[side * _n + i]) return i;
            return 0;
        }

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

        private void Record(int tick, BallActionKind kind, bool home, int slot, int target) =>
            _stream.Actions.Add(new BallAction(tick, kind, home, slot, target));

        // ------------------------------------------------------------------ output

        private void WriteFrame(int tick)
        {
            int bx = U.ClampX(_ball.X), by = U.ClampY(_ball.Y);
            _stream.BallXY[tick * 2] = U.Dm(bx);
            _stream.BallXY[tick * 2 + 1] = U.Dm(by);

            int baseIndex = tick * _n * 2;
            for (int i = 0; i < _n; i++)
            {
                _stream.HomeXY[baseIndex + i * 2] = U.Dm(_px[i]);
                _stream.HomeXY[baseIndex + i * 2 + 1] = U.Dm(_py[i]);
                _stream.AwayXY[baseIndex + i * 2] = U.Dm(_px[_n + i]);
                _stream.AwayXY[baseIndex + i * 2 + 1] = U.Dm(_py[_n + i]);
            }

            _stream.Owner[tick] = _ball.Free
                ? PositionStream.NoOwner
                : _stream.OwnerCode(_ball.OwnerSide == 0, _ball.OwnerSlot);
        }
    }
}
