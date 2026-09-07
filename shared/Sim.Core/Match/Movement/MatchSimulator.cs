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

        // Where a man's step STARTED and where it would have ENDED had the pitch not stopped him
        // (engine phase 5). A player is clamped back inside the touchlines, and the ball at his
        // feet was clamped with him — which is the whole of the §1.7 defect: the laws say a ball
        // a player is holding is out the moment it crosses the line, and the engine only ever
        // tested a FREE ball. The unclamped step is what the referee reads to find the point the
        // ball actually crossed, sub-tick, exactly as MatchBall.CrossingOf* does for a loose one.
        private int[] _stepFromX = System.Array.Empty<int>();
        private int[] _stepFromY = System.Array.Empty<int>();
        private int[] _stepToX = System.Array.Empty<int>();
        private int[] _stepToY = System.Array.Empty<int>();
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

        // One duty per man per brain tick, for the side without the ball (engine phase 3).
        // Going to the ball is decided every tick and is not stored here; these are the three
        // standing jobs: cover the presser, pick a man up, or hold your place in the block.
        private const int DutyZone = 0;
        private const int DutyCover = 1;
        private const int DutyMark = 2;
        private int[] _duty = System.Array.Empty<int>();

        /// <summary>Until when a ball played backwards keeps the opposing press switched on.</summary>
        private readonly int[] _pressUntil = new int[SideCount];

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

        /// <summary>
        /// Until this tick the man who has just put the ball back in play cannot be judged to
        /// have carried it out again. A throw-in is taken FROM the touchline, so the taker and
        /// the ball are both standing on a line the moment he collects it.
        /// </summary>
        private int _restartGrace;
        private int _restartTaker = -1;

        /// <summary>The tick the first half ends on, and which side kicked the match off.</summary>
        private int _halfTime;
        private int _kickedOffFirst;
        private bool _secondHalf;

        // ------------------------------------------------------------------ the referee (phase 5)

        /// <summary>
        /// Law 11, as the assistant referee keeps it: at the moment a ball is played, which of
        /// the passing side's men were beyond the offside line. The flag only matters if one of
        /// them then plays the ball, so it is raised here and answered in
        /// <see cref="ResolveControl"/> — and it is wiped by the next touch, whoever takes it.
        /// </summary>
        /// <summary>Sent off (a second yellow or a straight red): he leaves the field of play.</summary>
        private bool[] _sentOff = System.Array.Empty<bool>();
        private bool[] _booked = System.Array.Empty<bool>();
        private readonly int[] _tenMen = new int[SideCount];

        /// <summary>
        /// In the wall, this tick. A wall is made of men standing on each other's shoulders, so the
        /// one thing that must not apply to them is the separation that keeps team-mates apart — at
        /// a six-metre radius it opens the wall before it has finished forming, which is exactly
        /// what the eye caught in the replay dump.
        /// </summary>
        private bool[] _inWall = System.Array.Empty<bool>();

        /// <summary>The lineup slots making up the wall at the current free kick, or -1.</summary>
        private readonly int[] _wall = { -1, -1, -1, -1, -1 };

        private bool[] _flagged = System.Array.Empty<bool>();
        private int[] _flaggedX = System.Array.Empty<int>();
        private int[] _flaggedY = System.Array.Empty<int>();
        private bool _anyFlag;

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
        private int _foeSeparationU;
        private int _recoveryU;
        private long _foeSeparationSq;
        private int _approachU;
        private int _separationSq;
        private int _maxPassForce, _maxShootForce, _shootRangeU;
        private int _maxPassRange;
        private int _arrivalStepU, _carryArrivalU;
        private int _streamStride, _lastFrame;

        /// <summary>Scratch for <see cref="AssignDuties"/>, so a hot loop allocates nothing.</summary>
        private bool[] _markTaken = System.Array.Empty<bool>();

        // What a player is worth once the ball is at his feet (engine phase 4). Cached off the
        // lineup at setup because Act reaches for them on every decision and walking back to
        // Lineup.Slots[i].Player.Attributes ten times a tick is pure waste.
        private int[] _skPassing = System.Array.Empty<int>();
        private int[] _skTechnique = System.Array.Empty<int>();
        private int[] _skDribbling = System.Array.Empty<int>();
        private int[] _skDefending = System.Array.Empty<int>();
        private int[] _skPositioning = System.Array.Empty<int>();
        private int[] _skStrength = System.Array.Empty<int>();
        private int[] _skShooting = System.Array.Empty<int>();
        private int[] _skGoalkeeping = System.Array.Empty<int>();
        private int[] _skPace = System.Array.Empty<int>();
        private int[] _vision = System.Array.Empty<int>();

        /// <summary>How good the strike in the air was, for the keeper who has to deal with it.</summary>
        private int _shotQuality;
        private int _pressureU;

        /// <summary>
        /// How far inside the touchline the shape, a pass and a run with the ball all stay
        /// (engine phase 5). A footballer's POSITION is never the line itself: he stands a stride
        /// inside it, because standing on it puts half of him off the pitch. Until this phase the
        /// widest man in an attacking block was simply clamped onto the touchline, and a ball
        /// played to him sat there in his feet for seconds at a time — which is most of what the
        /// harness was counting as "a held ball on a line of the pitch".
        /// </summary>
        private int _insetU;

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

            // HALF TIME (Law 7). The whistle at the end of the first half, and the second half
            // kicked off from the centre spot by the side that did NOT kick off the first — both
            // sides behind the halfway line, which the block already knows how to do for a
            // kickoff. The ENDS are not swapped: see the note on MatchBalance.HalfTimeMs.
            // The whistle WAITS: not while a strike is in the air, and not while the timeline has a
            // chance due — a referee does not blow for half-time with the ball in the box, and the
            // director would un-dead the ball for the strike and cancel the restart if he did.
            if (!_secondHalf && tick >= _halfTime && !_shotLive && !_director.ShotDue(tick, out _))
            {
                _secondHalf = true;
                HalfTime(tick);
            }

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

        /// <summary>
        /// The interval. Everything in flight is settled, the ball goes back to the centre spot,
        /// and the second half is kicked off by the other side after a pause — which is the whole
        /// of Law 7 that has a consequence on the pitch.
        ///
        /// CHANGING ENDS is deliberately NOT modelled, and that is a considered decision rather
        /// than an omission: the pitch is symmetric, home advantage is a strength bonus and not a
        /// place, and every part of this model already carries its own attacking direction
        /// (MovementGeometry.Direction). Flipping the two sides at half-time would therefore change
        /// nothing about the football and would oblige every consumer of the stream — the analyzer,
        /// the HTML dump, the client's renderer and its goal labels — to flip back. If the picture
        /// ever wants it, it belongs in the renderer, as a mirror of the second half's frames.
        /// </summary>
        private void HalfTime(int tick)
        {
            if (_shotLive) ForceResolveShot(tick);

            ClearFlags();
            _receiver[0] = -1;
            _receiver[1] = -1;
            _releasedBy = -1;
            _releasedUntil = -1;
            _restartTaker = -1;
            _tackleLock = tick;

            Record(tick, BallActionKind.HalfTime, _kickedOffFirst == 0, -1, -1);

            int kicking = 1 - _kickedOffFirst;
            _ball.Place(U.CenterXU, U.CenterYU);
            _ball.Dead = true;
            _ball.OwnerSide = -1;
            _ball.OwnerSlot = -1;
            _ball.LastTouchSide = 1 - kicking;

            _deadKind = BallActionKind.Kickoff;
            _deadSide = kicking;
            _deadTaker = MostAdvanced(kicking);
            _deadAt = tick + _cfg.HalfTimeTicks;
            Record(tick, BallActionKind.Kickoff, kicking == 0, _deadTaker, -1);
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
            _inWall = new bool[total];
            _sentOff = new bool[total];
            _booked = new bool[total];
            _tenMen[0] = 0;
            _tenMen[1] = 0;
            _flagged = new bool[total];
            _flaggedX = new int[total];
            _flaggedY = new int[total];
            _stepFromX = new int[total];
            _stepFromY = new int[total];
            _stepToX = new int[total];
            _stepToY = new int[total];
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
            _duty = new int[total];
            _pressUntil[0] = -1;
            _pressUntil[1] = -1;
            _gotBallX = new int[total];
            _gotBallY = new int[total];
            _runCalled = new bool[total];
            _skPassing = new int[total];
            _skTechnique = new int[total];
            _skDribbling = new int[total];
            _skDefending = new int[total];
            _skPositioning = new int[total];
            _skStrength = new int[total];
            _skShooting = new int[total];
            _skGoalkeeping = new int[total];
            _skPace = new int[total];
            _vision = new int[total];

            _arrivalU = U.Units(_cfg.PlayerArrivalRadiusDm);
            _approachU = U.Units(_cfg.PlayerApproachDm);
            if (_approachU < 1) _approachU = 1;
            _dribbleReportU = U.Units(_cfg.DribbleReportDm);
            _controlU = U.Units(_cfg.ControlRadiusDm);
            _kickU = U.Units(_cfg.KickRangeDm);
            _separationU = U.Units(_cfg.SeparationRadiusDm);
            _separationSq = _separationU * _separationU;
            _recoveryU = U.Units(_cfg.RecoveryRunDm);
            if (_recoveryU < 1) _recoveryU = 1;
            _foeSeparationU = U.Units(_cfg.OpponentSeparationRadiusDm);
            if (_foeSeparationU < 1) _foeSeparationU = 1;
            _foeSeparationSq = (long)_foeSeparationU * _foeSeparationU;
            _interceptU = U.Units(_cfg.InterceptReachDm);
            _pressureU = U.Units(_cfg.PressureRadiusDm);
            if (_pressureU < 1) _pressureU = 1;
            _shootRangeU = U.Units(_cfg.MaxShootRangeDm);
            _insetU = U.Units(_cfg.TouchlineInsetDm);
            if (_insetU < 0) _insetU = 0;
            _markTaken = new bool[_n];

            // Speeds come off the config in decimetres per SECOND and are turned into units per
            // tick here — the one place the time base and the length unit meet (engine phase 1).
            _ball = new MatchBall(_cfg);
            _maxPassForce = U.PerTick(_cfg.MaxPassSpeedDmPerSecond, _cfg);
            _maxShootForce = U.PerTick(_cfg.MaxShootSpeedDmPerSecond, _cfg);
            _arrivalStepU = U.PerTick(_cfg.PassArrivalSpeedDmPerSecond, _cfg);
            if (_arrivalStepU < 1) _arrivalStepU = 1;
            _carryArrivalU = U.PerTick(_cfg.CarryArrivalSpeedDmPerSecond, _cfg);
            if (_carryArrivalU < 1) _carryArrivalU = 1;
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
                    PlayerAttributes attributes = slot.Player.Attributes;
                    _skPassing[k] = attributes.Passing;
                    _skTechnique[k] = attributes.Technique;
                    _skDribbling[k] = attributes.Dribbling;
                    _skDefending[k] = attributes.Defending;
                    _skPositioning[k] = attributes.Positioning;
                    _skStrength[k] = attributes.Strength;
                    _skShooting[k] = attributes.Shooting;
                    _skGoalkeeping[k] = attributes.Goalkeeping;
                    _skPace[k] = attributes.Pace;
                    _vision[k] = BallSkill.VisionPercent(attributes.Positioning, _cfg);

                    int pace = attributes.Pace;
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
            _halfTime = lastTick / 2;
            _kickedOffFirst = 0;
            _secondHalf = false;
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

                // The shape, once for the tick, and FIRST: everything that asks where a man
                // belongs reads it and nothing recomputes it, and the duties below are read off
                // it — where the back line is decides who has got in behind it.
                UpdateBlock(side);

                if (_attacking[side])
                {
                    if (rethink) UpdateSupport(tick, side);
                }
                else if (rethink)
                {
                    AssignDuties(side);
                }
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
                    if (_keeper[k] || _sentOff[k] || i == carrier || i == _chaser[side]) continue;
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
        /// Who does what, for a side without the ball (engine phase 3).
        ///
        /// What this replaced put a marker on every one of the ten opponents, wherever he was
        /// (§1.5): ten duels roaming the pitch, a Zone branch that ran 0.0% of the time, and a
        /// defending shape that was the attacking shape moved five metres back — which is why the
        /// two rows of the measurement agreed to a tenth of a metre. Football is zonal. One man
        /// goes to the ball (decided every tick, not here), one covers him, an opponent is picked
        /// up only where he is genuinely dangerous — inside our own third, or already through the
        /// back line — and everybody else holds his place in the block.
        /// </summary>
        private void AssignDuties(int side)
        {
            int opponent = 1 - side;
            bool home = side == 0;

            for (int i = 0; i < _n; i++)
            {
                _duty[side * _n + i] = DutyZone;
                _mark[side * _n + i] = -1;
            }

            // The line, in depth from our own goal: past it an opponent is in behind.
            int lineDepth = home
                ? U.Dm(_blockLineU[side])
                : Pitch.LengthDm - U.Dm(_blockLineU[side]);

            bool[] taken = _markTaken;
            for (int i = 0; i < _n; i++) taken[i] = false;

            // The dangerous ones first — nearest our goal wins, and a man the timeline says has
            // found a yard counts as nearer than he is.
            for (int pass = 0; pass < _cfg.MaxMarkers; pass++)
            {
                int worst = -1, worstDanger = int.MaxValue;
                for (int j = 0; j < _n; j++)
                {
                    int ok = opponent * _n + j;
                    if (_keeper[ok] || _sentOff[ok] || taken[j]) continue;

                    int depth = home ? U.Dm(_px[ok]) : Pitch.LengthDm - U.Dm(_px[ok]);
                    bool inOurThird = depth <= _cfg.MarkOwnThirdDepthDm;
                    bool inBehind = depth < lineDepth - _cfg.MarkBehindLineDm;
                    if (!inOurThird && !inBehind) continue;

                    int danger = depth;
                    if (j == _looseMan[opponent]) danger -= _cfg.MarkOwnThirdDepthDm / 4;
                    if (danger < worstDanger) { worstDanger = danger; worst = j; }
                }

                if (worst < 0) break;
                taken[worst] = true;

                // The nearest man who has nothing else to do takes him.
                int foe = opponent * _n + worst;
                int best = -1;
                long bestDistance = long.MaxValue;
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    if (_keeper[k] || _sentOff[k] || _duty[k] != DutyZone || _chaser[side] == i) continue;
                    long d = U.DistanceSq(_px[k], _py[k], _px[foe], _py[foe]);
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }

                if (best < 0) break;
                _duty[side * _n + best] = DutyMark;
                _mark[side * _n + best] = worst;
            }

            // And one man covers the space behind whoever goes to the ball.
            CoverSpot(side, out int cx, out int cy);
            int cover = -1;
            long coverRange = (long)U.Units(_cfg.CoverMaxRangeDm) * U.Units(_cfg.CoverMaxRangeDm);
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (_keeper[k] || _sentOff[k] || _duty[k] != DutyZone || _chaser[side] == i) continue;
                if (_lineRank[k] == 0) continue;   // a centre-back covering is a centre-back out of the line
                long d = U.DistanceSq(_px[k], _py[k], cx, cy);
                if (d < coverRange) { coverRange = d; cover = i; }
            }

            if (cover >= 0) _duty[side * _n + cover] = DutyCover;
        }

        // ------------------------------------------------------------------ acting

        /// <summary>The one decision a player makes per tick: what to do with the ball if he has it.</summary>
        private void Act(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            if (_sentOff[k]) return;

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

            // Engine phase 4: he WEIGHS what he can do instead of working down a fixed ladder.
            // A pass, a run and a hoof are all quoted in the same currency — the metres of
            // forward progress he expects out of it, less what giving it away where it would be
            // lost is worth to the other side — so they can be compared at all. What he can see
            // of that risk comes off his Positioning; what he can execute comes off his Passing,
            // Technique and Dribbling. That is where the difference between two players lives.
            int pressure = PressurePermille(side, slot);
            int vision = _vision[k];

            int passValue = FindPass(tick, side, slot, pressure, vision, out PassChoice pass);
            int carryValue = CarryValue(side, slot, pressure, vision);
            int clearValue = ClearValue(side, slot, vision);

            if (pass.Slot >= 0 && passValue >= carryValue && passValue >= clearValue)
            {
                PlayPass(tick, side, slot, pass, pressure);
                return;
            }

            if (clearValue > carryValue)
            {
                Clear(tick, side, slot);
                return;
            }

            Carry(tick, side, slot);
        }

        /// <summary>
        /// How hard he is being pressed, in permille: 0 with nobody near, 1000 with an opponent
        /// standing on him. The whole phase reads this — it widens the execution error, shortens
        /// the touch he takes and is what turns a comfortable ball into a hurried one.
        /// </summary>
        private int PressurePermille(int side, int slot)
        {
            int k = side * _n + slot;
            int opponent = 1 - side;
            int worst = 0;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (_sentOff[ok]) continue;
                int distance = U.Distance(_px[k], _py[k], _px[ok], _py[ok]);
                if (distance >= _pressureU) continue;
                int p = 1000 - 1000 * distance / _pressureU;
                if (p > worst) worst = p;
            }

            return worst;
        }

        /// <summary>
        /// What losing the ball HERE is worth to the other side, in decimetres of forward
        /// progress. Priced at the place the ball would be lost, not the place it is now — which
        /// is the whole reason a clearance can be the right answer: it moves the turnover forty
        /// metres up the pitch, where it costs a fraction of what it costs on your own box.
        /// </summary>
        private int TurnoverCostDm(int side, int x)
        {
            int depth = side == 0 ? U.Dm(x) : Pitch.LengthDm - U.Dm(x);
            int third = depth * 3 / Pitch.LengthDm;
            int[] table = _cfg.TurnoverCostDm;
            if (table.Length == 0) return 0;
            if (third < 0) third = 0;
            if (third >= table.Length) third = table.Length - 1;
            return table[third];
        }

        /// <summary>The nearest opponent to a point, in whole decimetres.</summary>
        private int NearestOpponentDm(int side, int x, int y)
        {
            int opponent = 1 - side;
            int best = int.MaxValue;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (_sentOff[ok]) continue;
                int distance = U.Distance(x, y, _px[ok], _py[ok]);
                if (distance < best) best = distance;
            }

            return best == int.MaxValue ? Pitch.LengthDm : U.Dm(best);
        }

        /// <summary>Per-tick odds, in permille, that the challenger takes it off the carrier.</summary>
        private int DuelWinPermille(int carrier, int challenger)
            => BallSkill.DuelWinPermille(
                _skDribbling[carrier], _skTechnique[carrier], _skStrength[carrier], _skPace[carrier],
                _skDefending[challenger], _skPositioning[challenger], _skPace[challenger], _cfg);

        /// <summary>How far ahead he knocks it: short when he is pressed, long when he has room.</summary>
        private int CarryTouchDm(int k, int pressure)
        {
            int skill = BallSkill.Mix(_skDribbling[k], 6, _skPace[k], 4);
            int touch = BallSkill.Lerp(pressure, 1000, _cfg.CarryTouchFreeDm, _cfg.CarryTouchPressedDm);
            return touch + _cfg.CarryTouchSkillDm * skill / 100;
        }

        /// <summary>
        /// Running with it. Worth the ground he covers, at the odds he keeps it past the man in
        /// front of him — which is a duel, so a dribbler carries where a centre-back would not.
        /// </summary>
        private int CarryValue(int side, int slot, int pressure, int vision)
        {
            int k = side * _n + slot;
            if (_keeper[k]) return int.MinValue / 4;

            int touch = CarryTouchDm(k, pressure);

            // Only as far as there is pitch left to run into. Without this a man carries the
            // ball to the byline and keeps carrying, because the option is priced on the touch
            // he would take rather than on the ground actually in front of him — which is how
            // the play deserted the middle of the pitch and the ball spent a match sitting on
            // a boundary.
            int goalX = U.Units(MovementGeometry.AttackedGoalX(side == 0));
            int room = U.Dm(goalX > _px[k] ? goalX - _px[k] : _px[k] - goalX);
            if (touch > room) touch = room;

            int keep = 1000;

            int opponent = 1 - side;
            int nearest = -1, gap = int.MaxValue;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (_sentOff[ok]) continue;
                int distance = U.Distance(_px[k], _py[k], _px[ok], _py[ok]);
                if (distance < gap) { gap = distance; nearest = ok; }
            }

            if (nearest >= 0 && gap < _pressureU)
            {
                int perTick = DuelWinPermille(k, nearest);
                int loss = perTick * _cfg.DribbleFlightTicks;
                keep = 1000 - BallSkill.Clamp(loss, 0, 850);
            }

            int value = BallSkill.OptionValue(
                keep, touch + _cfg.PossessionValueDm, TurnoverCostDm(side, _px[k]), vision);
            return value * _cfg.CarryValuePercent / 100;
        }

        /// <summary>
        /// Hoofing it. He keeps it only now and then, but the ball is forty metres up the pitch
        /// when he loses it, and that is what makes it the right answer with three men on him.
        /// </summary>
        private int ClearValue(int side, int slot, int vision)
        {
            int k = side * _n + slot;
            if (_keeper[k]) return int.MinValue / 4;
            if (!ClearRoom(side, k, out int landing)) return int.MinValue / 4;

            int dir = MovementGeometry.Direction(side == 0);
            int gain = dir * (landing - _px[k]) / U.Scale;
            return BallSkill.OptionValue(
                _cfg.ClearanceRetentionPermille, gain + _cfg.PossessionValueDm,
                TurnoverCostDm(side, landing), vision);
        }

        /// <summary>
        /// Where a clearance would LAND, and whether there is anywhere to clear it to at all.
        ///
        /// A hoof is "get rid of it", and it needs pitch in front of it. Until engine phase 5 the
        /// landing point was simply clamped to the goal line, so a forward standing in the last
        /// twenty metres could "clear" the ball — aimed at a point ON the line he was attacking,
        /// which is a goal kick by construction. Twenty-seven of the engine's thirty-nine goal
        /// kicks a match were that: not a clearance at all, and nothing a footballer would do.
        /// </summary>
        private bool ClearRoom(int side, int k, out int landing)
        {
            int dir = MovementGeometry.Direction(side == 0);
            int goalX = U.Units(MovementGeometry.AttackedGoalX(side == 0));
            int room = dir * (goalX - _px[k]) - U.Units(_cfg.ClearanceGoalGapDm);

            landing = _px[k];
            if (room < U.Units(_cfg.MinClearanceDm)) return false;

            int reach = U.Units(_cfg.ClearanceDistanceDm);
            if (reach > room) reach = room;
            landing = _px[k] + dir * reach;
            return true;
        }

        private void Carry(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);

            int touch = CarryTouchDm(k, PressurePermille(side, slot));
            CarryTarget(side, k, U.Units(touch), out int targetX, out int targetY);
            int distance = U.Distance(_px[k], _py[k], targetX, targetY);

            // He knocks it ahead and runs onto it. The push has to be long enough that one
            // touch covers real ground — knocking it a couple of metres every other tick is the
            // same picture with the feed full of "dribble, dribble, dribble" — and weighted so
            // it comes to rest where he is going rather than running away from him.
            int carryForce = _ball.ForceToArrive(distance, _carryArrivalU, _maxPassForce, out int carryTicks);
            _ball.Kick(side, slot, targetX - _px[k], targetY - _py[k], carryForce);

            int hold = HoldTicks(side);
            int settle = carryTicks < _cfg.DribbleFlightTicks ? carryTicks : _cfg.DribbleFlightTicks;
            _hold[k] = hold < settle ? settle : hold;

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

        /// <summary>
        /// Where a man running with the ball is going. Straight on while there is pitch in front
        /// of him, and at the goal mouth once there is not — a carrier who only ever knows
        /// "forward" ends up in the corner flag with nowhere to go, which is what put the ball
        /// on a boundary for minutes at a time and emptied the middle of the pitch.
        /// </summary>
        private void CarryTarget(int side, int k, int reachU, out int tx, out int ty)
        {
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);
            int goalX = U.Units(MovementGeometry.AttackedGoalX(home));
            int room = dir * (goalX - _px[k]);

            int aimX, aimY;
            if (room <= U.Units(_cfg.CarryCutInsideDm))
            {
                // In the last stretch he attacks the goal, not the line behind it.
                aimX = goalX - dir * U.Units(_cfg.CarryGoalStandOffDm);
                aimY = U.CenterYU;
            }
            else
            {
                aimX = _px[k] + dir * U.Units(Pitch.LengthDm);
                aimY = _py[k] + (U.CenterYU - _py[k]) / 8;
            }

            int dx = aimX - _px[k], dy = aimY - _py[k];
            int span = U.Length(dx, dy);
            if (span <= 0 || span <= reachU)
            {
                tx = U.ClampX(aimX);
                ty = Inside(aimY);
                return;
            }

            tx = U.ClampX(_px[k] + (int)((long)dx * reachU / span));
            ty = Inside(_py[k] + (int)((long)dy * reachU / span));
        }

        private void Clear(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);
            int pressure = PressurePermille(side, slot);

            int targetY = _py[k] < U.CenterYU ? U.Units(60) : U.WidthU - U.Units(60);
            ClearRoom(side, k, out int targetX);

            // PUTTING IT OUT (engine phase 5). Deep in his own third with a man on him, a defender
            // does not try to play football: he puts the ball out of the ground, and concedes the
            // corner or the throw-in that comes with it. It is a decision every defender makes
            // several times a match, and the engine had no way to express it — which is most of
            // the reason it produced 1.3 corners a match against football's eight to thirteen.
            int ownGoalX = U.Units(MovementGeometry.OwnGoalX(home));
            int depth = dir * (_px[k] - ownGoalX);
            bool desperate = depth <= U.Units(_cfg.ClearBehindDepthDm)
                             && pressure >= _cfg.ClearOutPressurePermille;

            int toTouch = _py[k] < U.CenterYU ? _py[k] : U.WidthU - _py[k];
            bool intoTouch = toTouch <= U.Units(_cfg.ClearIntoTouchDm)
                             && pressure >= _cfg.ClearOutPressurePermille
                             && _rng.NextInt(0, 1000) < _cfg.ClearIntoTouchPermille;
            bool behind = desperate && _rng.NextInt(0, 1000) < _cfg.ClearBehindPermille;

            if (behind || intoTouch)
            {
                int overshoot = U.Units(60);

                // Behind for the corner if his own line is the nearer one, into touch otherwise.
                if (behind && depth <= toTouch)
                {
                    // Behind, and WIDE of his own goal: sliced past the post rather than through
                    // the middle of the mouth, where his own keeper would simply pick it up.
                    targetX = ownGoalX - dir * overshoot;
                    targetY = _py[k] < U.CenterYU
                        ? U.CenterYU - U.Units(_cfg.ClearBehindOffCentreDm)
                        : U.CenterYU + U.Units(_cfg.ClearBehindOffCentreDm);
                }
                else
                {
                    targetX = _px[k] + dir * U.Units(80);
                    targetY = _py[k] < U.CenterYU ? -overshoot : U.WidthU + overshoot;
                }

                // Hit as hard as he can hit it: this is a man getting rid of the ball, and a ball
                // rolling gently towards the line is a ball his own keeper collects.
                _ball.Kick(side, slot, targetX - _px[k], targetY - _py[k], _maxPassForce);
                Release(tick, side, slot);
                Record(tick, BallActionKind.Clearance, home, slot, -1);
                return;
            }

            // A clearance is the most MISPLACED kick in football: hit hard, hit early, hit under
            // pressure and aimed at nothing but away. Phase 4 gave every PASS its execution error
            // and left this one exact, so a hoof landed on the centimetre it was aimed at and the
            // ball never once left the pitch off one (engine phase 5). Same error model, at long-
            // ball width, which is where a large share of football's forty throw-ins a match come
            // from.
            int error = BallSkill.PassErrorPermille(
                _skPassing[k], _skTechnique[k], pressure, longBall: true, _cfg);
            int spread = U.Length(targetX - _px[k], targetY - _py[k]) * BallSkill.Spread(_rng, error) / 1000;
            targetY += spread;

            // Struck to TRAVEL the distance it is aimed at and to be DYING not long after it, not
            // struck as hard as a ball can be struck. Hitting every clearance at maximum force is
            // what sent twenty-nine balls a match over the far goal line: a hoof aimed forty metres
            // upfield from a defender's own box flew the length of the pitch and out. It still
            // arrives with pace on it — it is a clearance, not a pass — which is why the arrival
            // speed here is a multiple of the pass's (engine phase 5).
            int distance = U.Distance(_px[k], _py[k], targetX, targetY);
            _ball.Kick(side, slot, targetX - _px[k], targetY - _py[k],
                _ball.ForceToArrive(
                    distance, _arrivalStepU * _cfg.ClearanceArrivalPercent / 100, _maxPassForce, out int _));
            Release(tick, side, slot);
            Record(tick, BallActionKind.Clearance, home, slot, -1);
        }

        private void TakeRestart(int tick, int side, int slot)
        {
            _ball.Dead = false;
            Collect(side, slot);
            _hold[side * _n + slot] = 1;
            _restartGrace = tick + _cfg.RestartGraceTicks;
            _restartTaker = side * _n + slot;

            // A penalty is STRUCK, from the spot (Law 14).
            if (_deadKind == BallActionKind.Penalty)
            {
                TakePenalty(tick, side, slot);
                return;
            }

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

            // How good a chance this is (engine phase 4). The timeline still owns the OUTCOME
            // until the causality is inverted in phase 6; what the quality decides here is where
            // the ball goes and whether the keeper can hold it. A good finisher's goal is placed
            // inside the post and his miss is a yard wide; a poor one's goal squeezes past the
            // keeper and his miss is in the stand.
            _shotQuality = ShotQuality(side, slot);

            int aimX = goalX;
            int aimY;
            switch (outcome)
            {
                case MatchEventType.Goal:
                    int reach = half - U.Units(6);
                    int centre = reach * _cfg.ShotPlacementCentrePermille / 1000;
                    int placed = centre + (reach - centre) * _shotQuality / 1000;
                    aimY = U.CenterYU + (_rng.NextInt(0, 2) == 0 ? -placed : placed);
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
                    int spread = U.Units(_cfg.ShotMissSpreadDm) * (1000 - _shotQuality) / 1000;
                    aimY = U.CenterYU + wide * (half + U.Units(10) + _rng.NextInt(0, spread + 1));
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
        /// How good this chance is, in permille — an xG-shaped reading of how far out it is, how
        /// tight the angle is, how many bodies are in the way and who is hitting it. Phase 4
        /// spends it on the placement and on the keeper's hands; phase 6, when the causality is
        /// inverted and the simulation scores its own goals, is where it decides the goal.
        /// </summary>
        private int ShotQuality(int side, int slot)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            int goalX = U.Units(MovementGeometry.AttackedGoalX(home));

            int distance = U.Dm(U.Distance(_ball.X, _ball.Y, goalX, U.CenterYU));
            int offCentre = U.Dm(_ball.Y > U.CenterYU ? _ball.Y - U.CenterYU : U.CenterYU - _ball.Y);

            int opponent = 1 - side;
            int crowd = 0;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (_keeper[ok] || _sentOff[ok]) continue;
                if (U.Distance(_px[ok], _py[ok], _ball.X, _ball.Y) < _pressureU) crowd++;
            }

            return BallSkill.ShotQualityPermille(
                distance, offCentre, crowd, _skShooting[k], _skTechnique[k], _cfg);
        }

        private int HoldTicks(int side)
        {
            MovementTactics t = _tactics[side];
            int min = t.HoldTicksMin < 1 ? 1 : t.HoldTicksMin;
            int max = t.HoldTicksMax < min ? min : t.HoldTicksMax;
            int held = _rng.NextInt(min, max + 1);
            return _urgent[side] ? 1 : held;
        }

        // ------------------------------------------------------------------ passing

        /// <summary>One way of playing it, and what it is worth.</summary>
        private struct PassChoice
        {
            public int Slot;
            public int X;
            public int Y;
            public int Force;
            public int Completion;
        }

        /// <summary>
        /// Looking up. Every team-mate is considered three ways — to his feet and either side of
        /// him, because a ball into his path is often on when a ball to his feet is not — and
        /// each option is priced: the odds it comes off, the ground it gains, the cost of losing
        /// it where it would be lost.
        ///
        /// Before this phase the test was binary and geometric: a lane no opponent could reach
        /// was "safe" and anything else was not, and the last seven metres in front of the
        /// receiver were excluded from the test altogether so a marked man could still be found.
        /// That exclusion is why 44% of passes went straight to an opponent — the marker standing
        /// on the receiver was invisible to the only test being run. He is now priced instead of
        /// ignored, which is a different thing entirely.
        /// </summary>
        private int FindPass(int tick, int side, int slot, int pressure, int vision, out PassChoice best)
        {
            best = new PassChoice { Slot = -1 };
            int k = side * _n + slot;
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);
            int priority = _director.PriorityTarget(tick, home);

            int bias = _tactics[side].ForwardBias;
            if (_urgent[side]) bias = bias * _cfg.ChanceForwardPercent / 100;

            int bestValue = int.MinValue;
            int tolerance = _controlU + U.Units(20);

            // Law 11, from the passer's own eyes (engine phase 5). He will not knowingly play a
            // man offside, so every option beyond the line he BELIEVES is there is struck off —
            // and when what he believes is wrong, the flag goes up. One draw per decision, taken
            // here rather than per option so that the line he is playing to is one line.
            int offsideLine = PerceivedOffsideLine(k, OffsideLineDepth(side)) + U.Units(_cfg.OffsideMarginDm);

            for (int j = 0; j < _n; j++)
            {
                if (j == slot) continue;
                int rk = side * _n + j;
                if (_sentOff[rk]) continue;
                if (dir * _px[rk] > offsideLine) continue;
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
                    ty = Inside(ty);

                    int distance = U.Distance(_px[k], _py[k], tx, ty);
                    if (distance < U.Units(_cfg.MinPassDm)) continue;

                    // Only a ball that ARRIVES about when it is meant to, and arrives DYING —
                    // weighted so the man it is for can take it in his stride rather than
                    // stepping into the path of something still travelling at seventeen metres
                    // a second. Beyond the range of a ball struck as hard as it can be struck
                    // the option is not a pass at all.
                    if (distance > _ball.RangeOf(_maxPassForce)) continue;
                    int force = _ball.ForceToArrive(
                        distance, _arrivalStepU, _maxPassForce, out int flight);
                    if (_ball.RangeInTicks(force, flight) < distance - _controlU) continue;

                    // Three things have to go right, and they are three different questions:
                    // does it get through the lane, does the man it is for have room to take it,
                    // and can this player actually hit it.
                    int completion = LaneCompletion(side, _px[k], _py[k], tx, ty, force);
                    completion = completion * BallSkill.ReceptionPermille(NearestOpponentDm(side, tx, ty), _cfg) / 1000;

                    bool longBall = distance > U.Units(_cfg.LongBallFromDm);
                    int error = BallSkill.PassErrorPermille(_skPassing[k], _skTechnique[k], pressure, longBall, _cfg);
                    int miss = (int)((long)distance * error / 1000 * 375 / 1000);
                    if (miss > tolerance)
                        completion = completion * BallSkill.Lerp(miss - tolerance, tolerance * 3, 1000, 250) / 1000;

                    if (completion < _cfg.MinPassCompletionPermille && j != priority) continue;

                    int gainDm = dir * (tx - _px[k]) / U.Scale * bias / 10;
                    int value = BallSkill.OptionValue(
                        completion, gainDm + _cfg.PossessionValueDm, TurnoverCostDm(side, tx), vision);

                    if (j == priority) value += _cfg.ChanceOptionBonusDm;
                    if (_keeper[rk]) value -= _cfg.BackToKeeperCostDm;
                    if (AlreadySupporting(side, j)) value += _cfg.SupportingRunBonusDm;

                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = new PassChoice
                        {
                            Slot = j,
                            X = tx,
                            Y = ty,
                            Force = force,
                            Completion = completion
                        };
                    }
                }
            }

            return best.Slot >= 0 ? bestValue : int.MinValue;
        }

        /// <summary>
        /// Striking it — and MISSING, by an amount his Passing and Technique earn him and the
        /// man on his shoulder takes away. The error is sideways off the line (the ball is
        /// dragged or over-hit across it) plus a little on the weight, and it is drawn as the
        /// average of two uniforms so that most balls are near their line and the wild one is
        /// rare. Before this phase every pass was executed exactly, which is why no attribute
        /// but Pace could be seen in the picture at all.
        /// </summary>
        private void PlayPass(int tick, int side, int slot, PassChoice choice, int pressure)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            int dir = MovementGeometry.Direction(home);

            int dx = choice.X - _px[k], dy = choice.Y - _py[k];
            int distance = U.Length(dx, dy);
            bool longBall = distance > U.Units(_cfg.LongBallFromDm);

            int error = BallSkill.PassErrorPermille(_skPassing[k], _skTechnique[k], pressure, longBall, _cfg);
            int sideways = (int)((long)distance * BallSkill.Spread(_rng, error) / 1000);
            int weight = BallSkill.Spread(_rng, error * _cfg.PassWeightErrorPermille / 1000);

            // Perpendicular to the pass, which is an exact integer operation: no angle, no
            // trigonometry, nothing that could round differently on another runtime.
            int aimX = choice.X, aimY = choice.Y;
            if (distance > 0 && sideways != 0)
            {
                aimX += (int)((long)(-dy) * sideways / distance);
                aimY += (int)((long)dx * sideways / distance);
            }

            aimX = U.ClampX(aimX);
            aimY = U.ClampY(aimY);

            int force = choice.Force + (int)((long)choice.Force * weight / 1000);
            if (force < 1) force = 1;
            if (force > _maxPassForce) force = _maxPassForce;

            _ball.Kick(side, slot, aimX - _px[k], aimY - _py[k], force);
            Release(tick, side, slot);

            // The assistant's flag, decided at the moment the ball is PLAYED and answered only if
            // one of the flagged men then touches it (engine phase 5).
            ClearFlags();
            FlagOffside(side, slot);

            // A ball played backwards is the moment the other side steps up (engine phase 3).
            if (dir * (choice.X - _px[k]) < -U.Units(_cfg.BackPassMinDm))
                _pressUntil[1 - side] = tick + _cfg.PressBackPassTicks;

            // He watches it fly: the man it is for runs to where it is ACTUALLY going, not to
            // where it was meant to go. A misplaced ball still drags him out of position, which
            // is the price of it, but it does not leave him standing while it rolls past.
            _receiver[side] = choice.Slot;
            _receiveX[side] = aimX;
            _receiveY[side] = aimY;

            BallActionKind kind = distance > U.Units(_cfg.LongBallFromDm)
                ? BallActionKind.LongBall
                : IsCross(side, k) ? BallActionKind.Cross : BallActionKind.Pass;
            Record(tick, kind, home, slot, choice.Slot);
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
                // What that stretch is NOT is free: the room the receiver has is priced by
                // BallSkill.ReceptionPermille, next to this. Leaving it unpriced, which is what
                // the old yes/no test did, is what made a ball into a marker's feet "safe".
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

        // ------------------------------------------------------------------ movement

        private void Move(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            bool home = side == 0;
            bool sprint = false;
            int tx, ty;

            // Sent off (engine phase 5). He WALKS off — to the touchline by the halfway line, at
            // the pace of a man who has just been sent off, because a body that jumps forty metres
            // in a tenth of a second is the one thing the stream's own contract forbids — and takes
            // no further part: every loop that reads the pitch skips him, so his side genuinely
            // plays the rest of the match with ten men. The duties are shared out among ten, the
            // offside line is drawn off ten, and there are ten men to pass to.
            if (_sentOff[k])
            {
                // Aimed just OUTSIDE the touchline so the pitch's own clamp puts him exactly on it:
                // steering at the line itself leaves him a couple of metres short, inside the field
                // of play, which is precisely where he may not be.
                Steer(k, U.CenterXU, home ? -U.Units(40) : U.WidthU + U.Units(40), sprint: false);
                return;
            }

            // Filled by the press test below whenever this side is defending; the compiler
            // cannot see that the branch which reads them is the same branch that sets them.
            int pressHomeX = 0, pressHomeY = 0;

            if (_ball.Dead && _deadSide == side && _deadTaker == slot)
            {
                // He goes to the ball — and at a kickoff he stands just BEHIND it, in his own half,
                // because that is where the man taking a kickoff stands (Law 8) and standing on the
                // centre spot itself puts him a stride into the other half. Placed, not steered:
                // the deadband would leave him three metres off the ball, which at a kickoff is
                // three metres inside the other side's half.
                WalkTo(
                    k,
                    _deadKind == BallActionKind.Kickoff
                        ? _ball.X - MovementGeometry.Direction(home) * U.Units(_cfg.KickoffStandOffDm)
                        : _ball.X,
                    _ball.Y);
                return;
            }
            else if (_ball.Dead && _deadKind == BallActionKind.Kickoff)
            {
                // A kickoff is taken with both sides in their own half (Law 8), and it is a
                // PLACEMENT like the wall: nobody makes a supporting run into the other half while
                // the referee waits, and a man caught over the line WALKS back onto his mark rather
                // than steering at it — steering leaves him a metre or two the wrong side of
                // halfway, because inside fifteen metres the approach paces him down to a crawl.
                HomeSpot(side, slot, out tx, out ty);
                WalkTo(k, tx, ty);
                return;
            }
            else if (RetreatSpot(side, slot, out int retreatX, out int retreatY))
            {
                // Ten yards, and the wall (Laws 13 and 14) — WALKED TO, not steered at.
                //
                // This is the one place in the model where a man is going to a MARK rather than to
                // the football, and the steering is wrong for it in both of its gears, which is
                // what the replay dump showed: sprinting, he carries his momentum straight past the
                // spot and spends the stoppage orbiting it (measured eight metres beyond); walking,
                // the approach slowdown paces him down to a fifth of a jog inside fifteen metres and
                // the arrival deadband stops him three metres short — so he never makes the nine
                // fifteen and the wall stands at five metres, which is against the law he is
                // standing there to obey. A referee walks a wall onto its mark and it stays there.
                WalkTo(k, retreatX, retreatY);
                return;
            }
            else if (_ball.OwnerSide == side && _ball.OwnerSlot == slot)
            {
                // On the ball he goes FORWARD, drifting off the touchline toward the middle,
                // and once he is inside the last quarter he stops running at the byline and
                // cuts in at the goal instead. Sending him back to his position is how a
                // defender ends up carrying the ball into his own corner; sending him straight
                // ahead for ever is how he ends up standing on the goal line holding it.
                CarryTarget(side, k, U.Units(160), out tx, out ty);
                sprint = true;
            }
            else if (_keeper[k])
            {
                KeeperSpot(side, out tx, out ty);
            }
            else if (_ball.Free && !_ball.Dead && _receiver[side] == slot)
            {
                // He runs to MEET it, not to the spot it was aimed at (engine phase 4). Steering
                // to a fixed point leaves him standing two or three metres off the line while
                // the ball rolls past — and two or three metres is the whole of a footballer's
                // control radius, which is why a pass into twelve metres of clear space was
                // still lost a quarter of the time.
                InterceptSpot(k, out tx, out ty);
                sprint = true;
            }
            else if (_ball.Free && !_ball.Dead && _chaser[side] == slot)
            {
                InterceptSpot(k, out tx, out ty);
                sprint = true;
            }
            else if (!_attacking[side] && Pressing(tick, side, slot, out pressHomeX, out pressHomeY))
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

                // A supporting run is a BURST to get there, not a ninety-minute sprint. Flat out
                // while the ground is still to be covered, and a jog once he is in the space he
                // ran into — which is the difference between a side that runs 12.1 km a man with
                // a busiest of 19.5 and one that runs 11 with a busiest of 15.
                sprint = _urgent[side]
                         || U.DistanceSq(_px[k], _py[k], tx, ty) > (long)_approachU * _approachU;
            }
            else if (!_attacking[side] && _duty[k] == DutyCover)
            {
                CoverSpot(side, out tx, out ty);
            }
            else if (!_attacking[side] && _duty[k] == DutyMark && _mark[k] >= 0)
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

            // The recovery run (engine phase 3). A man who has been caught up the pitch does
            // not jog home while the ball goes the other way, and the difference is not cosmetic:
            // at a jog the block takes a dozen seconds to re-form, which is a dozen seconds in
            // which the side defending is a side still strung out from attacking.
            tx = U.ClampX(tx);
            ty = U.ClampY(ty);
            if (!sprint && !_attacking[side] && !_keeper[k]
                && U.DistanceSq(_px[k], _py[k], tx, ty) > (long)_recoveryU * _recoveryU)
                sprint = true;

            Steer(k, tx, ty, sprint);

            // And at a kickoff the halfway line is a WALL: the referee holds the whistle until both
            // sides are in their own half (Law 8), so nobody may move INTO the other one — the
            // step he just took across it is undone. Written as undoing his own step rather than as
            // a clamp on purpose: clamping would drag a man caught thirty metres upfield back onto
            // the line in a single tick, which is the one thing the stream's contract forbids (see
            // PositionStreamTests.NobodyTeleports). A man in the wrong half walks back under his
            // own steam — the branch above sends him home at a sprint — and this only stops him
            // going further. The ball sits on the line, so the taker can still reach it from his
            // own side of it.
            if (_ball.Dead && _deadKind == BallActionKind.Kickoff)
            {
                int dir = MovementGeometry.Direction(home);
                int over = dir * (_px[k] - U.CenterXU);
                int stepped = dir * (_px[k] - _stepFromX[k]);
                if (over > 0 && stepped > 0) _px[k] -= dir * (over < stepped ? over : stepped);
            }

            // The man ON THE BALL keeps it on the pitch — unless he is actually running over the
            // line, which is a throw-in and is left alone. Wherever the game has dragged him
            // incidentally (a support run to the touchline, a loose ball chased into the corner)
            // his feet, and so the ball at them, end up a stride inside the line rather than on
            // it; a step that ended OUTSIDE is untouched, so the referee still reads it and gives
            // the throw-in. The inset is deliberately shorter than one stride at top speed, so a
            // man who means to take it out still can (engine phase 5).
            if (_ball.OwnerSide == side && _ball.OwnerSlot == slot && !_ball.Dead
                && _stepToX[k] >= 0 && _stepToX[k] <= U.LengthU
                && _stepToY[k] >= 0 && _stepToY[k] <= U.WidthU)
            {
                _px[k] = MovementGeometry.Clamp(_px[k], _insetU, U.LengthU - _insetU);
                _py[k] = Inside(_py[k]);
            }
        }

        /// <summary>
        /// To a mark: the movement of a man being placed for a dead ball rather than one chasing a
        /// ball. No inertia to carry him past the spot and no deadband to stop him short of it — he
        /// runs while it is a long way off and walks the last stretch, and he ends up ON it.
        ///
        /// This exists because the steering is wrong for a mark in both its gears, which the replay
        /// dump showed three times over: sprinting, a man carries his momentum eight metres past the
        /// spot and orbits it; jogging, the approach slowdown paces him to a crawl inside fifteen
        /// metres and the arrival deadband parks him three metres short — so a wall stood at five
        /// metres instead of nine fifteen, and men were left standing over the halfway line at a
        /// kickoff. Capped at his own running speed, so it can no more teleport a body than the
        /// steering can (PositionStreamTests.NobodyTeleports).
        /// </summary>
        private void WalkTo(int k, int tx, int ty)
        {
            _stepFromX[k] = _px[k];
            _stepFromY[k] = _py[k];

            int dx = tx - _px[k], dy = ty - _py[k];
            int gap = U.Length(dx, dy);
            int step = gap > _approachU ? _maxSpeed[k] : _cruise[k];
            if (step < 1) step = 1;

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

            _vx[k] = 0;
            _vy[k] = 0;
            _stepToX[k] = _px[k];
            _stepToY[k] = _py[k];
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

            // The step, before the pitch is allowed to have an opinion about it. A man who runs
            // over a line is put back on it; the ball he is holding is NOT, and the referee reads
            // the crossing point off these four numbers (engine phase 5).
            _stepFromX[k] = _px[k];
            _stepFromY[k] = _py[k];
            _stepToX[k] = _px[k] + _vx[k];
            _stepToY[k] = _py[k] + _vy[k];

            _px[k] = U.ClampX(_stepToX[k]);
            _py[k] = U.ClampY(_stepToY[k]);
        }

        /// <summary>Keeps team-mates off each other, so eleven men never stand in one heap.</summary>
        private void Separate(int k, ref int wx, ref int wy, int top)
        {
            int side = k / _n;
            int pushX = 0, pushY = 0;

            // A man in the wall stands shoulder to shoulder with the two beside him: the separation
            // that keeps team-mates six metres apart is the one rule a wall exists to break.
            for (int j = 0; j < _n && !_inWall[k]; j++)
            {
                int other = side * _n + j;
                if (other == k || _sentOff[other]) continue;

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

            // And off the opposition (engine phase 3). Separation used to push team-mates apart
            // and never opponents, which is why a marker stood literally on top of his man and
            // the video showed red/blue pairs moving as one body (§1.5). The radius is shorter
            // than the distance a presser stands off the ball, so this keeps men out of each
            // other without ever stopping a challenge.
            int foePushX = 0, foePushY = 0;
            int foes = (1 - side) * _n;
            for (int j = 0; j < _n; j++)
            {
                int other = foes + j;
                if (_sentOff[other]) continue;
                int dx = _px[k] - _px[other], dy = _py[k] - _py[other];
                long sq = (long)dx * dx + (long)dy * dy;
                if (sq >= _foeSeparationSq || sq <= 0) continue;
                int distance = U.Length(dx, dy);
                if (distance <= 0) continue;

                int strength = (_foeSeparationU - distance) * top / _foeSeparationU;
                U.Scaled(dx, dy, strength, out int fx, out int fy);
                foePushX += fx;
                foePushY += fy;
            }

            wx += pushX * _cfg.SeparationStrengthPercent / 100
                + foePushX * _cfg.OpponentSeparationStrengthPercent / 100;
            wy += pushY * _cfg.SeparationStrengthPercent / 100
                + foePushY * _cfg.OpponentSeparationStrengthPercent / 100;
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
            int collapse = 1000 / _cfg.ShapeCollapseTicks;
            if (collapse < 1) collapse = 1;
            int e = _expansion[side];
            if (_attacking[side]) { e += step; if (e > 1000) e = 1000; }
            else { e -= collapse; if (e < 0) e = 0; }
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
                : _cfg.LineSpacingDm
                  * (_cfg.DefendLineSpacingPercent
                     + (_cfg.AttackLineSpacingPercent - _cfg.DefendLineSpacingPercent) * e / 1000)
                  / 100;

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

            // A KICKOFF re-forms the shape rather than holding it. The deadband is what stops the
            // line creeping after every sideways pass, but at a kickoff the block has just been
            // squeezed behind the halfway line by the cap above, and holding the old line six metres
            // deeper leaves the front two men standing in the other side's half — which is against
            // Law 8, and was measured doing exactly that (engine phase 5).
            if (!_blockSet[side] || drift > _cfg.BackLineHoldDm || kickoff)
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
            y = Inside(py);
        }

        /// <summary>A place across the pitch that is ON the pitch — a stride inside the touchline.</summary>
        private int Inside(int y) => MovementGeometry.Clamp(y, _insetU, U.WidthU - _insetU);

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
                if (i == excluded || _keeper[k] || _sentOff[k]) continue;
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
        private bool Pressing(int tick, int side, int slot, out int homeX, out int homeY)
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
            if (_urgent[side])
            {
                reach = reach * _cfg.ChancePressPercent / 100;
            }
            else
            {
                // The TRIGGER (engine phase 3). A side does not chase the man on the ball
                // wherever he stands: it has a zone it presses in, which is what the coach's
                // Pressing instruction has always meant, plus the two situations that switch the
                // press on outside it — a ball played backwards, and a man receiving with a
                // touchline behind him. Outside both, the block holds its shape and lets him
                // have it. (A loose ball is not in here on purpose: it is chased by the branch
                // above this one, whoever owns the trigger.)
                int ballDepth = side == 0 ? U.Dm(_ball.X) : Pitch.LengthDm - U.Dm(_ball.X);
                if (ballDepth > _tactics[side].PressTriggerDepthDm) return false;

                int boost = 100;
                if (_pressUntil[side] > tick) boost = boost * _cfg.PressBackPassPercent / 100;
                int offCentre = _ball.Y - U.CenterYU;
                if (offCentre < 0) offCentre = -offCentre;
                if (offCentre > U.Units(_cfg.PressWideThresholdDm))
                    boost = boost * _cfg.PressWideReceptionPercent / 100;
                reach = reach * boost / 100;
            }

            return U.DistanceSq(homeX, homeY, _ball.X, _ball.Y) <= reach * reach;
        }

        /// <summary>
        /// Where the covering man stands: goal-side of the ball and a few metres off it. Not on
        /// the ball — that is the presser's job — but in the space the presser left behind him.
        /// </summary>
        private void CoverSpot(int side, out int x, out int y)
        {
            bool home = side == 0;
            int goalX = U.Units(MovementGeometry.OwnGoalX(home));
            U.Scaled(goalX - _ball.X, U.CenterYU - _ball.Y, U.Units(_cfg.CoverDistanceDm),
                out int gx, out int gy);
            x = U.ClampX(_ball.X + gx);
            y = U.ClampY(_ball.Y + gy);
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

            // A man who is NOT on the back line does not drop in behind it (engine phase 3).
            // He takes his man goal-side inside his own zone; the space between the back line and
            // the goal belongs to the back four, and a midfielder dropping into it is what put
            // eight metres of daylight through a line that is supposed to be flat.
            int ownLineU = _blockLineU[side];
            if (_lineRank[k] != 0)
            {
                int spotDepth = home ? x : U.LengthU - x;
                int floor = home ? ownLineU : U.LengthU - ownLineU;
                if (spotDepth < floor) x = ownLineU;
                return;
            }

            // A man on the back line HOLDS THE LINE (engine phase 2). He picks his opponent up
            // across the pitch, but he does not follow him up and down it: four defenders each
            // tracking his own man's depth is exactly how a back four stops being a line and
            // becomes four separate duels — the fourteen metres of "back line spread" the
            // harness was reading. He leaves the line only for a man who has already got behind
            // it, which is the one thing the line exists to deal with.
            int lineU = ownLineU;
            int lineDepth = home ? lineU : U.LengthU - lineU;
            int manDepth = home ? _px[ok] : U.LengthU - _px[ok];
            if (manDepth >= lineDepth - U.Units(_cfg.MarkBehindLineDm)) x = lineU;
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
            BallToFeet(k);
            if (!sameMan)
            {
                _gotBallX[k] = _px[k];
                _gotBallY[k] = _py[k];
                _runCalled[k] = false;
            }
        }

        /// <summary>
        /// The ball, at a man's feet — and ON the pitch (engine phase 5). A player can be a stride
        /// over the touchline while the ball is still in play at his inside foot; what cannot
        /// happen is the ball itself resting on a line, which is the throw-in the laws demand and
        /// the invariant the harness checks. A carrier who genuinely takes it over the line has
        /// already conceded the throw-in in <see cref="CarrierOutOfPlay"/>, which reads his
        /// unclamped step and not this.
        /// </summary>
        private void BallToFeet(int k)
        {
            _ball.X = MovementGeometry.Clamp(_px[k], _insetU, U.LengthU - _insetU);
            _ball.Y = Inside(_py[k]);
        }

        private void ResolveControl(int tick)
        {
            if (_ball.Dead) return;

            if (!_ball.Free)
            {
                // The carrier keeps it glued to his feet; a challenge can still take it.
                int owner = _ball.OwnerSide * _n + _ball.OwnerSlot;
                BallToFeet(owner);

                int taker = -1, takerSide = -1;
                long best = (long)_controlU * _controlU;
                int other = 1 - _ball.OwnerSide;
                for (int j = 0; j < _n; j++)
                {
                    int ok = other * _n + j;
                    if (_sentOff[ok]) continue;
                    long distance = U.DistanceSq(_px[ok], _py[ok], _ball.X, _ball.Y);
                    if (distance < best)
                    {
                        best = distance;
                        taker = j;
                        takerSide = other;
                    }
                }

                // The challenge, as a DUEL (engine phase 4). It used to be one flat roll per
                // tick of contact, identical for a winger and a centre-half; now the pace of
                // duels is the config's and who wins them is the two men's — the carrier's
                // Dribbling, Technique, Strength and Pace against the challenger's Defending,
                // Positioning and Pace.
                if (taker >= 0 && tick >= _tackleLock)
                {
                    int challenger = takerSide * _n + taker;
                    int odds = DuelWinPermille(owner, challenger);
                    if (_urgent[takerSide]) odds = odds * _cfg.ChanceTacklePercent / 100;

                    if (_rng.NextInt(0, 1000) < odds)
                    {
                        _receiver[takerSide] = -1;
                        _receiver[1 - takerSide] = -1;
                        _tackleLock = tick + _cfg.TackleLockTicks;

                        // Law 12. A challenge that stops the man does not always take the ball:
                        // the worse the defender, the more often his foot arrives instead. This is
                        // what makes Defending worth having in the picture rather than only in the
                        // result model — the same challenge, made by a worse tackler, is a free
                        // kick against him.
                        if (GiveFoulIfCommitted(tick, takerSide, taker, owner)) return;

                        // Not every challenge won is a ball won. Half of them the ball simply
                        // runs loose and both of them go after it — which is where the loose
                        // balls, and the throw-ins and corners that follow them, come from.
                        if (_rng.NextInt(0, 100) < _cfg.DuelCleanTacklePercent)
                        {
                            Collect(takerSide, taker);
                            _hold[challenger] = HoldTicks(takerSide);
                        }
                        else
                        {
                            int awayX = _px[challenger] - _px[owner];
                            int awayY = _py[challenger] - _py[owner];
                            if (awayX == 0 && awayY == 0) awayX = 1;
                            int loose = U.Units(_cfg.DuelLooseBallDm);
                            _ball.Kick(takerSide, taker, awayX, awayY,
                                _ball.ForceForTicks(loose, _cfg.DribbleFlightTicks, _maxPassForce));
                            _hold[challenger] = 0;
                        }

                        ClearFlags();
                        Record(tick, BallActionKind.Tackle, takerSide == 0, taker, -1);
                    }
                }

                return;
            }

            // What the ball was DOING before anybody got to it. Taking possession zeroes its
            // velocity, so a deflection or a block has to be priced off the incoming ball before
            // that happens (engine phase 5).
            int inVx = _ball.Vx, inVy = _ball.Vy;

            // A strike the timeline has already settled is nobody's to cut out: a goal is
            // going in, a miss is going wide, and a save belongs to the keeper it was aimed at.
            //
            // EXCEPT that it can be BLOCKED (engine phase 5). A quarter of the shots in a real
            // match hit a defender, and that is where a large share of football's corners come
            // from — the engine had no way for a body to get in the way at all, which is one of
            // the two reasons it produced under two corners a match. A goal on the timeline is
            // still a goal: nothing may block that. Anything else may be charged down, the
            // timeline's own outcome is recorded either way, and where the ball goes off him is
            // then the referee's business like any other loose ball.
            if (_shotLive && _shotOutcome != MatchEventType.Goal && BlockStrike(tick, inVx, inVy)) return;

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
                    if (_sentOff[k]) continue;
                    if (keeperOnly >= 0 && (side != keeperOnly || !_keeper[k])) continue;

                    if (k == _releasedBy && tick < _releasedUntil) continue;

                    // The extra stride is for a ball nobody owns — a loose ball, a clearance, a
                    // ball cut out. It is NOT a licence to pluck a team-mate's pass out of the
                    // air a metre after it leaves his foot, which turns the move into a huddle.
                    bool ownPassInFlight = _receiver[side] >= 0 && _receiver[side] != i;
                    int reach = _controlU;
                    if (_urgent[side] && !_keeper[k] && !ownPassInFlight)
                        reach += U.Units(_cfg.ChanceReachBonusDm);

                    // The man it was played to reaches further for it than anyone else, because
                    // he is facing it and running onto it while the man behind him is turning
                    // (engine phase 4).
                    if (_receiver[side] == i) reach += U.Units(_cfg.ReceiveReachDm);

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

            // The keeper's hands (engine phase 4). A save used to end the move by definition:
            // he got to it, therefore he had it. Goalkeeping now says how often he actually
            // HOLDS it, and a fierce strike is parried more often than a tame one — which puts
            // a live ball back in his six-yard box instead of ending the attack.
            int gkSlot = bestSide * _n + bestSlot;
            if (wasShot && _shotOutcome == MatchEventType.ChanceSaved
                && bestSide != (_shotHome ? 0 : 1) && _keeper[gkSlot]
                && _rng.NextInt(0, 100) >= BallSkill.KeeperHoldPercent(_skGoalkeeping[gkSlot], _shotQuality, _cfg))
            {
                _shotLive = false;
                _receiver[0] = -1;
                _receiver[1] = -1;
                _tackleLock = tick + _cfg.TackleLockTicks;

                // Where he puts it. A keeper pushes a fierce one BEHIND as often as he pushes it
                // back into play, and that is a corner — one of the two big sources of football's
                // ten a match that this engine had no way of producing (engine phase 5).
                int outX, outY;
                if (_rng.NextInt(0, 100) < _cfg.KeeperParryBehindPercent)
                {
                    outX = -MovementGeometry.Direction(bestSide == 0) * U.Units(60);
                    outY = _ball.Y < U.CenterYU ? -U.Units(60) : U.Units(60);
                }
                else
                {
                    outX = MovementGeometry.Direction(bestSide == 0) * U.Units(80);
                    outY = _ball.Y < U.CenterYU ? -U.Units(90) : U.Units(90);
                }

                _ball.Kick(bestSide, bestSlot, outX, outY,
                    _ball.ForceForTicks(U.Units(_cfg.KeeperParryDm), _cfg.TicksOfMs(700), _maxPassForce));
                Record(tick, BallActionKind.Save, bestSide == 0, bestSlot, -1);
                return;
            }

            Collect(bestSide, bestSlot);
            _hold[bestSide * _n + bestSlot] = HoldTicks(bestSide);
            _receiver[0] = -1;
            _receiver[1] = -1;
            if (interception) _tackleLock = tick + _cfg.TackleLockTicks;

            // The flag, answered (Law 11). It only means anything now that somebody has played
            // the ball: the man it was raised against has taken it, and the whistle goes — or
            // anybody else has, and it comes down.
            if (_anyFlag)
            {
                if (!wasShot && _flagged[bestSide * _n + bestSlot])
                {
                    GiveOffside(tick, bestSide, bestSlot);
                    return;
                }

                ClearFlags();
            }

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
            //
            // ENGINE PHASE 5 made this a DEFLECTION instead of a clearance. It used to send the
            // ball up the pitch and away from the nearest touchline at a fixed force, whoever hit
            // it and whatever the ball had been doing — so a cross flicked off a defender's shin
            // came out as a tidy forty-metre ball upfield, and the corners and throw-ins that a
            // deflection produces in football never happened (0.3 corners a match against 8-13).
            // A ball that has been touched rather than controlled now CARRIES ON: the incoming
            // line, scattered sideways and sometimes turned back off him, at the share of its own
            // speed the config allows. Where it ends up is then the referee's business like any
            // other loose ball, which is exactly the point.
            if (_rng.NextInt(0, 100) < _cfg.DeflectPercent)
            {
                Deflect(tick, bestSide, bestSlot, inVx, inVy);
                return;
            }

            Record(tick, BallActionKind.Interception, bestSide == 0, bestSlot, -1);
        }

        private void ResolveOutOfPlay(int tick)
        {
            if (_ball.Dead) return;

            // A ball at a player's FEET is out the moment it crosses a line, exactly like a ball
            // running free (Law 9). Until engine phase 5 this method gave up here, so a carrier
            // who ran over the touchline was quietly clamped back inside with the ball still his
            // — the §1.7 defect, measured at fifty ticks a match by the harness's own contract
            // check, and the reason there were eighteen throw-ins a match instead of forty.
            if (!_ball.Free)
            {
                CarrierOutOfPlay(tick);
                return;
            }

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

            // Did a DEFENDER put it behind? That is the question a corner turns on (Law 17), and
            // until engine phase 5 it was asked only of the last man to kick the ball — so a
            // keeper who turned a shot round his own post was never the last toucher, the striker
            // was, and every save that left the pitch was given as a goal kick. Which is why the
            // engine produced 0.3 corners and 48 goal kicks a match against football's 8-13 and
            // 8-16: the save IS the defender's touch.
            bool defenderPutItBehind = _ball.LastTouchSide == defending;

            if (_shotLive && _shotOutcome == MatchEventType.ChanceMissed)
            {
                _shotLive = false;
                Record(tick, BallActionKind.Miss, attacking == 0, _shotSlot, -1);
            }
            else if (_shotLive && _shotOutcome == MatchEventType.ChanceSaved)
            {
                // Turned round the post or over the bar rather than gathered — still a save, and
                // the keeper's hand on it is the last touch: corner.
                _shotLive = false;
                Record(tick, BallActionKind.Save, defending == 0, KeeperOf(defending), -1);
                defenderPutItBehind = true;
            }
            else if (betweenPosts)
            {
                // On target but not a goal on the timeline: the keeper kept it out.
                _shotLive = false;
                Record(tick, BallActionKind.Save, defending == 0, KeeperOf(defending), -1);
                defenderPutItBehind = true;
            }

            if (defenderPutItBehind)
                Restart(tick, BallActionKind.Corner, attacking, goalX, crossY < U.CenterYU ? 0 : U.WidthU);
            else
                Restart(tick, BallActionKind.GoalKick, defending,
                    overHomeGoal ? U.Units(55) : U.LengthU - U.Units(55), U.CenterYU);
        }

        /// <summary>
        /// The man on the ball has run over a line. Which line he crossed and WHERE is read off
        /// his unclamped step (see <see cref="_stepToX"/>), interpolated to the crossing point
        /// the same way <see cref="MatchBall.CrossingOfX"/> does it for a loose ball — so a
        /// throw-in is taken from the spot the ball actually left the pitch rather than from
        /// wherever the man had got to by the end of the tick.
        ///
        /// He is the last man to touch it by definition, so the restart always goes the other
        /// way: a throw-in, a corner when he has carried it over his OWN line, a goal kick when
        /// he has run it over the one he is attacking.
        /// </summary>
        private void CarrierOutOfPlay(int tick)
        {
            int side = _ball.OwnerSide;
            int k = side * _n + _ball.OwnerSlot;

            // The man who has just put the ball back in play gets a moment's grace, and only he
            // does: a throw-in is taken from beside the touchline, so on the tick he collects it
            // a step of his that ends outside is him reaching for the ball rather than him
            // carrying it out. Without this the two sides trade throw-ins from the same spot.
            if (k == _restartTaker && tick < _restartGrace) return;

            int fromX = _stepFromX[k], fromY = _stepFromY[k];
            int toX = _stepToX[k], toY = _stepToY[k];
            if (toX >= 0 && toX <= U.LengthU && toY >= 0 && toY <= U.WidthU) return;

            // Which line he reached FIRST, in the fraction of the step at which he reached it.
            // A man running into a corner crosses both, and the laws care which came first.
            int firstAt = 1001;
            bool overTouchline = false;
            int line = 0;

            if (toY < 0) Earliest(fromY, toY, 0, ref firstAt, ref overTouchline, ref line, true, 0);
            else if (toY > U.WidthU) Earliest(fromY, toY, U.WidthU, ref firstAt, ref overTouchline, ref line, true, U.WidthU);

            if (toX < 0) Earliest(fromX, toX, 0, ref firstAt, ref overTouchline, ref line, false, 0);
            else if (toX > U.LengthU) Earliest(fromX, toX, U.LengthU, ref firstAt, ref overTouchline, ref line, false, U.LengthU);

            if (firstAt > 1000) return;

            int crossX = fromX + (int)((long)(toX - fromX) * firstAt / 1000);
            int crossY = fromY + (int)((long)(toY - fromY) * firstAt / 1000);

            if (overTouchline)
            {
                Restart(tick, BallActionKind.ThrowIn, 1 - side, U.ClampX(crossX), line);
                return;
            }

            // A goal line. Whose it is decides everything: his own is a corner against him, the
            // one he is attacking is a goal kick. A ball CARRIED between the posts is not a goal
            // here — the score belongs to the 1.4 result model until phase 6 — and a carrier who
            // gets that far is aiming at the goal from eleven metres out, not at the line behind
            // it (phase 4's CarryTarget), so it is a corner or a goal kick like any other.
            int defending = line == 0 ? 0 : 1;
            if (side == defending)
                Restart(tick, BallActionKind.Corner, 1 - side, line, U.ClampY(crossY) < U.CenterYU ? 0 : U.WidthU);
            else
                GoalKick(tick, defending);
        }

        /// <summary>
        /// Keeps the earliest of the boundaries a step crossed. <paramref name="at"/> is in
        /// thousandths of the step, which is exact integer arithmetic on the two endpoints.
        /// </summary>
        private static void Earliest(
            int from, int to, int boundary,
            ref int firstAt, ref bool touchline, ref int line, bool isTouchline, int lineValue)
        {
            int span = to - from;
            if (span == 0) return;

            int at = (int)((long)(boundary - from) * 1000 / span);
            if (at < 0) at = 0;
            if (at > 1000) at = 1000;
            if (at >= firstAt) return;

            firstAt = at;
            touchline = isTouchline;
            line = lineValue;
        }

        /// <summary>
        /// A body in the way. The strike is called as the thing the timeline made it — with the
        /// man who blocked it named, rather than his keeper — and the ball comes off him along the
        /// line it arrived on, which is what sends so many of them behind for a corner.
        /// </summary>
        private bool BlockStrike(int tick, int inVx, int inVy)
        {
            int attacking = _shotHome ? 0 : 1;
            int defending = 1 - attacking;
            int reach = _interceptU + U.Units(_cfg.BlockReachDm);

            for (int j = 0; j < _n; j++)
            {
                int k = defending * _n + j;
                if (_keeper[k] || _sentOff[k]) continue;
                if (U.Distance(_px[k], _py[k], _ball.X, _ball.Y) > reach) continue;
                if (_rng.NextInt(0, 1000) >= _cfg.BlockPermillePerTick) continue;

                _shotLive = false;
                if (_shotOutcome == MatchEventType.ChanceSaved)
                    Record(tick, BallActionKind.Save, defending == 0, j, -1);
                else
                    Record(tick, BallActionKind.Miss, _shotHome, _shotSlot, -1);

                Deflect(tick, defending, j, inVx, inVy);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The ball, off a man who got something on it rather than controlling it: it CARRIES ON —
        /// the line it arrived on, scattered sideways and sometimes turned straight back off him,
        /// at the share of its own speed the config allows (engine phase 5). It used to be sent
        /// tidily up the pitch at a fixed force whatever the ball had been doing, which is why a
        /// cross flicked off a shin came out as a forty-metre clearance and never as a corner.
        /// </summary>
        private void Deflect(int tick, int side, int slot, int inVx, int inVy)
        {
            int speed = U.Length(inVx, inVy);
            int force = speed * _cfg.DeflectForcePercent / 100;
            if (force < _maxPassForce / 20) force = _maxPassForce / 20;

            // In his own box he is not deflecting it, he is CLEARING it — a header or a stretched
            // boot, aimed at nothing but away from his goal, and every defender in football is
            // happy to put that one behind for a corner. This is the other big source of the ten
            // corners a real match produces, and the reason a cross into the six-yard box used to
            // come out as a tidy forty-metre clearance every time (engine phase 5).
            int dir = MovementGeometry.Direction(side == 0);
            int ownGoalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
            int depth = dir * (_ball.X - ownGoalX);

            int toTouch = _ball.Y < U.CenterYU ? _ball.Y : U.WidthU - _ball.Y;
            bool deep = depth <= U.Units(_cfg.ClearBehindDepthDm);
            bool nearTouch = toTouch <= U.Units(_cfg.ClearIntoTouchDm);

            if (deep || nearTouch)
            {
                int firm = _maxPassForce * _cfg.DeflectClearForcePercent / 100;
                int roll = _rng.NextInt(0, 1000);

                if (deep && roll < _cfg.DeflectBehindPermille)
                {
                    // Over his own line: the corner every defender in football is happy to give.
                    int behindX = ownGoalX - dir * U.Units(60) - _ball.X;
                    int behindY = (_ball.Y < U.CenterYU
                        ? U.CenterYU - U.Units(_cfg.ClearBehindOffCentreDm)
                        : U.CenterYU + U.Units(_cfg.ClearBehindOffCentreDm)) - _ball.Y;
                    _ball.Kick(side, slot, behindX, behindY, firm);
                }
                else if (nearTouch && roll < _cfg.DeflectBehindPermille + _cfg.DeflectIntoTouchPermille)
                {
                    // Or into touch — a man stretching for a ball with a touchline behind him puts
                    // it out of play, wherever on the pitch he is doing it.
                    int outY = (_ball.Y < U.CenterYU ? -U.Units(60) : U.WidthU + U.Units(60)) - _ball.Y;
                    _ball.Kick(side, slot, dir * U.Units(40), outY, firm);
                }
                else
                {
                    int upX = dir * U.Units(200);
                    int upY = _rng.NextInt(-U.Units(200), U.Units(200) + 1);
                    _ball.Kick(side, slot, upX, upY, firm);
                }

                _hold[side * _n + slot] = 0;
                Record(tick, BallActionKind.Clearance, side == 0, slot, -1);
                return;
            }

            int alongPermille = _rng.NextInt(-_cfg.DeflectBackPermille, 1001);
            int acrossPermille = _rng.NextInt(-_cfg.DeflectSpreadPermille, _cfg.DeflectSpreadPermille + 1);
            int dx = (int)((long)inVx * alongPermille / 1000) - (int)((long)inVy * acrossPermille / 1000);
            int dy = (int)((long)inVy * alongPermille / 1000) + (int)((long)inVx * acrossPermille / 1000);
            if (dx == 0 && dy == 0) dx = dir;

            _ball.Kick(side, slot, dx, dy, force);
            _hold[side * _n + slot] = 0;
            Record(tick, BallActionKind.Clearance, side == 0, slot, -1);
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
                    // Blocked or beaten away without ever reaching a line: a defender's touch was
                    // the last one on it, so the attacking side keeps the ball — a corner.
                    Record(tick, BallActionKind.Save, defending == 0, KeeperOf(defending), -1);
                    Corner(tick, attacking);
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

        /// <summary>A corner to the attacking side, on the side of the goal the ball was nearest.</summary>
        private void Corner(int tick, int attacking)
        {
            int goalX = U.Units(MovementGeometry.AttackedGoalX(attacking == 0));
            Restart(tick, BallActionKind.Corner, attacking, goalX, _ball.Y < U.CenterYU ? 0 : U.WidthU);
        }

        private void GoalKick(int tick, int defending)
        {
            int spot = defending == 0 ? U.Units(55) : U.LengthU - U.Units(55);
            Restart(tick, BallActionKind.GoalKick, defending, spot, U.CenterYU);
        }

        private void Restart(int tick, BallActionKind kind, int side, int x, int y)
        {
            _shotLive = false;
            ClearFlags();

            // Put back in play from a spot that is ON the pitch. The laws have a throw-in taken
            // from the touchline and a corner from the corner arc — with the taker standing OFF
            // the field of play, which a top-down picture of twenty-two dots has nowhere to put.
            // So the ball goes down a stride inside the line instead: the same spot to the eye,
            // and it keeps "a held ball is never sitting on a line of the pitch" a real invariant
            // instead of a check that fires every time the referee gets a restart right.
            _ball.Place(
                MovementGeometry.Clamp(x, _insetU, U.LengthU - _insetU),
                Inside(y));
            _ball.Dead = true;
            _ball.OwnerSide = -1;
            _ball.OwnerSlot = -1;
            _receiver[0] = -1;
            _receiver[1] = -1;

            _deadKind = kind;
            _deadSide = side;
            _deadTaker = kind == BallActionKind.GoalKick
                ? KeeperOf(side)
                : kind == BallActionKind.Penalty
                    ? BestStriker(side)
                    : NearestTo(side, _ball.X, _ball.Y, includeKeeper: false);
            _deadAt = tick + _cfg.DeadBallTicks;

            // And the wall, decided here and once (Law 13): the three men nearest the ball at the
            // whistle, when the kick is inside shooting range of the goal they are defending.
            int defending = 1 - side;
            int defendedGoalX = U.Units(MovementGeometry.OwnGoalX(defending == 0));
            bool walled = kind == BallActionKind.FreeKick
                && U.Distance(_ball.X, _ball.Y, defendedGoalX, U.CenterYU) < U.Units(_cfg.MaxShootRangeDm);
            FormWall(walled ? defending : -1);

            Record(tick, kind, side == 0, _deadTaker, -1);
        }

        // ------------------------------------------------------------------ the laws (phase 5)

        /// <summary>
        /// The offside line for the side in possession, in DEPTH — how far up the pitch it is,
        /// measured in that side's own attacking direction, so one formula serves both sides.
        ///
        /// Law 11: the line is the second-rearmost opponent, and a man is only offside if he is
        /// also ahead of the ball and in the opponents' half. The keeper is one of the two, which
        /// is why it is the SECOND-rearmost and not simply the last defender.
        /// </summary>
        private int OffsideLineDepth(int side)
        {
            int dir = MovementGeometry.Direction(side == 0);
            int opponent = 1 - side;

            // The two opponents nearest their own goal, i.e. with the greatest depth.
            int first = int.MinValue, second = int.MinValue;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (_sentOff[ok]) continue;
                int depth = dir * _px[ok];
                if (depth > first) { second = first; first = depth; }
                else if (depth > second) second = depth;
            }

            if (second == int.MinValue) second = first;

            int ball = dir * _ball.X;
            if (ball > second) second = ball;

            int halfway = dir * U.CenterXU;
            if (halfway > second) second = halfway;
            return second;
        }

        /// <summary>
        /// Where THIS player thinks the line is. The whole model of why offsides happen: the man
        /// on the ball plays what he believes is on, the referee judges what actually was, and
        /// the gap between the two is the flag. A poor reader of the game (Positioning) is out by
        /// several metres either way; a good one is barely out at all — which is why an offside
        /// is a mistake by the passer and his runner rather than a dice roll.
        /// </summary>
        private int PerceivedOffsideLine(int k, int trueDepth)
        {
            int span = _cfg.OffsideJudgementDm - _cfg.OffsideJudgementFloorDm;
            if (span < 0) span = 0;
            int judgement = _cfg.OffsideJudgementFloorDm
                            + span * (100 - BallSkill.Clamp(_skPositioning[k], 1, 100)) / 100;
            int error = (_rng.NextInt(-judgement, judgement + 1) + _rng.NextInt(-judgement, judgement + 1)) / 2;
            return trueDepth + U.Units(error);
        }

        /// <summary>Raises the flag on every man of the passing side who was beyond the line.</summary>
        private void FlagOffside(int side, int passer)
        {
            int dir = MovementGeometry.Direction(side == 0);
            int line = OffsideLineDepth(side) + U.Units(_cfg.OffsideMarginDm);

            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (i == passer || _sentOff[k]) continue;
                if (dir * _px[k] <= line) continue;

                _flagged[k] = true;
                _flaggedX[k] = _px[k];
                _flaggedY[k] = _py[k];
                _anyFlag = true;
            }
        }

        /// <summary>
        /// Was the challenge a foul? If it was, everything that follows from it is done here: the
        /// whistle, the card, and the free kick or the penalty. Returns true when the game has
        /// been stopped, in which case the caller must not go on resolving the ball.
        /// </summary>
        private bool GiveFoulIfCommitted(int tick, int side, int slot, int carrier)
        {
            int k = side * _n + slot;
            bool inOwnBox = MovementGeometry.InOwnBox(side == 0, U.Dm(_ball.X), U.Dm(_ball.Y));

            int odds = BallSkill.Lerp(
                BallSkill.Clamp(_skDefending[k], 1, 100), 100,
                _cfg.FoulPermilleOfChallengesWorst, _cfg.FoulPermilleOfChallengesBest);

            // In his own box he stays on his feet. It is why penalties are rare without being
            // impossible, and it is a real thing defenders do rather than a knob invented to keep
            // the count down.
            if (inOwnBox) odds = odds * _cfg.FoulInBoxPermille / 1000;
            if (_rng.NextInt(0, 1000) >= odds) return false;

            int victimSide = 1 - side;
            int victimSlot = carrier - victimSide * _n;
            int spotX = _ball.X, spotY = _ball.Y;
            bool cynical = StoppedAnAttack(victimSide, victimSlot);

            ClearFlags();
            Record(tick, BallActionKind.Foul, side == 0, slot, victimSlot);

            // The card. A second yellow is a red by the law and not by a knob, so a man already
            // booked who fouls cynically again walks.
            int yellow = _cfg.YellowPercentOfFouls;
            if (cynical) yellow = yellow * _cfg.YellowCynicalPercent / 100;
            if (_booked[k]) yellow = yellow * _cfg.BookedCarePercent / 100;
            bool straightRed = _rng.NextInt(0, 1000) < _cfg.RedPermilleOfFouls;
            bool booked = _rng.NextInt(0, 100) < yellow;

            if (straightRed || (booked && _booked[k]))
            {
                _booked[k] = true;
                _sentOff[k] = true;
                _tenMen[side]++;
                Record(tick, BallActionKind.RedCard, side == 0, slot, -1);
            }
            else if (booked)
            {
                _booked[k] = true;
                Record(tick, BallActionKind.YellowCard, side == 0, slot, -1);
            }

            if (inOwnBox)
            {
                int ownGoalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
                int spot = side == 0
                    ? ownGoalX + U.Units(_cfg.PenaltySpotDm)
                    : ownGoalX - U.Units(_cfg.PenaltySpotDm);
                Restart(tick, BallActionKind.Penalty, victimSide, spot, U.CenterYU);
            }
            else
            {
                Restart(tick, BallActionKind.FreeKick, victimSide, spotX, spotY);
            }

            // A stoppage: the referee has a word, the wall is walked back, the ball is placed.
            _deadAt += _cfg.FoulStoppageTicks;
            return true;
        }

        /// <summary>
        /// Was that foul cynical — a man stopped while he was running at a defence with hardly
        /// anybody left between him and the goal? It is what turns a foul into a booking, and it
        /// is read off the pitch rather than rolled for.
        /// </summary>
        private bool StoppedAnAttack(int side, int slot)
        {
            int k = side * _n + slot;
            int dir = MovementGeometry.Direction(side == 0);
            if (dir * _px[k] <= dir * U.CenterXU) return false;

            int opponent = 1 - side;
            int goalSide = 0;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (_sentOff[ok] || _keeper[ok]) continue;
                if (dir * _px[ok] > dir * _px[k]) goalSide++;
            }

            return goalSide <= 2;
        }

        /// <summary>
        /// A penalty (Law 14). The score still belongs to the 1.4 result model until phase 6, so
        /// the strike CANNOT invent a goal: if the side's next chance on the timeline is close
        /// enough to be honest about, the penalty IS that chance, taken now and with its outcome —
        /// which is the very mechanism the director already uses to keep the picture and the
        /// scoresheet the same match. When there is no chance to claim, the keeper saves it or it
        /// goes wide. That residue is phase 6's, and it is written down as such.
        /// </summary>
        private void TakePenalty(int tick, int side, int slot)
        {
            MatchEventType outcome;
            int credit = slot;

            if (_director.TryCurrent(tick, out MatchDirector.Chance chance) && chance.Home == (side == 0))
            {
                outcome = chance.Outcome;
                credit = chance.Slot;
                _director.MarkTaken();
            }
            else
            {
                outcome = _rng.NextInt(0, 100) < _cfg.PenaltySavedPercent
                    ? MatchEventType.ChanceSaved
                    : MatchEventType.ChanceMissed;
            }

            if (_shotLive) ForceResolveShot(tick);
            TakeShot(tick, side, slot, outcome, credit);
        }

        /// <summary>
        /// Where a defender must stand while a free kick or a penalty is being taken (Laws 13 and
        /// 14): nine and a bit metres away, and out of the penalty area for a penalty. The men
        /// nearest the ball form the WALL when the kick is within range of their goal — on the
        /// line between the ball and the middle of it, shoulder to shoulder.
        /// </summary>
        private bool RetreatSpot(int side, int slot, out int x, out int y)
        {
            x = 0;
            y = 0;
            int k = side * _n + slot;
            _inWall[k] = false;
            if (!_ball.Dead || _deadSide == side) return false;
            if (_deadKind != BallActionKind.FreeKick && _deadKind != BallActionKind.Penalty) return false;
            if (_keeper[k]) return false;

            // A penalty: everybody but the taker and the keeper waits outside the box.
            if (_deadKind == BallActionKind.Penalty)
            {
                int ownGoalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
                int dir = MovementGeometry.Direction(side == 0);
                int edge = ownGoalX + dir * U.Units(MovementGeometry.BoxDepthDm + 20);
                if (dir * _px[k] >= dir * edge) return false;
                x = edge;
                y = _py[k];
                return true;
            }

            int retreat = U.Units(_cfg.FreeKickRetreatDm);
            int away = U.Distance(_px[k], _py[k], _ball.X, _ball.Y);

            // The wall — the three men picked at the whistle, each keeping the place he was given.
            int goalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
            int place = WallPlace(slot);

            if (place >= 0)
            {
                _inWall[k] = true;
                int dx = goalX - _ball.X, dy = U.CenterYU - _ball.Y;
                int span = U.Length(dx, dy);
                if (span <= 0) span = 1;

                int alongX = _ball.X + (int)((long)dx * retreat / span);
                int alongY = _ball.Y + (int)((long)dy * retreat / span);

                // Shoulder to shoulder ACROSS the line of the kick, centred on it.
                int step = U.Units(_cfg.WallSpacingDm);
                int offset = (place - (_cfg.WallMen - 1) / 2) * step;
                x = U.ClampX(alongX + (int)((long)(-dy) * offset / span));
                y = Inside(alongY + (int)((long)dx * offset / span));
                return true;
            }

            if (away >= retreat) return false;

            // Anybody else simply retires the required distance, straight back from the ball.
            int ox = _px[k] - _ball.X, oy = _py[k] - _ball.Y;
            if (ox == 0 && oy == 0) ox = MovementGeometry.Direction(side == 0);
            U.Scaled(ox, oy, retreat, out int rx, out int ry);
            x = U.ClampX(_ball.X + rx);
            y = Inside(_ball.Y + ry);
            return true;
        }

        /// <summary>
        /// Who makes up the wall, decided ONCE when the free kick is given — the three men nearest
        /// the ball at the whistle, and they keep their places.
        ///
        /// Re-ranking them every tick, which is what this replaced, made the wall chase itself: two
        /// men swap places as they run, so each sets off for the spot the other has just left and
        /// neither arrives. Deciding it at the whistle is also what happens on a pitch — the
        /// referee walks THOSE men back — and it is one O(n) pass a restart instead of one per man
        /// per tick.
        /// </summary>
        private void FormWall(int side)
        {
            for (int w = 0; w < _wall.Length; w++) _wall[w] = -1;
            if (side < 0) return;

            for (int w = 0; w < _cfg.WallMen && w < _wall.Length; w++)
            {
                int best = -1;
                long bestDistance = long.MaxValue;
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    if (_keeper[k] || _sentOff[k] || InWall(i)) continue;
                    long d = U.DistanceSq(_px[k], _py[k], _ball.X, _ball.Y);
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }

                if (best < 0) break;
                _wall[w] = best;
            }
        }

        /// <summary>His place in the wall, or -1 if he is not in it.</summary>
        private int WallPlace(int slot)
        {
            for (int w = 0; w < _wall.Length; w++)
                if (_wall[w] == slot) return w;
            return -1;
        }

        private bool InWall(int slot) => WallPlace(slot) >= 0;

        private void ClearFlags()
        {
            if (!_anyFlag) return;
            for (int i = 0; i < _flagged.Length; i++) _flagged[i] = false;
            _anyFlag = false;
        }

        /// <summary>
        /// The flag goes up: an indirect free kick to the other side, from the place the man was
        /// standing when the ball was played to him — not from where he has run to since, which
        /// is the whole reason his position is remembered at the kick.
        /// </summary>
        private void GiveOffside(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            int x = _flaggedX[k], y = _flaggedY[k];
            ClearFlags();

            Record(tick, BallActionKind.Offside, side == 0, slot, -1);
            Restart(tick, BallActionKind.FreeKick, 1 - side, x, y);
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
                if (_sentOff[k]) continue;
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

        /// <summary>Who steps up for a penalty: the best striker of a ball still on the pitch.</summary>
        private int BestStriker(int side)
        {
            int best = -1, bestSkill = -1;
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (_keeper[k] || _sentOff[k]) continue;
                int skill = BallSkill.Mix(_skShooting[k], 7, _skTechnique[k], 3);
                if (skill > bestSkill) { bestSkill = skill; best = i; }
            }

            return best >= 0 ? best : KeeperOf(side);
        }

        private int MostAdvanced(int side)
        {
            int best = 0, bestForward = -1;
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (_sentOff[k]) continue;
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
