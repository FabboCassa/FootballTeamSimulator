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
        private MatchBall _ball = null!;
        private MatchDirector _director = null!;
        private PositionStream _stream = new PositionStream();

        private int _n;
        private int[] _px = System.Array.Empty<int>();
        private int[] _py = System.Array.Empty<int>();
        private int[] _vx = System.Array.Empty<int>();
        private int[] _vy = System.Array.Empty<int>();
        private int[] _maxSpeed = System.Array.Empty<int>();
        private int[] _cruise = System.Array.Empty<int>();
        private int[] _accel = System.Array.Empty<int>();
        /// <summary>Which line of the block a man belongs to: 0 is the back line.</summary>
        private int[] _lineRank = System.Array.Empty<int>();

        /// <summary>Free positioning, as a displacement in decimetres from his own line.</summary>
        private int[] _offsetXDm = System.Array.Empty<int>();
        private int[] _baseY = System.Array.Empty<int>();   // permille across the pitch
        private int[] _forward = System.Array.Empty<int>();
        private bool[] _keeper = System.Array.Empty<bool>();
        private int[] _hold = System.Array.Empty<int>();
        private int[] _mark = System.Array.Empty<int>();

        /// <summary>Where each player was when he gained the ball, and whether his run has been called.</summary>
        private int[] _gotBallX = System.Array.Empty<int>();
        private int[] _gotBallY = System.Array.Empty<int>();
        private bool[] _runCalled = System.Array.Empty<bool>();

        private readonly MovementTactics[] _tactics = new MovementTactics[SideCount];
        private readonly bool[] _attacking = new bool[SideCount];

        // The block: one shape per side, worked out once a tick and read by everything that
        // asks where a man belongs. Absolute, already mirrored for the away side.
        private readonly int[] _lineCount = new int[SideCount];
        private readonly int[] _blockLineU = new int[SideCount];      // the back line
        private readonly int[] _blockSpacingU = new int[SideCount];   // step to the next line, signed
        private readonly int[] _blockWidthPercent = new int[SideCount];
        private readonly int[] _blockY = new int[SideCount];          // the block's centre across the pitch

        /// <summary>
        /// How far into its attacking shape a side is, in permille. A shape cannot teleport: a
        /// team that has just won the ball takes a few seconds to open up and a few more to
        /// close down again, so this walks toward its target instead of snapping to it. Without
        /// it every one of the several hundred turnovers a match moved twenty-two men six metres
        /// sideways and back, which is neither watchable nor payable in kilometres.
        /// </summary>
        private readonly int[] _expansion = new int[SideCount];

        /// <summary>The height the line is currently holding, so it steps instead of shuffling.</summary>
        private readonly int[] _blockLine = new int[SideCount];
        private readonly bool[] _blockSet = new bool[SideCount];
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

        private int _controlU, _kickU, _separationU, _interceptU, _arrivalU, _dribbleReportU;
        private int _approachU;
        private int _separationSq;
        private int _maxPassForce, _maxShootForce, _shootRangeU;
        private int _maxPassRange, _nominalStepU, _minFlightTicks, _maxFlightTicks;
        private int _streamStride, _lastFrame;

        /// <summary>Scratch for <see cref="AssignMarks"/>, so a hot loop allocates nothing.</summary>
        private bool[] _markTaken = System.Array.Empty<bool>();

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

                // The physics runs at 10 Hz; the replay does not need to (engine phase 1). Only
                // every StreamTicksPerFrame-th tick is written, which is what keeps the stream
                // the size of a replay instead of the size of the simulation.
                if (t % _streamStride == 0) WriteFrame(t / _streamStride);
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
            _streamStride = _cfg.StreamTicksPerFrame < 1 ? 1 : _cfg.StreamTicksPerFrame;
            _lastFrame = lastTick / _streamStride;
            int frames = _lastFrame + 1;

            _px = new int[total];
            _py = new int[total];
            _vx = new int[total];
            _vy = new int[total];
            _maxSpeed = new int[total];
            _cruise = new int[total];
            _accel = new int[total];
            _lineRank = new int[total];
            _offsetXDm = new int[total];
            _baseY = new int[total];
            _forward = new int[total];
            _keeper = new bool[total];
            _hold = new int[total];
            _mark = new int[total];
            _gotBallX = new int[total];
            _gotBallY = new int[total];
            _runCalled = new bool[total];

            _arrivalU = U.Units(_cfg.PlayerArrivalRadiusDm);
            _approachU = U.Units(_cfg.PlayerApproachDm);
            if (_approachU < 1) _approachU = 1;
            _dribbleReportU = U.Units(_cfg.DribbleReportDm);
            _controlU = U.Units(_cfg.ControlRadiusDm);
            _kickU = U.Units(_cfg.KickRangeDm);
            _separationU = U.Units(_cfg.SeparationRadiusDm);
            _separationSq = _separationU * _separationU;
            _interceptU = U.Units(_cfg.InterceptReachDm);
            _shootRangeU = U.Units(_cfg.MaxShootRangeDm);
            _markTaken = new bool[_n];

            // Speeds come off the config in decimetres per SECOND and are turned into units per
            // tick here — the one place the time base and the length unit meet (engine phase 1).
            _ball = new MatchBall(_cfg);
            _maxPassForce = U.PerTick(_cfg.MaxPassSpeedDmPerSecond, _cfg);
            _maxShootForce = U.PerTick(_cfg.MaxShootSpeedDmPerSecond, _cfg);
            _nominalStepU = U.PerTick(_cfg.NominalPassSpeedDmPerSecond, _cfg);
            if (_nominalStepU < 1) _nominalStepU = 1;
            _maxFlightTicks = _cfg.MaxFlightTicks;
            _minFlightTicks = _cfg.TicksOfMs(200);
            _maxPassRange = _ball.RangeOf(_maxPassForce);

            _tactics[0] = MovementTactics.From(tactics?.Home, _cfg);
            _tactics[1] = MovementTactics.From(tactics?.Away, _cfg);

            // On the centre spot before anybody takes up a position: the block is built around
            // the ball, and a ball still sitting at the origin drags all twenty-two men onto one
            // touchline for the kickoff frame.
            _ball.Place(U.CenterXU, U.CenterYU);

            // Kickoff: the home side takes it, so it is the side in possession. Both are needed
            // before the block can be built, and the block is needed before anybody can be
            // placed — hence the two passes below.
            _attacking[0] = true;
            _attacking[1] = false;
            _expansion[0] = 1000;
            _expansion[1] = 0;
            _deadKind = BallActionKind.Kickoff;
            _ball.Dead = true;

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

                    // His place in the SHAPE: which line he stands in, how far across it he
                    // stands, and — if the user has dragged him off his anchor (task 6.10) — how
                    // far off his own line he has been put. Free positioning is a displacement
                    // FROM the line rather than a position on the pitch, so a man moved forward
                    // ten metres stays ten metres in front of his line wherever the block goes.
                    _lineRank[k] = FormationGeometry.LineRank(roles, i, _cfg, out int lines);
                    _lineCount[side] = lines;
                    _baseY[k] = custom.HasValue ? custom.Value.YPermille : FormationGeometry.AnchorY(roles, i, _cfg);
                    _forward[k] = FormationGeometry.AnchorX(roles[i], _cfg);
                    _offsetXDm[k] = custom.HasValue
                        ? (custom.Value.XPermille - _forward[k]) * Pitch.LengthDm / 1000
                        : 0;
                    _keeper[k] = roles[i] == PositionRole.Goalkeeper;

                    // A footballer's top speed is 5.5 to 8.5 m/s and Pace spans that band; what he
                    // does off the ball is a jog, not a sprint, which is why cruising is a
                    // separate figure rather than "top speed unless sprinting".
                    int pace = slot.Player.Attributes.Pace;
                    int topDmPerSecond = _cfg.PlayerTopSpeedDmPerSecond + _cfg.PlayerTopSpeedPaceDmPerSecond * pace / 100;
                    _maxSpeed[k] = U.PerTick(topDmPerSecond, _cfg);
                    if (_maxSpeed[k] < 1) _maxSpeed[k] = 1;
                    _cruise[k] = _maxSpeed[k] * _cfg.PlayerCruisePercent / 100;
                    if (_cruise[k] < 1) _cruise[k] = 1;
                    _accel[k] = U.PerTickPerTick(_cfg.PlayerAccelDmPerSecond2, _cfg);
                    if (_accel[k] < 1) _accel[k] = 1;
                    _mark[k] = -1;
                }

                _chaser[side] = -1;
                _receiver[side] = -1;
                for (int s = 0; s < MaxSupporters; s++) _supporters[side * MaxSupporters + s] = -1;
            }

            for (int side = 0; side < SideCount; side++)
            {
                UpdateBlock(side);
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    HomeSpot(side, i, out int hx, out int hy);
                    _px[k] = hx;
                    _py[k] = hy;
                }
            }

            _director = new MatchDirector(report, home, away, _cfg, lastTick);

            _stream = new PositionStream
            {
                TicksPerMinute = _cfg.FramesPerMinute,
                PlayerCount = _n,
                LastTick = _lastFrame,
                BallXY = new int[frames * 2],
                HomeXY = new int[frames * _n * 2],
                AwayXY = new int[frames * _n * 2],
                Owner = new int[frames],
                HomePlayerIds = PlayerIds(home),
                AwayPlayerIds = PlayerIds(away),
                HomeShirts = ShirtNumbers.For(home),
                AwayShirts = ShirtNumbers.For(away)
            };

            // Kick off: the home side's most advanced man takes it from the centre spot.
            _ball.LastTouchSide = 1;
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

        /// <summary>
        /// Reading the game. Split in two on purpose (engine phase 1): who is nearest the ball
        /// changes every tenth of a second and is cheap, but who picks up whom, and where the
        /// supporting run goes, are DECISIONS — a defender does not repick his man ten times a
        /// second, and re-deciding at 10 Hz would cost fifty times what it costs at 2 Hz for a
        /// picture nobody could tell apart.
        /// </summary>
        private void UpdateTeams(int tick)
        {
            int brain = _cfg.TeamBrainTicks < 1 ? 1 : _cfg.TeamBrainTicks;
            _looseMan[0] = -1;
            _looseMan[1] = -1;
            for (int side = 0; side < SideCount; side++)
            {
                bool wasAttacking = _attacking[side];
                _attacking[side] = _ball.OwnerSide == side
                    || (_ball.Free && _ball.LastTouchSide == side);

                // A side that has just lost or won the ball re-reads the game at once, whatever
                // the cadence says: waiting half a second to notice would be a hole in the block.
                bool rethink = tick % brain == 0 || wasAttacking != _attacking[side];

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

                if (_attacking[side])
                {
                    if (rethink) UpdateSupport(tick, side);
                }
                else if (rethink)
                {
                    AssignMarks(side);
                }

                // The shape, once for the tick. Everything that asks where a man belongs reads
                // it; nothing recomputes it.
                UpdateBlock(side);
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
            // A destination, not a reflex: recomputed on its own slower clock, and only ever on
            // a tick the brain is already awake for, so the two cadences cannot drift apart.
            int period = _cfg.SupportRecalcTicks < 1 ? 1 : _cfg.SupportRecalcTicks;
            if (tick % period == 0 || _supportX[side] == 0)
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
            bool[] taken = _markTaken;
            for (int i = 0; i < _n; i++) taken[i] = false;

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
                _ball.ForceForTicks(distance, _cfg.DribbleFlightTicks, _maxPassForce));

            int hold = HoldTicks(side);
            _hold[k] = hold < _cfg.DribbleFlightTicks ? _cfg.DribbleFlightTicks : hold;

            // Called once per possession, and only once he has actually run with it. Logging
            // every touch turned the commentary into "dribble, dribble, dribble" and filled the
            // stream with a thousand entries a match that nobody would name.
            if (!_runCalled[k]
                && U.DistanceSq(_px[k], _py[k], _gotBallX[k], _gotBallY[k]) >= (long)_dribbleReportU * _dribbleReportU)
            {
                _runCalled[k] = true;
                Record(tick, BallActionKind.Dribble, home, slot, -1);
            }
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
                    _ball.ForceForTicks(distance, _cfg.TicksOfMs(1200), _maxPassForce));
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

        /// <summary>
        /// How long the ball should be in the air: the time an ordinary pass takes at the speed
        /// an ordinary pass travels (engine phase 1 — before it, this was a tick per seven
        /// metres, which at five seconds a tick meant a twenty-metre pass took fifteen seconds).
        /// Then however many ticks more it takes for a ball struck as hard as it can be struck
        /// to actually GET there, because a flight the ball cannot make is a pass never played.
        /// </summary>
        private int FlightTicks(int distanceU)
        {
            int ticks = distanceU / _nominalStepU;
            if (ticks < _minFlightTicks) ticks = _minFlightTicks;
            if (ticks > _maxFlightTicks) ticks = _maxFlightTicks;
            while (ticks < _maxFlightTicks && _ball.RangeInTicks(_maxPassForce, ticks) < distanceU)
                ticks++;
            return ticks;
        }

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
            long radius = U.Units(_cfg.PressureRadiusDm);
            long radiusSq = radius * radius;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (U.DistanceSq(_px[k], _py[k], _px[ok], _py[ok]) < radiusSq) return true;
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
                if (gap > _maxPassRange) continue;

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
                    if (distance > _ball.RangeInTicks(_maxPassForce, flight)) continue;

                    int force = _ball.ForceForTicks(distance, flight, _maxPassForce);
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
            int passForce = _ball.ForceForTicks(passDistance, FlightTicks(passDistance), _maxPassForce);
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

                int reach = U.Distance(_px[ok], _py[ok], meetX, meetY) - _interceptU;
                if (reach < 0) return false;

                // Asked the other way round — how far has the ball got by the time he is there —
                // this is one lookup in the ball's roll table instead of a search for the tick on
                // which it arrives. Same question, and it is asked for every opponent on every
                // candidate pass, which at 10 Hz is where the passing model's cost lives.
                int manTicks = reach / _maxSpeed[ok] + _cfg.PassReactionTicks;
                if (_ball.RangeInTicks(force, manTicks) < along) return false;
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

            // Filled by the press test below whenever this side is defending; the compiler
            // cannot see that the branch which reads them is the same branch that sets them.
            int pressHomeX = 0, pressHomeY = 0;

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
            else if (!_attacking[side] && Pressing(side, slot, out pressHomeX, out pressHomeY))
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
                    HomeSpot(side, slot, out int hx, out int hy);
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
            else if (!_attacking[side])
            {
                // Already worked out by the press test above; recomputing it is pure waste.
                tx = pressHomeX;
                ty = pressHomeY;
            }
            else
            {
                HomeSpot(side, slot, out tx, out ty);
            }

            Steer(k, U.ClampX(tx), U.ClampY(ty), sprint);
        }

        private void Steer(int k, int tx, int ty, bool sprint)
        {
            // Flat out only when the ball is the reason; otherwise a jog. A match where every
            // man ran at eight metres a second for ninety minutes would cover forty kilometres.
            int top = sprint ? _maxSpeed[k] : _cruise[k];
            if (top < 1) top = 1;

            // Within one step of the target the vector IS the step, so no root is needed: this
            // is the common case for the twenty players who are not chasing anything.
            int dx = tx - _px[k], dy = ty - _py[k];
            long gap = (long)dx * dx + (long)dy * dy;
            int wx, wy;

            // He WALKS to a place a few metres away and jogs to one across the pitch. Setting
            // off at a constant jog for a five-metre correction is a kilometre a match of
            // running no footballer does, and it also means a man chasing a spot that jitters
            // can never settle: at walking pace he averages the jitter out instead, which is
            // what actually happens on a pitch (engine phase 2).
            if (!sprint && gap < (long)_approachU * _approachU)
            {
                int near = U.Length(dx, dy);
                int paced = top * near / _approachU;
                if (paced < 1) paced = 1;
                top = paced;
            }

            if (!sprint && gap <= (long)_arrivalU * _arrivalU)
            {
                // Arrived. A footballer standing in position stands in it; chasing a spot that
                // drifts with the ball ten times a second is how the eleven of them walked
                // sixteen kilometres a match. It is a deadband on REPOSITIONING only — a man
                // going for the ball goes all the way to it, or nobody ever collects it.
                wx = 0;
                wy = 0;
            }
            else if (gap <= (long)top * top)
            {
                wx = dx;
                wy = dy;
            }
            else
            {
                U.Scaled(dx, dy, top, out wx, out wy);
            }

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
                long sq = (long)dx * dx + (long)dy * dy;
                if (sq >= _separationSq || sq <= 0) continue;   // no root for the pairs that are far apart
                int distance = U.Length(dx, dy);
                if (distance <= 0) continue;

                int strength = (_separationU - distance) * top / _separationU;
                U.Scaled(dx, dy, strength, out int sx, out int sy);
                pushX += sx;
                pushY += sy;
            }

            wx += pushX * _cfg.SeparationStrengthPercent / 100;
            wy += pushY * _cfg.SeparationStrengthPercent / 100;
            U.Cap(ref wx, ref wy, top);
        }

        /// <summary>
        /// The block, for one side, for both phases — worked out once a tick (engine phase 2).
        ///
        /// A team is a shape with three separate properties: WHERE it is, how DEEP it is and how
        /// WIDE it is. Where it is has exactly one degree of freedom along the pitch, the height
        /// of the back line, because that is the line a coach actually instructs — "hold on the
        /// halfway line", "drop off" — and every other line is spaced forward off it, which is
        /// what makes the back four a line by construction instead of by luck. How deep and how
        /// wide follow from one thing: whether the team has the ball.
        ///
        /// The asymmetry of the shift falls out of measuring the spacing FORWARD from the back
        /// line: losing the ball drops the striker forty-odd metres and moves his centre-backs
        /// fifteen, which is what a team collapsing into its block looks like.
        /// </summary>
        private void UpdateBlock(int side)
        {
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);
            MovementTactics t = _tactics[side];
            int lines = _lineCount[side];

            // The ball's depth measured from THIS side's own goal, so one formula serves both
            // sides and neither has a mirrored copy of the rule.
            int ballDepth = home ? U.Dm(_ball.X) : Pitch.LengthDm - U.Dm(_ball.X);

            // Toward the attacking shape, or back toward the defensive one, a step at a time.
            int step = 1000 / _cfg.ShapeTransitionTicks;
            if (step < 1) step = 1;
            int e = _expansion[side];
            if (_attacking[side]) { e += step; if (e > 1000) e = 1000; }
            else { e -= step; if (e < 0) e = 0; }
            _expansion[side] = e;

            // Law 8: a kickoff is taken with both sides in their own half. The shape is squeezed
            // back behind the halfway line until the ball is in play — the taker is on the ball
            // and exempt.
            bool kickoff = _ball.Dead && _deadKind == BallActionKind.Kickoff;

            // Where the line holds, as a function of where the ball is — and NOT one for one. A
            // line that followed the ball metre for metre would spend the match chasing a ball
            // being passed up and down the pitch, which is two kilometres a man of running that
            // no defender does. It is anchored near its own goal and pulled up by the ball,
            // which is what a defender's two jobs — protect the goal, squeeze the space in front
            // of it — add up to.
            int line = _cfg.BackLineMinDepthDm
                     + (ballDepth - _cfg.BackLineBallLagDm) * _cfg.BackLineBallFollowPercent / 100
                     + t.LinePushDm
                     + _cfg.PossessionLinePushDm * e / 1000;
            if (_driving[side]) line += _cfg.ChanceDriveShiftPermille * Pitch.LengthDm * e / 1000_000;
            line = MovementGeometry.Clamp(line, _cfg.BackLineMinDepthDm, _cfg.BackLineMaxDepthDm);

            int spacing = kickoff
                ? _cfg.LineSpacingDm
                : _cfg.LineSpacingDm * (100 + (_cfg.AttackLineSpacingPercent - 100) * e / 1000) / 100;

            // How much room the front line has. Without a ceiling the most advanced line ends up
            // standing on the goal line instead of on the edge of the box.
            int reach = kickoff
                ? Pitch.CenterX - _cfg.KickoffHalfwayGapDm
                : Pitch.LengthDm - _cfg.FrontLineGoalGapDm;

            if (lines > 1 && line + (lines - 1) * spacing > reach)
            {
                // Behind the halfway line at a kickoff the whole block moves back; in open play
                // the line stays where the game put it and the shape compresses in front of it.
                if (kickoff) line = reach - (lines - 1) * spacing;
                else spacing = (reach - line) / (lines - 1);
            }

            if (line < 0) line = 0;
            if (spacing < 0) spacing = 0;

            // And the line STEPS. It holds its height, and when the game has genuinely moved it
            // moves as a unit — which is both what a back four does and the difference between
            // ten men covering eleven kilometres in a match and covering thirteen. Filtering the
            // BALL instead of the line does not do it: the ball crosses any deadband every
            // second or two, so the line ends up creeping after it a metre at a time.
            int held = _blockLine[side];
            int drift = line - held;
            if (drift < 0) drift = -drift;
            if (!_blockSet[side] || drift > _cfg.BackLineHoldDm)
            {
                held = line;
                _blockSet[side] = true;
            }

            _blockLine[side] = held;
            line = held;

            _blockLineU[side] = U.Units(home ? line : Pitch.LengthDm - line);
            _blockSpacingU[side] = dir * U.Units(spacing);
            _blockWidthPercent[side] = t.WidthPercent
                * (_cfg.DefendWidthPercent
                   + (_cfg.AttackWidthPercent - _cfg.DefendWidthPercent) * e / 1000) / 100;

            // The shape slides toward the ball's side of the pitch, as one piece and no further
            // than the cap. What this replaced was a per-player lerp toward the ball's Y: the man
            // furthest from the ball moved MORE than the man nearest it, so the team did not
            // shift across, it collapsed into the ball's channel (§1.4).
            int shift = U.Dm(_ball.Y) - Pitch.CenterY;
            int max = _cfg.BlockLateralShiftMaxDm;
            if (shift < -max) shift = -max;
            else if (shift > max) shift = max;
            _blockY[side] = U.Units(Pitch.CenterY + shift);
        }

        /// <summary>
        /// Where a man stands when the game is asking nothing else of him: his place IN THE
        /// BLOCK. Not a point on the pitch he owns — a point in a shape, which is why all this
        /// does is take the block's line and its width and add his own offset within them.
        /// </summary>
        private void HomeSpot(int side, int slot, out int x, out int y)
        {
            int k = side * _n + slot;
            if (_keeper[k])
            {
                KeeperSpot(side, out x, out y);
                return;
            }

            int dir = MovementGeometry.Direction(side == 0);
            int px = _blockLineU[side] + _lineRank[k] * _blockSpacingU[side] + dir * U.Units(_offsetXDm[k]);

            // His distance from the middle is a SHARE of the block's, so the shape squeezes and
            // stretches as one piece instead of eleven men each widening on their own.
            int spreadDm = (_baseY[k] - 500) * Pitch.WidthDm / 1000;
            int py = _blockY[side] + dir * U.Units(spreadDm) * _blockWidthPercent[side] / 100;

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
            // Run to where the ball will be, not to where it is. The roll is stepped forward once
            // rather than replayed from scratch for every horizon, which at 10 Hz matters.
            int bx = _ball.X, by = _ball.Y, vx = _ball.Vx, vy = _ball.Vy;
            int horizon = _cfg.InterceptLookaheadTicks;

            for (int ahead = 1; ahead <= horizon; ahead++)
            {
                _ball.StepPrediction(ref bx, ref by, ref vx, ref vy);
                int reach = U.Distance(_px[k], _py[k], U.ClampX(bx), U.ClampY(by));
                if (reach <= (long)_maxSpeed[k] * ahead)
                {
                    x = U.ClampX(bx);
                    y = U.ClampY(by);
                    return;
                }
            }

            x = U.ClampX(bx);
            y = U.ClampY(by);
        }

        private int SecondNearestToBall(int side, int excluded)
        {
            int best = -1;
            long bestDistance = long.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (i == excluded || _keeper[k]) continue;
                long d = U.DistanceSq(_px[k], _py[k], _ball.X, _ball.Y);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }

            return best;
        }

        /// <summary>
        /// Is this man going to the ball? Hands back his defensive home spot either way, because
        /// the caller needs it whichever answer it gets and it is not cheap enough to work out
        /// twice for eleven men, ten times a second.
        /// </summary>
        private bool Pressing(int side, int slot, out int homeX, out int homeY)
        {
            int k = side * _n + slot;
            HomeSpot(side, slot, out homeX, out homeY);

            if (_ball.OwnerSide == side || _ball.Dead) return false;

            if (_chaser[side] != slot)
            {
                // A second man closes in while a chance is being built: one presser is easy to
                // play around, and the ball has to be won before his minute comes.
                if (!_urgent[side] || _keeper[k]) return false;
                if (_second[side] != slot) return false;
            }

            long reach = _tactics[side].PressReachU;
            if (_urgent[side]) reach = reach * _cfg.ChancePressPercent / 100;
            return U.DistanceSq(homeX, homeY, _ball.X, _ball.Y) <= reach * reach;
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

            // A man on the back line HOLDS THE LINE (engine phase 2). He picks his opponent up
            // across the pitch, but he does not follow him up and down it: four defenders each
            // tracking his own man's depth is exactly how a back four stops being a line and
            // becomes four separate duels — the fourteen metres of "back line spread" the
            // harness was reading. He leaves the line only for a man who has already got behind
            // it, which is the one thing the line exists to deal with.
            if (_lineRank[k] != 0) return;

            int lineU = _blockLineU[side];
            int lineDepth = home ? lineU : U.LengthU - lineU;
            int manDepth = home ? _px[ok] : U.LengthU - _px[ok];
            if (manDepth >= lineDepth) x = lineU;
        }

        // ------------------------------------------------------------------ resolution

        /// <summary>
        /// A player gains the ball: it goes to HIS feet, this tick. Leaving it where it was
        /// and waiting for the next tick's glue is what puts the ball a couple of metres from
        /// the man the frame says is carrying it.
        /// </summary>
        private void Collect(int side, int slot)
        {
            int k = side * _n + slot;

            // A man running onto his own knock-on has not started a new possession, he is still
            // running with the ball — so the run keeps its starting point instead of being
            // rebased every touch, which is what made a fifty-metre surge look like six touches.
            bool sameMan = _ball.LastTouchSide == side && _ball.LastTouchSlot == slot;

            _ball.Take(side, slot);
            _ball.X = _px[k];
            _ball.Y = _py[k];
            if (!sameMan)
            {
                _gotBallX[k] = _px[k];
                _gotBallY[k] = _py[k];
                _runCalled[k] = false;
            }
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

                int taker = -1, takerSide = -1;
                long best = (long)_controlU * _controlU;
                int other = 1 - _ball.OwnerSide;
                for (int j = 0; j < _n; j++)
                {
                    int ok = other * _n + j;
                    long distance = U.DistanceSq(_px[ok], _py[ok], _ball.X, _ball.Y);
                    if (distance < best)
                    {
                        best = distance;
                        taker = j;
                        takerSide = other;
                    }
                }

                int tacklePermille = _cfg.TackleChancePermillePerTick;
                if (taker >= 0 && _urgent[takerSide]) tacklePermille = tacklePermille * _cfg.ChanceTacklePercent / 100;

                if (taker >= 0 && tick >= _tackleLock && _rng.NextInt(0, 1000) < tacklePermille)
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
                // A live strike that runs out over the SIDE still has to become the thing the
                // timeline says it was. Restart() clears the strike silently, so before phase 1
                // caught it a shot that drifted wide lost its save or its miss altogether — the
                // chance was on the scoresheet and nowhere in the picture. It costs nothing when
                // no strike is live, and it cannot swallow a goal: a goal crosses a GOAL line.
                SettleStrayStrike(tick);

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
        /// A strike that has left the pitch anywhere but through a goal: it is called as the
        /// save or the miss the timeline made it, and the restart follows.
        /// </summary>
        private void SettleStrayStrike(int tick)
        {
            if (!_shotLive) return;

            int attacking = _shotHome ? 0 : 1;
            int defending = 1 - attacking;
            _shotLive = false;

            if (_shotOutcome == MatchEventType.ChanceSaved)
                Record(tick, BallActionKind.Save, defending == 0, KeeperOf(defending), -1);
            else
                Record(tick, BallActionKind.Miss, _shotHome, _shotSlot, -1);
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
            int best = -1;
            long bestDistance = long.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (_keeper[k] && !includeKeeper) continue;

                long distance = U.DistanceSq(_px[k], _py[k], x, y);
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

        /// <summary>
        /// Files what just happened to the ball, at the FRAME it belongs to. The simulation runs
        /// at 10 Hz and the stream is written at 2 Hz (engine phase 1), so an action's tick has
        /// to be mapped into frame space here — otherwise every consumer of the stream (the
        /// renderer, the analyzer, the tests) would need two clocks and a conversion rule.
        ///
        /// Rounded UP, to the first frame at or AFTER the action, and that is not a detail: a
        /// frame is half a second, and a shot struck at thirty metres a second covers fifteen
        /// metres in one. Filed on the frame BEFORE, a goal is drawn with the ball still short of
        /// the line and a pass with the ball still at the passer's feet — the commentary would be
        /// announcing things the picture had not done yet. Rounding up is monotone, so the order
        /// the actions happened in is kept either way.
        /// </summary>
        private void Record(int tick, BallActionKind kind, bool home, int slot, int target)
        {
            int frame = (tick + _streamStride - 1) / _streamStride;
            if (frame > _lastFrame) frame = _lastFrame;
            _stream.Actions.Add(new BallAction(frame, kind, home, slot, target));
        }

        // ------------------------------------------------------------------ output

        /// <summary>Writes one stream frame. <paramref name="frame"/> is in FRAME space, not tick space.</summary>
        private void WriteFrame(int frame)
        {
            int bx = U.ClampX(_ball.X), by = U.ClampY(_ball.Y);
            _stream.BallXY[frame * 2] = U.Dm(bx);
            _stream.BallXY[frame * 2 + 1] = U.Dm(by);

            int baseIndex = frame * _n * 2;
            for (int i = 0; i < _n; i++)
            {
                _stream.HomeXY[baseIndex + i * 2] = U.Dm(_px[i]);
                _stream.HomeXY[baseIndex + i * 2 + 1] = U.Dm(_py[i]);
                _stream.AwayXY[baseIndex + i * 2] = U.Dm(_px[_n + i]);
                _stream.AwayXY[baseIndex + i * 2 + 1] = U.Dm(_py[_n + i]);
            }

            _stream.Owner[frame] = _ball.Free
                ? PositionStream.NoOwner
                : _stream.OwnerCode(_ball.OwnerSide == 0, _ball.OwnerSlot);
        }
    }
}
