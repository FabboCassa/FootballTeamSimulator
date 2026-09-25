using System.Collections.Generic;
using Sim.Core.Condition;
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
    /// AND THE SCORE IS ITS OWN (engine phase 6). Until this phase the result belonged to
    /// the 1.4 minute model and a director worked the ball toward a shooter the timeline had
    /// already elected, switching on super-powers to get it there. That is gone. A man on the
    /// ball now weighs the SHOT against the pass, the run and the clearance in the one currency
    /// they are all quoted in, strikes it where his Shooting and Technique let him, and the ball
    /// decides: a body in the way blocks it, the keeper's dive reaches it or does not, and a goal
    /// is the ball crossing the line between the posts. Nothing scripts a single touch, and the
    /// report this fills in IS the match that was played.
    ///
    /// Deterministic: integer arithmetic in sixteenths of a decimetre, a fixed iteration
    /// order, and every random draw taken from the seeded source in that same order.
    /// </summary>
    public sealed partial class MatchSimulator
    {
        private const int SideCount = 2;

        private readonly MatchBalance _cfg;

        /// <summary>
        /// What each man decides and where he goes. <see cref="MatchBalance.Brain"/> picks it once;
        /// the ball, the laws and the executions below serve whichever brain plays.
        /// </summary>
        private readonly IMatchBrain _brain;

        private IRandomSource _rng = null!;
        private MatchBall _ball = null!;
        private PositionStream _stream = new PositionStream();

        /// <summary>The report this match fills in. Since phase 6 the pitch writes the score.</summary>
        private MatchReport _report = null!;
        private Lineup _home = null!;
        private Lineup _away = null!;
        private MatchInputFeed? _feed;

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

        /// <summary>Until when a ball played backwards keeps the opposing press switched on.</summary>
        private readonly int[] _pressUntil = new int[SideCount];

        /// <summary>Where each player was when he gained the ball, and whether his run has been called.</summary>
        private int[] _gotBallX = System.Array.Empty<int>();
        private int[] _gotBallY = System.Array.Empty<int>();
        private bool[] _runCalled = System.Array.Empty<bool>();

        private readonly MovementTactics[] _tactics = new MovementTactics[SideCount];

        /// <summary>How many lines each side's formation stands in.</summary>
        private readonly int[] _lineCount = new int[SideCount];
        private readonly int[] _receiver = new int[SideCount];
        private readonly int[] _receiveX = new int[SideCount];
        private readonly int[] _receiveY = new int[SideCount];

        /// <summary>The tick the first half ends on, and which side kicked the match off.</summary>
        private int _halfTime;
        private int _kickedOffFirst;
        private bool _secondHalf;

        // ------------------------------------------------------------------ the referee (phase 5)

        // The dead ball, the strike in flight and the laws live in their own classes and share
        // the pitch through one context, built per match in Setup.
        private MatchContext _ctx = null!;
        private MatchScoresheet _sheet = null!;
        private Offside _offside = null!;
        private FreeKickWall _freeKickWall = null!;
        private Restarts _restarts = null!;
        private Referee _referee = null!;
        private OutOfPlay _outOfPlay = null!;

        /// <summary>Sent off (a second yellow or a straight red): he leaves the field of play.</summary>
        private bool[] _sentOff = System.Array.Empty<bool>();

        /// <summary>
        /// Whether the strike, as it actually left his foot, is going between the posts. It is
        /// the one thing about a shot that has to be settled the moment it is struck rather than
        /// when it arrives, because it decides WHOSE ball it is on the way: an effort on target is
        /// the keeper's to deal with, and one flying wide is nobody's — a keeper does not gather
        /// a ball going a metre past his post, he watches it go out for a goal kick.
        /// </summary>
        private bool _shotOnTarget;

        /// <summary>No second challenge for a moment after one is won, or the ball ping-pongs.</summary>
        private int _tackleLock;

        /// <summary>When a live strike must have become a goal, a save or a ball out of play.</summary>
        private int _shotExpires;

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

        // What a man is worth TODAY rather than on paper (engine phase 6). The cached skills
        // above are the scaled ones — his attributes times this — so every decision, every duel
        // and every stride already reads the player as he is this afternoon, and nothing at the
        // call sites had to learn about it. Condition and home advantage are fixed for the match;
        // tiredness is folded in at each minute boundary, which is when the skills are rebuilt.
        private int[] _scalePermille = System.Array.Empty<int>();
        private int[] _stamina = System.Array.Empty<int>();

        /// <summary>The last man of each side to touch the ball: the one a goal is credited to.</summary>
        private readonly int[] _lastTouch = new int[SideCount];

        private readonly ConditionBalance _condition;
        private readonly bool _applyCondition;
        private readonly bool _applyMatchFatigue;

        /// <summary>How good the strike in the air was, for the keeper who has to deal with it.</summary>
        private int _shotQuality;
        private int _pressureU;

        public MatchSimulator(MatchBalance cfg)
            : this(cfg, new ConditionBalance(), false, false)
        {
        }

        /// <summary>
        /// The watched match reads the same three things the result model always read — the home
        /// side's advantage, each man's condition and how he tires — because since phase 6 it IS
        /// the result. A neutral squad with both flags off is the identity, so a caller that opts
        /// into nothing gets the attributes exactly as they are written.
        /// </summary>
        public MatchSimulator(MatchBalance cfg, ConditionBalance condition, bool applyCondition, bool applyMatchFatigue)
        {
            _cfg = cfg;
            _condition = condition;
            _applyCondition = applyCondition;
            _applyMatchFatigue = applyMatchFatigue;
            _brain = cfg.Brain switch
            {
                MatchBrainVersion.V10 => new V10Brain(this),
                MatchBrainVersion.V11 => new V11Brain(this),
                _ => throw new System.ArgumentOutOfRangeException(
                    nameof(cfg), cfg.Brain, "Unknown match brain.")
            };
        }

        // ------------------------------------------------------------------ entry point

        /// <summary>
        /// Plays the match. <paramref name="report"/> comes in carrying the two club ids and goes
        /// out carrying the SCORE AND THE EVENTS the pitch produced (engine phase 6);
        /// <paramref name="feed"/> is the kickoff input plus whatever substitutions and tactical
        /// changes are due, asked for at each minute boundary against the live score.
        /// </summary>
        public PositionStream Generate(MatchInputFeed feed, MatchReport report, IRandomSource rng)
        {
            _feed = feed;
            return Generate(feed.Current.Home, feed.Current.Away, report, rng, feed.Current.Tactics);
        }

        public PositionStream Generate(
            Lineup home, Lineup away, MatchReport report, IRandomSource rng, MatchTactics? tactics)
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
                if (t == lastTick && _ctx.ShotLive) ForceResolveShot(t);

                // The physics runs at 10 Hz; the replay does not need to (engine phase 1). Only
                // every StreamTicksPerFrame-th tick is written, which is what keeps the stream
                // the size of a replay instead of the size of the simulation.
                if (t % _streamStride == 0) WriteFrame(t / _streamStride);
            }

            return _stream;
        }

        private void Tick(int tick)
        {
            // The minute boundary: the legs get a little heavier, and whatever the bench has
            // decided by now comes on. Both are read off the LIVE score, which since phase 6 is
            // the score the pitch has produced.
            if (tick % _cfg.TicksPerMinute == 0) MinuteBoundary(tick);

            _brain.UpdateTeams(tick);

            for (int side = 0; side < SideCount; side++)
                for (int slot = 0; slot < _n; slot++)
                    _brain.Act(tick, side, slot);

            for (int side = 0; side < SideCount; side++)
                for (int slot = 0; slot < _n; slot++)
                    _brain.Move(tick, side, slot);

            _ball.Advance();
            ResolveControl(tick);
            _outOfPlay.ResolveOutOfPlay(tick);
            ResolveStuckShot(tick);

            // HALF TIME (Law 7). The whistle at the end of the first half, and the second half
            // kicked off from the centre spot by the side that did NOT kick off the first — both
            // sides behind the halfway line, which the block already knows how to do for a
            // kickoff. The ENDS are not swapped: see the note on MatchBalance.HalfTimeMs.
            // The whistle WAITS: not while a strike is in the air, and not while the timeline has a
            // chance due — a referee does not blow for half-time with the ball in the box, and the
            // director would un-dead the ball for the strike and cancel the restart if he did.
            if (!_secondHalf && tick >= _halfTime && !_ctx.ShotLive)
            {
                _secondHalf = true;
                HalfTime(tick);
            }

            // Celebration over: the ball goes back to the centre spot for the kickoff.
            if (_ball.Dead && _ctx.DeadKind == BallActionKind.Goal && tick >= _ctx.DeadAt)
            {
                int conceding = _ctx.DeadSide;
                _ball.Place(U.CenterXU, U.CenterYU);
                _ctx.DeadKind = BallActionKind.Kickoff;
                _ctx.DeadTaker = MostAdvanced(conceding);
                _ctx.DeadAt = tick + _cfg.DeadBallTicks;
                _sheet.Record(tick, BallActionKind.Kickoff, conceding == 0, _ctx.DeadTaker, -1);
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
            if (_ctx.ShotLive) ForceResolveShot(tick);

            _offside.ClearFlags();
            _receiver[0] = -1;
            _receiver[1] = -1;
            _releasedBy = -1;
            _releasedUntil = -1;
            _ctx.RestartTaker = -1;
            _tackleLock = tick;

            _sheet.Record(tick, BallActionKind.HalfTime, _kickedOffFirst == 0, -1, -1);

            int kicking = 1 - _kickedOffFirst;
            _ball.Place(U.CenterXU, U.CenterYU);
            _ball.Dead = true;
            _ball.OwnerSide = -1;
            _ball.OwnerSlot = -1;
            _ball.LastTouchSide = 1 - kicking;

            _ctx.DeadKind = BallActionKind.Kickoff;
            _ctx.DeadSide = kicking;
            _ctx.DeadTaker = MostAdvanced(kicking);
            _ctx.DeadAt = tick + _cfg.HalfTimeTicks;
            _sheet.Record(tick, BallActionKind.Kickoff, kicking == 0, _ctx.DeadTaker, -1);
        }

        // ------------------------------------------------------------------ setup

        private void Setup(Lineup home, Lineup away, MatchReport report, MatchTactics? tactics, int lastTick)
        {
            _report = report;
            _home = home;
            _away = away;
            _lastTouch[0] = -1;
            _lastTouch[1] = -1;
            _n = home.Slots.Count;
            int total = _n * SideCount;
            _streamStride = _cfg.StreamTicksPerFrame < 1 ? 1 : _cfg.StreamTicksPerFrame;
            _lastFrame = lastTick / _streamStride;
            int frames = _lastFrame + 1;

            _px = new int[total];
            _py = new int[total];
            _vx = new int[total];
            _vy = new int[total];
            _sentOff = new bool[total];
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
            _scalePermille = new int[total];
            _stamina = new int[total];

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

            _sheet = new MatchScoresheet(_cfg, _stream, report, _streamStride, _lastFrame);
            _ctx = new MatchContext(
                _cfg, _rng, _ball, _sheet, _n, _px, _py, _keeper, _sentOff,
                _skShooting, _skTechnique, _receiver, _lastTouch);
            _offside = new Offside(_ctx, _skPositioning);
            _freeKickWall = new FreeKickWall(_ctx);
            _restarts = new Restarts(_ctx, _offside, _freeKickWall, WallRangeDm);
            _referee = new Referee(_ctx, _restarts, _offside, _skDefending);
            _outOfPlay = new OutOfPlay(_ctx, _restarts, _stepFromX, _stepFromY, _stepToX, _stepToY);

            _tactics[0] = MovementTactics.From(tactics?.Home, _cfg);
            _tactics[1] = MovementTactics.From(tactics?.Away, _cfg);
            SetTempo(tactics);

            // On the centre spot before anybody takes up a position: the block is built around
            // the ball, and a ball still sitting at the origin drags all twenty-two men onto one
            // touchline for the kickoff frame.
            _ball.Place(U.CenterXU, U.CenterYU);

            _ctx.DeadKind = BallActionKind.Kickoff;
            _ball.Dead = true;

            for (int side = 0; side < SideCount; side++)
            {
                Lineup lineup = side == 0 ? home : away;
                var roles = new List<PositionRole>(_n);
                for (int i = 0; i < _n; i++) roles.Add(lineup.Slots[i].Role);

                for (int i = 0; i < _n; i++)
                {
                    BindPlace(side, i, lineup, roles);
                    BindSkills(side, i, lineup, 1000);
                }

                _receiver[side] = -1;
            }

            // Both sides take up their places, in the brain's own shape.
            _brain.Begin();

            // Kick off: the home side's most advanced man takes it from the centre spot.
            _halfTime = lastTick / 2;
            _kickedOffFirst = 0;
            _secondHalf = false;
            _ball.LastTouchSide = 1;
            _ctx.DeadSide = 0;
            _ctx.DeadTaker = MostAdvanced(0);
            _ctx.DeadAt = _cfg.DeadBallTicks;
            _sheet.Record(0, BallActionKind.Kickoff, true, _ctx.DeadTaker, -1);
        }

        /// <summary>
        /// His place in the SHAPE: which line he stands in, how far across it he stands, and — if
        /// the user has dragged him off his anchor (task 6.10) — how far off his own line he has
        /// been put. Free positioning is a displacement FROM the line rather than a position on
        /// the pitch, so a man moved forward ten metres stays ten metres in front of his line
        /// wherever the block goes.
        /// </summary>
        private void BindPlace(int side, int i, Lineup lineup, List<PositionRole> roles)
        {
            int k = side * _n + i;
            SlotPosition? custom = lineup.Slots[i].Position;

            _lineRank[k] = FormationGeometry.LineRank(roles, i, _cfg, out int lines);
            _lineCount[side] = lines;
            _baseY[k] = custom.HasValue ? custom.Value.YPermille : FormationGeometry.AnchorY(roles, i, _cfg);
            _forward[k] = FormationGeometry.AnchorX(roles[i], _cfg);
            _offsetXDm[k] = custom.HasValue
                ? (custom.Value.XPermille - _forward[k]) * Pitch.LengthDm / 1000
                : 0;
            _keeper[k] = roles[i] == PositionRole.Goalkeeper;
        }

        /// <summary>
        /// What he is worth with the ball, and how fast he can run — his attributes scaled by
        /// what the afternoon has done to them (engine phase 6). <paramref name="fatiguePermille"/>
        /// is the only part that moves during the match, which is why this is cheap enough to
        /// redo for twenty-two men at every minute boundary.
        ///
        /// A footballer's top speed is 5.5 to 8.5 m/s and Pace spans that band; what he does off
        /// the ball is a jog, not a sprint, which is why cruising is a separate figure rather
        /// than "top speed unless sprinting".
        /// </summary>
        private void BindSkills(int side, int i, Lineup lineup, int fatiguePermille)
        {
            int k = side * _n + i;
            PlayerAttributes attributes = lineup.Slots[i].Player.Attributes;
            _stamina[k] = attributes.Stamina;

            // Condition and home advantage are the match's, and settled once. Recomputing the
            // condition multiplier every minute would be twenty-two doubles a minute for a number
            // that cannot have changed.
            if (fatiguePermille == 1000 || _scalePermille[k] == 0)
            {
                int fixedScale = 1000;
                if (_applyCondition)
                    fixedScale = (int)(ConditionModel.PerformanceMultiplier(
                        lineup.Slots[i].Player.Condition, _condition) * 1000);
                if (side == 0)
                    fixedScale = fixedScale * (1000 + _cfg.PitchHomeAdvantagePermille) / 1000;
                _scalePermille[k] = fixedScale < 1 ? 1 : fixedScale;
            }

            int scale = _scalePermille[k] * fatiguePermille / 1000;

            _skPassing[k] = Rate(attributes.Passing, scale);
            _skTechnique[k] = Rate(attributes.Technique, scale);
            _skDribbling[k] = Rate(attributes.Dribbling, scale);
            _skDefending[k] = Rate(attributes.Defending, scale);
            _skPositioning[k] = Rate(attributes.Positioning, scale);
            _skStrength[k] = Rate(attributes.Strength, scale);
            _skShooting[k] = Rate(attributes.Shooting, scale);
            _skGoalkeeping[k] = Rate(attributes.Goalkeeping, scale);
            _skPace[k] = Rate(attributes.Pace, scale);
            _vision[k] = BallSkill.VisionPercent(_skPositioning[k], _cfg);

            int topDmPerSecond = _cfg.PlayerTopSpeedDmPerSecond
                                 + _cfg.PlayerTopSpeedPaceDmPerSecond * _skPace[k] / 100;
            _maxSpeed[k] = U.PerTick(topDmPerSecond, _cfg);
            if (_maxSpeed[k] < 1) _maxSpeed[k] = 1;
            _cruise[k] = _maxSpeed[k] * _cfg.PlayerCruisePercent / 100;
            if (_cruise[k] < 1) _cruise[k] = 1;
            _accel[k] = U.PerTickPerTick(_cfg.PlayerAccelDmPerSecond2, _cfg);
            if (_accel[k] < 1) _accel[k] = 1;
        }

        private static int Rate(int attribute, int scalePermille)
            => BallSkill.Clamp(attribute * scalePermille / 1000, 1, 100);

        /// <summary>
        /// The minute has turned. Two things happen, in this order and neither of them random:
        /// the bench is asked what it wants (a scheduled substitution, or a conditional rule that
        /// the live score has just opened), and every man's legs are re-rated for how far into
        /// the match he is and how much Stamina he has to spend on it.
        /// </summary>
        private void MinuteBoundary(int tick)
        {
            int minute = tick / _cfg.TicksPerMinute;

            if (_feed != null && _feed.Advance(minute, _report.HomeGoals, _report.AwayGoals))
            {
                _home = _feed.Current.Home;
                _away = _feed.Current.Away;
                _tactics[0] = MovementTactics.From(_feed.Current.Tactics?.Home, _cfg);
                _tactics[1] = MovementTactics.From(_feed.Current.Tactics?.Away, _cfg);
                SetTempo(_feed.Current.Tactics);

                for (int side = 0; side < SideCount; side++)
                {
                    Lineup lineup = side == 0 ? _home : _away;
                    var roles = new List<PositionRole>(_n);
                    for (int i = 0; i < _n; i++) roles.Add(lineup.Slots[i].Role);
                    int[] shirts = ShirtNumbers.For(lineup);
                    for (int i = 0; i < _n; i++)
                    {
                        int k = side * _n + i;
                        // A fresh man in that slot: he is a different player, so his fixed scale
                        // is stale, and the stream has to carry HIS id — the replay names the man
                        // who came on, not the man he came on for. A sent-off shirt is never
                        // refilled: the eleven that is down to ten stays down to ten.
                        int[] ids = side == 0 ? _stream.HomePlayerIds : _stream.AwayPlayerIds;
                        if (lineup.Slots[i].Player.Id != ids[i] && !_sentOff[k])
                        {
                            // ENGINE PHASE 7. The id array is overwritten here — the replay names
                            // the man who is ON the pitch — so the change itself is written down
                            // before it is lost. Without it a substitute's line of the match
                            // report says he played ninety minutes, and the man he came on for
                            // does not appear at all.
                            int[] sideShirts = side == 0 ? _stream.HomeShirts : _stream.AwayShirts;
                            _stream.Changes.Add(new SlotChange(
                                _sheet.MinuteFrame(tick), side == 0, i,
                                lineup.Slots[i].Player.Id, ids[i], shirts[i], sideShirts[i]));

                            _scalePermille[k] = 0;
                            ids[i] = lineup.Slots[i].Player.Id;
                            sideShirts[i] = shirts[i];
                        }

                        BindPlace(side, i, lineup, roles);
                    }
                }
            }

            for (int side = 0; side < SideCount; side++)
            {
                Lineup lineup = side == 0 ? _home : _away;
                for (int i = 0; i < _n; i++)
                    BindSkills(side, i, lineup, FatiguePermille(minute, _stamina[side * _n + i]));
            }
        }

        /// <summary>
        /// How much of himself he still has, in permille. Tiredness builds toward
        /// <see cref="ConditionBalance.MatchFatigueAt90Permille"/> by full time, steps back at the
        /// break, and is scaled by his own Stamina — so a low-stamina man fades and a high-stamina
        /// one barely does. It is the result model's own curve
        /// (<c>MatchEngine.FatigueFactor</c>), read one player at a time instead of one team at a
        /// time, which is the whole gain of the causality being on the pitch.
        /// </summary>
        private int FatiguePermille(int minute, int stamina)
        {
            if (!_applyMatchFatigue) return 1000;

            int permille = _condition.MatchFatigueAt90Permille * minute / 90;
            if (minute > 45) permille -= _condition.HalfTimeRecoveryPermille;
            if (permille < 0) permille = 0;

            int neutral = _condition.StaminaNeutral;
            int scaled = neutral > 0 ? permille * (2 * neutral - stamina) / neutral : permille;
            if (scaled < 0) scaled = 0;
            if (scaled > 900) scaled = 900;
            return 1000 - scaled;
        }

        private static int[] PlayerIds(Lineup lineup)
        {
            var ids = new int[lineup.Slots.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = lineup.Slots[i].Player.Id;
            return ids;
        }

        // ------------------------------------------------------------------ acting

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
                _sheet.Record(tick, BallActionKind.Dribble, home, slot, -1);
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
                ty = _ctx.Inside(aimY);
                return;
            }

            tx = U.ClampX(_px[k] + (int)((long)dx * reachU / span));
            ty = _ctx.Inside(_py[k] + (int)((long)dy * reachU / span));
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
                _sheet.Record(tick, BallActionKind.Clearance, home, slot, -1);
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
            _sheet.Record(tick, BallActionKind.Clearance, home, slot, -1);
        }

        private void TakeRestart(int tick, int side, int slot)
        {
            BeginRestart(tick, side, slot);

            // A penalty is STRUCK, from the spot (Law 14).
            if (_ctx.DeadKind == BallActionKind.Penalty)
            {
                TakePenalty(tick, side, slot);
                return;
            }

            // A corner is swung into the box; everything else is played out normally.
            if (_ctx.DeadKind == BallActionKind.Corner)
            {
                bool home = side == 0;
                int goalX = U.Units(MovementGeometry.AttackedGoalX(home));
                int aimX = goalX - MovementGeometry.Direction(home) * U.Units(90);
                int aimY = U.CenterYU + (_rng.NextInt(-120, 121) * U.Scale);
                int distance = U.Distance(_ball.X, _ball.Y, aimX, U.ClampY(aimY));
                _ball.Kick(side, slot, aimX - _ball.X, U.ClampY(aimY) - _ball.Y,
                    _ball.ForceForTicks(distance, _cfg.TicksOfMs(1200), _maxPassForce));
                _sheet.Record(tick, BallActionKind.Cross, home, slot, -1);
            }
        }

        /// <summary>The ball is back in play, at the taker's feet.</summary>
        private void BeginRestart(int tick, int side, int slot)
        {
            _ball.Dead = false;
            Collect(side, slot);
            _hold[side * _n + slot] = 1;
            _ctx.RestartGrace = tick + _cfg.RestartGraceTicks;
            _ctx.RestartTaker = side * _n + slot;
        }

        /// <summary>
        /// He strikes it. Where he MEANS to put it is inside a post, the better the chance the
        /// tighter; where it ACTUALLY goes is that, plus an error his Shooting and Technique earn
        /// him and the difficulty of the chance takes away — the same two-uniform draw a misplaced
        /// pass gets, so most strikes are near their line and the wild one is rare.
        ///
        /// And then nothing else is decided here. The ball is in the air: a defender in front of
        /// it may block it, the keeper may reach it, and if it crosses the line between the posts
        /// it is a goal. That sentence is the whole of engine phase 6.
        /// </summary>
        private void TakeShot(int tick, int side, int slot)
        {
            bool home = side == 0;
            int goalX = U.Units(MovementGeometry.AttackedGoalX(home));
            int half = U.Units(MovementGeometry.GoalHalfWidthDm);

            _shotQuality = ShotQuality(side, slot);

            int reach = half - U.Units(6);
            int centre = reach * _cfg.ShotPlacementCentrePermille / 1000;
            int placed = centre + (reach - centre) * _shotQuality / 1000;
            int aimY = U.CenterYU + (_rng.NextInt(0, 2) == 0 ? -placed : placed);

            int spread = U.Units(_cfg.ShotSpreadDm) * (1000 - _shotQuality) / 1000;
            aimY = U.ClampY(aimY + BallSkill.Spread(_rng, spread));

            int offCentre = aimY > U.CenterYU ? aimY - U.CenterYU : U.CenterYU - aimY;
            _shotOnTarget = offCentre < half;

            // Struck as hard as a shot is struck: it gets there, and it gets there quickly.
            // It leaves from the BALL, so nothing jumps; the striker is on it by construction.
            _ball.Kick(side, slot, goalX - _ball.X, aimY - _ball.Y, _maxShootForce);

            _ctx.ShotLive = true;
            _ctx.ShotHome = home;
            _ctx.ShotSlot = slot;
            _lastTouch[side] = slot;
            _shotExpires = tick + _cfg.ShotResolveTicks;

            // Filed under the frame the strike was struck IN, not the next one written. Every
            // other action is rounded UP so it is never shown before it happened; a strike is the
            // one thing for which that is wrong, because in half a second the ball has travelled
            // sixteen metres and the frame would show it halfway to the goal. Anything that asks
            // "where was this hit from" — the replay dump, the harness's shot map, a future
            // player's shot chart — reads the ball at this frame.
            _sheet.RecordStruckAt(tick, home, slot);
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
            return held;
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
            _offside.ClearFlags();
            _offside.FlagOffside(side, slot);

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
            _sheet.Record(tick, kind, home, slot, choice.Slot);
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
            bool wide = IsWideChannel(_py[k]);
            return wide && U.Distance(_px[k], _py[k], goalX, U.CenterYU) < U.Units(Pitch.LengthDm / 3);
        }

        /// <summary>
        /// The outer quarter of the pitch on either side — the touchline channels. It is the same
        /// test <see cref="IsCross"/> makes, named once so that the Width instruction prices
        /// exactly the position the feed will later call a cross (engine phase 8).
        /// </summary>
        private static bool IsWideChannel(int y) => y < U.WidthU / 4 || y > U.WidthU * 3 / 4;

        // ------------------------------------------------------------------ movement

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
            for (int j = 0; j < _n && !_freeKickWall.IsInWall(k); j++)
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
            _lastTouch[side] = slot;
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
            _ball.X = MovementGeometry.Clamp(_px[k], _ctx.InsetU, U.LengthU - _ctx.InsetU);
            _ball.Y = _ctx.Inside(_py[k]);
        }

        private void ResolveControl(int tick)
        {

            // OUT IS OUT (engine phase 6). ResolveOutOfPlay runs after this method, so a ball
            // that left the field of play during this tick was still being offered to everyone
            // standing near its path — and a defender who "blocked" it there put a ball that had
            // already crossed the line back into a state where the referee then read a bogus
            // crossing point off his deflection. While the score belonged to the timeline that
            // could only produce a strange restart; with the causality inverted it produced a
            // GOAL, about one in every three matches. The laws are simpler than the code was: the
            // moment it is out, nobody may play it.
            if (_ball.Free && (_ball.X < 0 || _ball.X > U.LengthU || _ball.Y < 0 || _ball.Y > U.WidthU))
                return;
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
                        if (_referee.GiveFoulIfCommitted(tick, takerSide, taker, owner)) return;

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

                        _offside.ClearFlags();
                        _sheet.Record(tick, BallActionKind.Recovery, takerSide == 0, taker, -1);
                    }
                }

                return;
            }

            // What the ball was DOING before anybody got to it. Taking possession zeroes its
            // velocity, so a deflection or a block has to be priced off the incoming ball before
            // that happens (engine phase 5).
            int inVx = _ball.Vx, inVy = _ball.Vy;

            // A strike can be BLOCKED (engine phase 5). A quarter of the shots in a real match
            // hit a defender, and that is where a large share of football's corners come from.
            // Since phase 6 NOTHING is exempt: there is no timeline saying this one was always
            // going in, so the man who throws himself in front of it can stop any of them.
            if (_ctx.ShotLive && BlockStrike(tick, inVx, inVy)) return;

            // And past the bodies, a strike ON TARGET is the keeper's ball and nobody else's. An
            // outfielder does not "control" a shot travelling at thirty metres a second; he
            // blocks it, which is the branch above. One flying wide is nobody's at all: it runs
            // out, and the goal kick is the referee's business.
            if (_ctx.ShotLive && !_shotOnTarget) return;
            int keeperOnly = _ctx.ShotLive ? (_ctx.ShotHome ? 1 : 0) : -1;

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
                    int reach = _controlU;

                    // THE DIVE (engine phase 6). A strike at his goal is the one ball a keeper
                    // reaches further for than anybody reaches for anything, and it is the whole
                    // of the save: phase 5 had saves only because the timeline had already
                    // decided there would be one.
                    if (_ctx.ShotLive && _keeper[k] && side == keeperOnly)
                        reach += U.Units(BallSkill.KeeperDiveDm(_skGoalkeeping[k], _shotQuality, _cfg));

                    // The man it was played to reaches further for it than anyone else, because
                    // he is facing it and running onto it while the man behind him is turning
                    // (engine phase 4).
                    if (_receiver[side] == i) reach += U.Units(_cfg.ReceiveReachDm);

                    int distance = _ctx.ShotLive
                        ? SweptDistance(k, inVx, inVy)
                        : U.Distance(_px[k], _py[k], _ball.X, _ball.Y);
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

            bool wasShot = _ctx.ShotLive;
            bool interception = _ball.LastTouchSide >= 0 && _ball.LastTouchSide != bestSide;

            // The keeper's hands (engine phase 4). A save used to end the move by definition:
            // he got to it, therefore he had it. Goalkeeping now says how often he actually
            // HOLDS it, and a fierce strike is parried more often than a tame one — which puts
            // a live ball back in his six-yard box instead of ending the attack.
            int gkSlot = bestSide * _n + bestSlot;

            // BEATEN (engine phase 6). He got across to it; that is not the same as keeping it
            // out. If his Goalkeeping is not equal to the strike he never touches it, the ball
            // carries on, and — since it was on target to be his ball at all — it is a goal.
            if (wasShot && bestSide != (_ctx.ShotHome ? 0 : 1) && _keeper[gkSlot]
                && _rng.NextInt(0, 100) >= BallSkill.KeeperStopPercent(_skGoalkeeping[gkSlot], _shotQuality, _cfg))
                return;

            if (wasShot && bestSide != (_ctx.ShotHome ? 0 : 1) && _keeper[gkSlot]
                && _rng.NextInt(0, 100) >= BallSkill.KeeperHoldPercent(_skGoalkeeping[gkSlot], _shotQuality, _cfg))
            {
                _ctx.ShotLive = false;
                _receiver[0] = -1;
                _receiver[1] = -1;
                _tackleLock = tick + _cfg.TackleLockTicks;

                // Where he puts it. A keeper pushes a fierce one BEHIND as often as he pushes it
                // back into play, and that is a corner — one of the two big sources of football's
                // ten a match that this engine had no way of producing (engine phase 5).
                int outX, outY;
                if (_rng.NextInt(0, 100) < _cfg.KeeperParryBehindPercent)
                {
                    // BEHIND THE POST, not merely backwards (engine phase 6). This used to push
                    // the ball six metres back and six metres sideways, which from a central
                    // position is a point INSIDE his own goal — and while the score belonged to
                    // the timeline that could not become a goal, so nobody could see it. With the
                    // causality inverted it became a keeper punching one into his own net once a
                    // match. He puts it round the post: past the line, wide of the frame.
                    int ownGoalX = U.Units(MovementGeometry.OwnGoalX(bestSide == 0));
                    int postY = U.CenterYU + (_ball.Y < U.CenterYU ? -1 : 1)
                                * U.Units(MovementGeometry.GoalHalfWidthDm + _cfg.ParryRoundThePostDm);
                    // Aimed AT THE LINE and wide of the frame, not at a point behind it: a ball
                    // aimed four metres behind the goal from four and a half in front of it
                    // crosses the line half way through the turn, and half of "wide of the post"
                    // is inside it. The crossing point has to BE the target.
                    outX = ownGoalX - _ball.X;
                    outY = postY - _ball.Y;
                }
                else
                {
                    outX = MovementGeometry.Direction(bestSide == 0) * U.Units(80);
                    outY = _ball.Y < U.CenterYU ? -U.Units(90) : U.Units(90);
                }

                _ball.Kick(bestSide, bestSlot, outX, outY,
                    _ball.ForceForTicks(U.Units(_cfg.KeeperParryDm), _cfg.TicksOfMs(700), _maxPassForce));
                _lastTouch[bestSide] = bestSlot;
                _sheet.RecordSave(tick, bestSide, bestSlot, _ctx.ShotSlot);
                return;
            }

            Collect(bestSide, bestSlot);
            _lastTouch[bestSide] = bestSlot;
            _hold[bestSide * _n + bestSlot] = HoldTicks(bestSide);
            _receiver[0] = -1;
            _receiver[1] = -1;
            if (interception) _tackleLock = tick + _cfg.TackleLockTicks;

            // The flag, answered (Law 11). It only means anything now that somebody has played
            // the ball: the man it was raised against has taken it, and the whistle goes — or
            // anybody else has, and it comes down.
            if (_offside.AnyFlag)
            {
                if (!wasShot && _offside.IsFlagged(bestSide * _n + bestSlot))
                {
                    _referee.GiveOffside(tick, bestSide, bestSlot);
                    return;
                }

                _offside.ClearFlags();
            }

            if (wasShot && bestSide != (_ctx.ShotHome ? 0 : 1))
            {
                _ctx.ShotLive = false;
                _sheet.RecordSave(tick, bestSide, bestSlot, _ctx.ShotSlot);
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

            _sheet.Record(tick, BallActionKind.Interception, bestSide == 0, bestSlot, -1);
        }

        /// <summary>
        /// A body in the way. The strike is called as the thing the timeline made it — with the
        /// man who blocked it named, rather than his keeper — and the ball comes off him along the
        /// line it arrived on, which is what sends so many of them behind for a corner.
        /// </summary>
        /// <summary>
        /// How near a man came to the ball over the whole of this tick, not just at the end of it
        /// (engine phase 6). See <see cref="U.DistanceSqToSegment"/>: at strike speed the ball
        /// covers three and a quarter metres a tick, and sampling only the endpoint lets it
        /// tunnel through the man standing in front of it.
        /// </summary>
        private int SweptDistance(int k, int inVx, int inVy)
        {
            long sq = U.DistanceSqToSegment(
                _px[k], _py[k], _ball.X - inVx, _ball.Y - inVy, _ball.X, _ball.Y);
            return MovementGeometry.Sqrt(sq > int.MaxValue ? int.MaxValue : (int)sq);
        }

        private bool BlockStrike(int tick, int inVx, int inVy)
        {
            int attacking = _ctx.ShotHome ? 0 : 1;
            int defending = 1 - attacking;
            int reach = _interceptU + U.Units(_cfg.BlockReachDm);

            for (int j = 0; j < _n; j++)
            {
                int k = defending * _n + j;
                if (_keeper[k] || _sentOff[k]) continue;
                if (SweptDistance(k, inVx, inVy) > reach) continue;
                if (_rng.NextInt(0, 1000) >= _cfg.BlockPermillePerTick) continue;

                // A BLOCK is not a save and it is not a miss — football counts it as its own
                // thing, and so does the timeline: the strike is over, nobody kept it out, and
                // where the ball goes off him is anybody's, up to and including into the net.
                _ctx.ShotLive = false;
                _lastTouch[defending] = j;
                _sheet.Record(tick, BallActionKind.Block, defending == 0, j, -1);
                _sheet.AddEvent(tick, attacking, _ctx.ShotSlot, MatchEventType.ChanceMissed);

                Deflect(tick, defending, j, inVx, inVy, charged: true);
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
        private void Deflect(int tick, int side, int slot, int inVx, int inVy, bool charged = false)
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

            // A CHARGED-DOWN STRIKE is always that (engine phase 6): a man throwing himself in
            // front of a shot absorbs it, he does not give it pace toward his own goal. Anywhere
            // inside shooting range of the goal he is defending, a block goes away or behind —
            // which is what a block is. Left as an ordinary deflection it read as a defender
            // volleying charged-down shots into his own net once every three matches, and that
            // only became visible when the causality was inverted and such a ball could score.
            if (charged && depth <= U.Units(_cfg.MaxShootRangeDm)) deep = true;
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
                    // AWAY, and struck to ARRIVE where it is aimed (engine phase 6). Phase 5 made
                    // exactly this fix to `Clear` — "a hoof aimed forty metres upfield flew the
                    // length of the pitch and out" — and left the sibling here hitting a twenty
                    // metre target at a flat share of maximum force. Sixteen of the engine's
                    // twenty-eight goal kicks a match were that ball running over the far byline,
                    // and it took the causality being inverted for the total to break its band:
                    // the eleven off-target shots football actually has were the ones that
                    // exposed a number that had been too big since before this phase.
                    int upX = dir * U.Units(200);
                    int upY = _rng.NextInt(-U.Units(200), U.Units(200) + 1);
                    _ball.Kick(side, slot, upX, upY,
                        _ball.ForceToArrive(
                            U.Length(upX, upY),
                            _arrivalStepU * _cfg.ClearanceArrivalPercent / 100, firm, out int _));
                }

                _hold[side * _n + slot] = 0;
                _sheet.Record(tick, BallActionKind.Clearance, side == 0, slot, -1);
                return;
            }

            int alongPermille = _rng.NextInt(-_cfg.DeflectBackPermille, 1001);
            int acrossPermille = _rng.NextInt(-_cfg.DeflectSpreadPermille, _cfg.DeflectSpreadPermille + 1);
            int dx = (int)((long)inVx * alongPermille / 1000) - (int)((long)inVy * acrossPermille / 1000);
            int dy = (int)((long)inVy * alongPermille / 1000) + (int)((long)inVx * acrossPermille / 1000);
            if (dx == 0 && dy == 0) dx = dir;

            _ball.Kick(side, slot, dx, dy, force);
            _hold[side * _n + slot] = 0;
            _sheet.Record(tick, BallActionKind.Clearance, side == 0, slot, -1);
        }

        /// <summary>
        /// A strike that neither crossed a line nor was gathered — it hit somebody, or pulled up
        /// short. The flag has to come down, or the ball stays "live" and nobody is allowed to
        /// touch it for the rest of the match.
        /// </summary>
        private void ResolveStuckShot(int tick)
        {
            if (!_ctx.ShotLive || tick < _shotExpires) return;
            ForceResolveShot(tick);
        }

        /// <summary>
        /// The strike is over without anything having settled it: it pulled up, or the whistle
        /// went while it was travelling. Since engine phase 6 there is no outcome owed to a
        /// timeline, so this is bookkeeping and nothing more — the shot is recorded as the miss
        /// it turned out to be and the ball, wherever it is, is live again.
        /// </summary>
        private void ForceResolveShot(int tick)
        {
            if (!_ctx.ShotLive) return;

            _ctx.ShotLive = false;
            _sheet.RecordMiss(tick, _ctx.ShotHome ? 0 : 1, _ctx.ShotSlot);
        }

        /// <summary>
        /// A penalty (Law 14) — and since engine phase 6 it is simply a strike from twelve yards
        /// with nobody in the way. It goes in when it beats the keeper and it does not when it
        /// does not; the phase-5 version had to borrow an outcome from the timeline, because the
        /// score was not the pitch's to write. The odds it converts at are therefore not a knob
        /// any more: they are the taker's Shooting against the keeper's Goalkeeping, from six
        /// metres out and straight in front, which is what makes a penalty a penalty.
        /// </summary>
        private void TakePenalty(int tick, int side, int slot)
        {
            if (_ctx.ShotLive) ForceResolveShot(tick);
            TakeShot(tick, side, slot);
        }

        // ------------------------------------------------------------------ lookups

        private int NearestToBall(int side, bool includeKeeper) =>
            _ctx.NearestTo(side, _ball.X, _ball.Y, includeKeeper);

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
