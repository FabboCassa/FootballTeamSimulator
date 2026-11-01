using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Aerial duels and second balls (free-ball spec R4, issue #181). Where a high ball drops to a
    /// header's height with a man of each side under it, they go up for it together; the better in
    /// the air wins more often, and the ball comes off the winner's head toward the goal he attacks —
    /// or, the winner out of reach of it, off the other man's head anywhere. Nobody controls it: it is
    /// the second ball, and the two who went up sit it out for the release lock while the others
    /// contest it. A header is nodded down: it lands within AerialHeaderLandDm and runs on along the
    /// grass, so the second ball is played at feet. Inside either penalty area a high ball is still
    /// taken as before: crosses, box arrival and headed attempts are R5's.
    /// </summary>
    public sealed partial class MatchSimulator
    {
        private int _aerialContestU, _aerialHeaderU, _aerialSpoiledU, _aerialHeaderLandU;

        /// <summary>The duel's loser, landing: he may not touch the second ball until <see cref="_groundedUntil"/>.</summary>
        private int _grounded = -1, _groundedUntil = -1;

        /// <summary>The ball's touch count after the last duel's header, and the side that had played the ball into it.</summary>
        private int _duelTouch = -1, _duelFrom = -1;

        private void InitAerial()
        {
            _aerialContestU = U.Units(_cfg.AerialContestDm);
            _aerialHeaderU = U.Units(_cfg.AerialHeaderDm);
            _aerialSpoiledU = U.Units(_cfg.AerialSpoiledHeaderDm);
            _aerialHeaderLandU = U.Units(_cfg.AerialHeaderLandDm);
            _grounded = -1;
            _groundedUntil = -1;
            _duelTouch = -1;
            _duelFrom = -1;
        }

        /// <summary>
        /// The second ball, gathered off the duel's header by the side that did not play the ball into
        /// it, nobody having touched it since: the ball won back. Read before the gather.
        /// </summary>
        private bool SecondBallWon(int side) => _duelFrom >= 0 && _duelFrom != side && _ball.Touches == _duelTouch;

        /// <summary>The odds, in permille, that a man rated <paramref name="rating"/> in the air beats one rated <paramref name="other"/>.</summary>
        private static int AerialWinPermille(int rating, int other, MatchBalance cfg) =>
            BallSkill.Clamp(500 + (rating - other) * cfg.AerialDuelPermillePerPoint,
                cfg.AerialDuelMinPermille, 1000 - cfg.AerialDuelMinPermille);

        private int AerialRating(int k) => BallSkill.Mix(
            _skStrength[k], _cfg.AerialStrengthWeight,
            _skPositioning[k], _cfg.AerialPositioningWeight,
            _skTechnique[k], _cfg.AerialTechniqueWeight);

        /// <summary>A man still coming down from the duel he lost.</summary>
        private bool Grounded(int tick, int k) => k == _grounded && tick < _groundedUntil;

        /// <summary>True when an aerial duel settled the free ball this tick.</summary>
        private bool AerialDuel(int tick, bool justStruck)
        {
            if (_ctx.ShotLive || justStruck || !_ball.Dropping) return false;
            int z = _ball.Z;
            if (z < _cfg.AerialDuelFromDm || z > _cfg.TouchHeightDm) return false;
            int xDm = U.Dm(_ball.X), yDm = U.Dm(_ball.Y);
            if (MovementGeometry.InOwnBox(true, xDm, yDm) || MovementGeometry.InOwnBox(false, xDm, yDm)) return false;

            int home = AerialContender(tick, 0, out int homeU), away = AerialContender(tick, 1, out int awayU);
            if (home < 0 || away < 0 || (homeU > _controlU && awayU > _controlU)) return false;

            // An attacker in an offside position who goes up for it is involved in play (Law 11).
            if (_offside.AnyFlag)
            {
                if (_offside.IsFlagged(home) || _offside.IsFlagged(away))
                {
                    int flagged = _offside.IsFlagged(home) ? home : away;
                    _referee.GiveOffside(tick, flagged / _n, flagged % _n);
                    return true;
                }

                _offside.ClearFlags();
            }

            bool homeWins = _rng.NextInt(0, 1000) < AerialWinPermille(AerialRating(home), AerialRating(away), _cfg);
            int winner = homeWins ? home : away, loser = homeWins ? away : home;
            bool clean = (homeWins ? homeU : awayU) <= _controlU;
            int toucher = clean ? winner : loser;
            TouchPoint(toucher, false, out int metX, out int metY);
            _ball.X = metX;
            _ball.Y = metY;

            int dx, dy, distance;
            if (clean) HeaderAim(toucher, out dx, out dy, out distance);
            else
            {
                dx = _rng.NextInt(-1000, 1001);
                dy = _rng.NextInt(-1000, 1001);
                if (dx == 0 && dy == 0) dx = 1;
                distance = _aerialSpoiledU;
            }

            int side = toucher / _n, slot = toucher % _n;
            _duelFrom = _ball.LastTouchSide;
            _ball.AerialDuel = true;
            _ball.Kick(side, slot, dx, dy, _ball.ForceToArrive(distance, _arrivalStepU, _maxPassForce, out int _));
            _ball.AerialDuel = false;
            _duelTouch = _ball.Touches;
            _ball.Loft(distance < _aerialHeaderLandU ? distance : _aerialHeaderLandU, 0);

            Release(tick, side, slot);
            int other = toucher == winner ? loser : winner;
            _grounded = other;
            _groundedUntil = tick + _cfg.ReleaseLockTicks;
            _lastTouch[side] = slot;
            _receiver[0] = -1;
            _receiver[1] = -1;

            // Away off a head, aimed at nothing but space: on the sheet, whose touch it was (the
            // restart of a ball headed out goes against his side).
            _sheet.Record(tick, BallActionKind.Clearance, side == 0, slot, -1);
            return true;
        }

        /// <summary>
        /// The man of <paramref name="side"/> nearest this tick's flight within AerialContestDm of it,
        /// -1 if none; keepers, the sent off and a man locked off the ball do not go up.
        /// </summary>
        private int AerialContender(int tick, int side, out int distanceU)
        {
            int best = -1;
            distanceU = int.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                int k = side * _n + i;
                if (_keeper[k] || _sentOff[k] || Grounded(tick, k)) continue;
                if (k == _releasedBy && tick < _releasedUntil) continue;
                int d = TouchPoint(k, false, out int _, out int _);
                if (d > _aerialContestU || d >= distanceU) continue;
                best = k;
                distanceU = d;
            }

            return best;
        }

        /// <summary>
        /// The clean header: AerialHeaderDm toward the goal he attacks, scattered sideways by his
        /// Technique, and short of the box he attacks — a header at goal is R5's, from a cross.
        /// </summary>
        private void HeaderAim(int k, out int dx, out int dy, out int distance)
        {
            bool home = k < _n;
            int dir = MovementGeometry.Direction(home);
            int spread = BallSkill.Lerp(_skTechnique[k], 100, _cfg.AerialHeaderSpreadPermille, 0);
            int across = _rng.NextInt(-spread, spread + 1);

            int boxLine = U.Units(MovementGeometry.AttackedGoalX(home)) - dir * U.Units(MovementGeometry.BoxDepthDm);
            int tx = _ball.X + dir * _aerialHeaderU;
            if (dir * (tx - boxLine) > 0) tx = boxLine;
            dx = tx - _ball.X;
            dy = (int)((long)_aerialHeaderU * across / 1000);
            if (dx == 0 && dy == 0) dy = 1;
            distance = U.Length(dx, dy);
        }
    }
}
