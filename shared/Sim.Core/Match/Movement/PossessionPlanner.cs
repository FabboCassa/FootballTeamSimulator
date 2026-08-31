using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Random;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Turns the result model's event timeline (task 1.4) into a choreography of
    /// possessions (task 13.1): who has the ball, for how long, how it moves, how each
    /// possession breaks down — and, around every scripted event, a build-up that
    /// delivers the ball to exactly the right player at exactly the right tick.
    ///
    /// EVERY random draw of the movement layer happens here, before a single position is
    /// integrated. That is the determinism rule: the planner is the only consumer of the
    /// RNG, the integrator is a pure function of the plan plus the positions it has
    /// already produced. It replaces 1.5's fragile "same number of draws per tick
    /// whatever branch is taken" with something that cannot be got wrong by adding a
    /// branch. The planner also never touches the score: it is handed the events and
    /// works around them.
    /// </summary>
    internal sealed class PossessionPlanner
    {
        /// <summary>Shortest build-up the planner will squeeze a scripted chance into.</summary>
        private const int MinChainTicks = 5;

        /// <summary>Shortest free possession worth emitting.</summary>
        private const int MinPossessionTicks = 6;

        private readonly MatchBalance _cfg;
        private readonly IRandomSource _rng;

        public PossessionPlanner(MatchBalance cfg, IRandomSource rng)
        {
            _cfg = cfg;
            _rng = rng;
        }

        /// <summary>
        /// Builds the script. <paramref name="homePossessionPermille"/> is the same
        /// possession share the engine derives from the midfield ratings, so the side
        /// that dominates the result model visibly keeps the ball.
        /// </summary>
        public MatchScript Plan(
            Lineup home, Lineup away, MatchReport report, int homePossessionPermille, int lastTick)
        {
            var script = new MatchScript();
            int tpm = _cfg.TicksPerMinute;

            script.Add(0, ScriptOp.Kickoff, BallActionKind.Kickoff, home: true);
            bool homeHasBall = true;
            int t = 1;

            List<MatchEvent> events = report.Events;
            for (int e = 0; e < events.Count; e++)
            {
                MatchEvent ev = events[e];
                int shotTick = ev.Minute * tpm;
                if (shotTick > lastTick) shotTick = lastTick;

                bool attackHome = ev.ClubId == report.HomeClubId;
                int shooterSlot = SlotIndexOf(attackHome ? home : away, ev.PlayerId);

                // How far this chance's aftermath may run before the next one needs the ball.
                int nextLimit = e + 1 < events.Count
                    ? events[e + 1].Minute * tpm - MinChainTicks
                    : lastTick;

                int chainTicks = ChainTicks();
                int chainStart = shotTick - chainTicks;
                if (chainStart < t) chainStart = t;

                FillFreePlay(script, ref t, ref homeHasBall, chainStart, homePossessionPermille);

                if (homeHasBall != attackHome)
                {
                    // The side about to score has to WIN the ball first — never inherit it
                    // silently, or the integrator would have to teleport it to them.
                    int at = t < shotTick ? t : shotTick - 1;
                    if (at < 0) at = 0;
                    script.Add(at, ScriptOp.Turnover, TurnoverKind(), attackHome);
                    homeHasBall = attackHome;
                    if (t <= at) t = at + 1;
                }

                EmitChain(script, ref t, attackHome, shooterSlot, shotTick);
                EmitShot(script, ref t, ref homeHasBall, attackHome, shooterSlot, ev.Type, shotTick, lastTick, nextLimit);
            }

            FillFreePlay(script, ref t, ref homeHasBall, lastTick, homePossessionPermille);

            script.Sort();
            return script;
        }

        // ------------------------------------------------------------------ free play

        /// <summary>Ordinary football between the scripted chances, up to (not past) <paramref name="limit"/>.</summary>
        private void FillFreePlay(
            MatchScript script, ref int t, ref bool homeHasBall, int limit, int homePossessionPermille)
        {
            while (t + MinPossessionTicks <= limit)
            {
                int len = _rng.NextInt(_cfg.FreePossessionTicksMin, _cfg.FreePossessionTicksMax + 1);
                if (t + len > limit) len = limit - t;
                if (len < MinPossessionTicks) return;

                int end = t + len;
                EmitTouches(script, ref t, homeHasBall, end);
                t = end;
                if (t >= limit) return;

                if (_rng.NextInt(0, 100) < _cfg.TurnoverSharePercent)
                {
                    // Won back on the pitch: the ball simply changes hands.
                    bool winner = !homeHasBall;
                    script.Add(t, ScriptOp.Turnover, TurnoverKind(), winner);
                    homeHasBall = winner;
                    t++;
                }
                else
                {
                    // Out of play. A corner stays with the attacking side; the rest turn over.
                    int roll = _rng.NextInt(0, 100);
                    BallActionKind kind = roll < 45 ? BallActionKind.ThrowIn
                        : roll < 70 ? BallActionKind.GoalKick
                        : roll < 88 ? BallActionKind.Corner
                        : BallActionKind.FreeKick;

                    bool taker = kind == BallActionKind.Corner || kind == BallActionKind.FreeKick
                        ? (kind == BallActionKind.Corner ? homeHasBall : !homeHasBall)
                        : !homeHasBall;

                    script.Add(t, ScriptOp.DeadBall, kind, taker);
                    homeHasBall = taker;
                    t += _cfg.DeadBallTicks;
                }

                // Keep the match's possession share honest over the 90 minutes: now and then,
                // pull the ball back toward the side the ratings say should have it. Always as
                // a CHALLENGE, never a silent hand-over — the ball is a physical object here
                // and it may only change owner through something the viewer can see.
                bool wanted = _rng.NextInt(0, 1000) < homePossessionPermille;
                bool reshuffle = _rng.NextInt(0, 1000) >= 500;
                if (reshuffle && wanted != homeHasBall && t + 1 < limit)
                {
                    script.Add(t, ScriptOp.Turnover, TurnoverKind(), wanted);
                    homeHasBall = wanted;
                    t++;
                }
            }
        }

        /// <summary>Passes, long balls, crosses and dribbles filling a possession up to <paramref name="end"/>.</summary>
        private void EmitTouches(MatchScript script, ref int t, bool home, int end)
        {
            while (true)
            {
                int hold = _rng.NextInt(_cfg.CarryTicksMin, _cfg.CarryTicksMax + 1);
                int roll = _rng.NextInt(0, 100);
                bool longBall = roll < _cfg.LongBallSharePercent;
                int flight = longBall
                    ? _cfg.LongBallFlightTicks
                    : _rng.NextInt(_cfg.PassFlightTicksMin, _cfg.PassFlightTicksMax + 1);

                if (t + hold + flight > end) return;

                t += hold;
                BallActionKind kind = longBall ? BallActionKind.LongBall
                    : roll < _cfg.LongBallSharePercent + _cfg.CrossSharePercent ? BallActionKind.Cross
                    : BallActionKind.Pass;
                int bias = longBall ? MatchScript.BiasLong : _rng.NextInt(MatchScript.BiasBack, MatchScript.BiasForward + 1);

                script.Add(t, ScriptOp.Pass, kind, home, bias: bias, flight: flight);
                t += flight;

                if (_rng.NextInt(0, 100) < _cfg.DribbleSharePercent && t + 1 <= end)
                {
                    script.Add(t, ScriptOp.Dribble, BallActionKind.Dribble, home);
                    t++;
                }
            }
        }

        // ------------------------------------------------------------------ scripted chance

        /// <summary>Build-up that puts the ball on the scripted shooter's foot at <paramref name="shotTick"/>.</summary>
        private void EmitChain(MatchScript script, ref int t, bool attackHome, int shooterSlot, int shotTick)
        {
            int spotX, spotY;
            ShotSpot(attackHome, out spotX, out spotY);

            int aim = shotTick - _cfg.ShooterApproachTicks;
            if (aim < 0) aim = 0;
            // Flight carries the tick the shot resolves at: approach windows overlap (chances
            // can be a minute apart while the run-up is two and a half), so the integrator
            // holds a QUEUE of armed runs and always steers by the one that fires first.
            script.Add(aim, ScriptOp.AimShot, BallActionKind.Shot, attackHome,
                slot: shooterSlot, flight: shotTick, x: spotX, y: spotY);

            int gap = shotTick - t;
            if (gap < MinChainTicks)
            {
                int tick = shotTick - 2;
                if (tick < t) tick = t;
                if (tick < shotTick)
                    script.Add(tick, ScriptOp.Pass, BallActionKind.Pass, attackHome,
                        target: shooterSlot, flight: shotTick - tick - 1 > 0 ? shotTick - tick - 1 : 1);
                t = shotTick;
                return;
            }

            int hold = MovementGeometry.Clamp(
                _rng.NextInt(_cfg.CarryTicksMin, _cfg.CarryTicksMax + 1), 1, gap / 3 > 0 ? gap / 3 : 1);
            int maxFlight = gap - hold - 1;
            if (maxFlight < 1) maxFlight = 1;
            int flight = MovementGeometry.Clamp(
                _rng.NextInt(_cfg.PassFlightTicksMin, _cfg.PassFlightTicksMax + 1), 1, maxFlight);

            // The assist must LAND before the shot, never on it or after it: the whole
            // contract is that the shooter is holding the ball at shotTick.
            int passTick = shotTick - hold - flight;
            if (passTick < t) passTick = t;
            if (passTick >= shotTick) passTick = shotTick - 1;
            if (passTick + flight >= shotTick) flight = shotTick - passTick - 1;
            if (flight < 1) flight = 1;

            if (passTick > t) EmitTouches(script, ref t, attackHome, passTick);

            BallActionKind assist = _rng.NextInt(0, 100) < _cfg.CrossSharePercent
                ? BallActionKind.Cross
                : BallActionKind.Pass;
            script.Add(passTick, ScriptOp.Pass, assist, attackHome, target: shooterSlot, flight: flight);
            t = shotTick;
        }

        /// <summary>The strike, its outcome and the restart it produces.</summary>
        private void EmitShot(
            MatchScript script, ref int t, ref bool homeHasBall, bool attackHome, int shooterSlot,
            MatchEventType type, int shotTick, int lastTick, int nextLimit)
        {
            int goalX = MovementGeometry.AttackedGoalX(attackHome);
            int targetY;
            switch (type)
            {
                case MatchEventType.Goal:
                    targetY = Pitch.CenterY + _rng.NextInt(-MovementGeometry.GoalHalfWidthDm + 8, MovementGeometry.GoalHalfWidthDm - 7);
                    break;
                case MatchEventType.ChanceSaved:
                    targetY = Pitch.CenterY + _rng.NextInt(-MovementGeometry.GoalHalfWidthDm, MovementGeometry.GoalHalfWidthDm + 1);
                    break;
                default:
                    int side = _rng.NextInt(0, 2) == 0 ? -1 : 1;
                    targetY = Pitch.CenterY + side * (MovementGeometry.GoalHalfWidthDm + _rng.NextInt(15, 140));
                    break;
            }

            targetY = Pitch.ClampY(targetY);
            script.Add(shotTick, ScriptOp.Shot, BallActionKind.Shot, attackHome,
                slot: shooterSlot, flight: _cfg.ShotFlightTicks, x: goalX, y: targetY);

            int arrive = shotTick + _cfg.ShotFlightTicks;
            if (arrive > lastTick) arrive = lastTick;
            if (arrive <= shotTick)
            {
                // The whistle goes with the ball still travelling: no outcome, no restart.
                // Emitting one at the shot's own tick would move the ball off the shooter,
                // and "ball on the shooter at M * TicksPerMinute" is the contract the whole
                // event timeline is rendered against.
                homeHasBall = attackHome;
                t = lastTick;
                return;
            }

            BallActionKind outcome = type == MatchEventType.Goal ? BallActionKind.Goal
                : type == MatchEventType.ChanceSaved ? BallActionKind.Save
                : BallActionKind.Miss;
            script.Add(arrive, ScriptOp.Outcome, outcome, attackHome, slot: shooterSlot, x: goalX, y: targetY);

            if (type == MatchEventType.Goal)
            {
                int restart = Restart(arrive + _cfg.GoalCelebrationTicks, arrive, lastTick, nextLimit);
                script.Add(restart, ScriptOp.Kickoff, BallActionKind.Kickoff, !attackHome);
                homeHasBall = !attackHome;
                t = restart + 1;
            }
            else if (type == MatchEventType.ChanceSaved)
            {
                // The keeper has it: no dead ball, he simply plays it away.
                homeHasBall = !attackHome;
                t = Restart(arrive + _cfg.DeadBallTicks, arrive, lastTick, nextLimit);
            }
            else
            {
                int restart = Restart(arrive + 1, arrive, lastTick, nextLimit);
                script.Add(restart, ScriptOp.DeadBall, BallActionKind.GoalKick, !attackHome);
                homeHasBall = !attackHome;
                t = Restart(restart + _cfg.DeadBallTicks, restart, lastTick, nextLimit);
            }

            if (t > lastTick) t = lastTick;
        }

        /// <summary>A restart tick that respects both full time and the next scripted chance.</summary>
        private static int Restart(int wanted, int earliest, int lastTick, int nextLimit)
        {
            int r = wanted;
            if (r > nextLimit) r = nextLimit;
            if (r > lastTick) r = lastTick;
            if (r <= earliest) r = earliest + 1;
            if (r > lastTick) r = lastTick;
            return r;
        }

        // ------------------------------------------------------------------ draws

        private int ChainTicks()
        {
            int touches = _rng.NextInt(_cfg.PossessionTouchesMin, _cfg.PossessionTouchesMax + 1);
            int perTouch = (_cfg.CarryTicksMin + _cfg.CarryTicksMax + _cfg.PassFlightTicksMin + _cfg.PassFlightTicksMax) / 2;
            int ticks = touches * perTouch;
            return ticks < MinChainTicks ? MinChainTicks : ticks;
        }

        private BallActionKind TurnoverKind() =>
            _rng.NextInt(0, 100) < 55 ? BallActionKind.Tackle : BallActionKind.Interception;

        private void ShotSpot(bool attackHome, out int x, out int y)
        {
            int dist = _rng.NextInt(_cfg.ShotSpotGoalDistanceMinDm, _cfg.ShotSpotGoalDistanceMaxDm + 1);
            x = attackHome ? Pitch.LengthDm - dist : dist;
            y = Pitch.ClampY(Pitch.CenterY + _rng.NextInt(-_cfg.ShotSpotHalfWidthDm, _cfg.ShotSpotHalfWidthDm + 1));
        }

        private static int SlotIndexOf(Lineup lineup, int playerId)
        {
            for (int i = 0; i < lineup.Slots.Count; i++)
                if (lineup.Slots[i].Player.Id == playerId) return i;
            return 0;
        }
    }
}
