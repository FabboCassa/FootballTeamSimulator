using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Match
{
    /// <summary>
    /// Generates the replayable top-down position stream (task 1.5) as a
    /// post-process of the event timeline produced by the result model (1.4).
    ///
    /// Model:
    ///   - the ball follows keyframes: kickoff at centre, the shot spot at every
    ///     event tick, centre again right after each goal, plus random "wander"
    ///     waypoints in between; positions interpolate linearly with noise that
    ///     fades to zero at keyframes (so keyframes are hit exactly);
    ///   - players steer toward formation anchors shifted toward the ball, with
    ///     a capped step per tick; the shooter sprints to the shot spot in the
    ///     ticks before an event and coincides with the ball at the event tick.
    ///
    /// Deterministic: integer math only, fixed RNG draw order (same draw count
    /// per tick regardless of branches taken).
    /// </summary>
    public sealed class PositionStreamGenerator
    {
        private readonly MatchBalance _cfg;

        // Wander band for intermediate ball waypoints (presentation-only geometry).
        private const int WanderMarginX = Pitch.LengthDm / 5;
        private const int WanderMarginY = Pitch.WidthDm / 6;

        public PositionStreamGenerator(MatchBalance cfg)
        {
            _cfg = cfg;
        }

        public PositionStream Generate(Lineup home, Lineup away, MatchReport report, IRandomSource rng)
        {
            int lastTick = 90 * _cfg.TicksPerMinute;

            List<(int Tick, PitchPoint Point)> keyframes = BuildKeyframes(
                home, away, report, rng, lastTick,
                out List<(int Tick, int SlotIndex, bool HomeShoots, PitchPoint Spot)> shots);

            PitchPoint[] ball = InterpolateBall(keyframes, rng, lastTick);

            PitchPoint[] homeAnchors = BuildAnchors(home, mirror: false);
            PitchPoint[] awayAnchors = BuildAnchors(away, mirror: true);
            var homePos = (PitchPoint[])homeAnchors.Clone();
            var awayPos = (PitchPoint[])awayAnchors.Clone();

            var stream = new PositionStream { TicksPerMinute = _cfg.TicksPerMinute };
            stream.Frames.Add(MakeFrame(0, ball[0], homePos, awayPos));

            int shotIndex = 0;
            for (int t = 1; t <= lastTick; t++)
            {
                while (shotIndex < shots.Count && shots[shotIndex].Tick < t) shotIndex++;

                bool approaching = shotIndex < shots.Count
                    && shots[shotIndex].Tick - t < _cfg.ShooterApproachTicks;
                var shot = approaching ? shots[shotIndex] : default;

                MoveTeam(home, homePos, homeAnchors, ball[t], rng,
                    approaching && shot.HomeShoots ? shot.SlotIndex : -1, shot.Spot);
                MoveTeam(away, awayPos, awayAnchors, ball[t], rng,
                    approaching && !shot.HomeShoots ? shot.SlotIndex : -1, shot.Spot);

                // At the event tick the shooter coincides with the ball exactly.
                if (approaching && shot.Tick == t)
                {
                    if (shot.HomeShoots) homePos[shot.SlotIndex] = shot.Spot;
                    else awayPos[shot.SlotIndex] = shot.Spot;
                }

                stream.Frames.Add(MakeFrame(t, ball[t], homePos, awayPos));
            }

            return stream;
        }

        // ------------------------------------------------------------ keyframes

        private List<(int, PitchPoint)> BuildKeyframes(
            Lineup home, Lineup away, MatchReport report, IRandomSource rng, int lastTick,
            out List<(int Tick, int SlotIndex, bool HomeShoots, PitchPoint Spot)> shots)
        {
            var center = new PitchPoint(Pitch.CenterX, Pitch.CenterY);
            var keyframes = new List<(int Tick, PitchPoint Point)> { (0, center) };
            shots = new List<(int, int, bool, PitchPoint)>();

            foreach (MatchEvent e in report.Events)
            {
                int te = e.Minute * _cfg.TicksPerMinute;
                bool homeShoots = e.ClubId == report.HomeClubId;

                int x = homeShoots
                    ? Pitch.LengthDm - _cfg.ShotSpotGoalDistanceDm
                    : _cfg.ShotSpotGoalDistanceDm;
                int y = Pitch.ClampY(Pitch.CenterY
                    + rng.NextInt(-_cfg.ShotSpotHalfWidthDm, _cfg.ShotSpotHalfWidthDm + 1));
                var spot = new PitchPoint(x, y);

                // The shot spot wins over any earlier keyframe at/after its tick
                // (e.g. a kickoff keyframe when events land in adjacent minutes).
                while (keyframes.Count > 0 && keyframes[keyframes.Count - 1].Tick >= te)
                    keyframes.RemoveAt(keyframes.Count - 1);

                keyframes.Add((te, spot));
                shots.Add((te, SlotIndexOf(homeShoots ? home : away, e.PlayerId), homeShoots, spot));

                if (e.Type == MatchEventType.Goal && te + 2 <= lastTick)
                    keyframes.Add((te + 2, center)); // kickoff restart
            }

            if (keyframes[keyframes.Count - 1].Tick < lastTick)
                keyframes.Add((lastTick, RandomWanderPoint(rng)));

            // Insert wander waypoints so the ball doesn't crawl in straight lines.
            var full = new List<(int, PitchPoint)>();
            for (int i = 0; i < keyframes.Count - 1; i++)
            {
                full.Add(keyframes[i]);
                int t0 = keyframes[i].Tick, t1 = keyframes[i + 1].Tick;
                int interval = _cfg.BallWaypointIntervalTicks;
                if (interval > 0 && t1 - t0 > interval * 2)
                    for (int t = t0 + interval; t <= t1 - interval; t += interval)
                        full.Add((t, RandomWanderPoint(rng)));
            }
            full.Add(keyframes[keyframes.Count - 1]);
            return full;
        }

        private PitchPoint RandomWanderPoint(IRandomSource rng) => new PitchPoint(
            rng.NextInt(WanderMarginX, Pitch.LengthDm - WanderMarginX + 1),
            rng.NextInt(WanderMarginY, Pitch.WidthDm - WanderMarginY + 1));

        // ------------------------------------------------------------ ball path

        private PitchPoint[] InterpolateBall(
            List<(int Tick, PitchPoint Point)> waypoints, IRandomSource rng, int lastTick)
        {
            var ball = new PitchPoint[lastTick + 1];
            ball[0] = waypoints[0].Point;

            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                (int t0, PitchPoint p0) = waypoints[i];
                (int t1, PitchPoint p1) = waypoints[i + 1];
                int span = t1 - t0;
                int half = span / 2 < 1 ? 1 : span / 2;

                for (int t = t0 + 1; t <= t1; t++)
                {
                    int d = t - t0;
                    int bx = p0.X + (p1.X - p0.X) * d / span;
                    int by = p0.Y + (p1.Y - p0.Y) * d / span;

                    int w = d < span - d ? d : span - d; // fades to 0 at both keyframes
                    int nx = rng.NextInt(-_cfg.BallNoiseDm, _cfg.BallNoiseDm + 1) * w / half;
                    int ny = rng.NextInt(-_cfg.BallNoiseDm, _cfg.BallNoiseDm + 1) * w / half;

                    ball[t] = new PitchPoint(Pitch.ClampX(bx + nx), Pitch.ClampY(by + ny));
                }
            }

            return ball;
        }

        // ------------------------------------------------------------ players

        private PitchPoint[] BuildAnchors(Lineup lineup, bool mirror)
        {
            var anchors = new PitchPoint[lineup.Slots.Count];

            for (int i = 0; i < lineup.Slots.Count; i++)
            {
                PositionRole role = lineup.Slots[i].Role;

                int ax = Pitch.LengthDm * AnchorPermille(role) / 1000;
                if (mirror) ax = Pitch.LengthDm - ax;

                bool wide = role == PositionRole.FullBack || role == PositionRole.Winger;
                int margin = wide ? _cfg.WideRoleYMarginDm : _cfg.CentralRoleYMarginDm;
                int lo = margin, hi = Pitch.WidthDm - margin;

                CountRolePeers(lineup, role, i, out int groupSize, out int indexInGroup);
                int ay = role == PositionRole.Goalkeeper || groupSize == 1
                    ? Pitch.CenterY
                    : lo + (hi - lo) * indexInGroup / (groupSize - 1);

                anchors[i] = new PitchPoint(Pitch.ClampX(ax), Pitch.ClampY(ay));
            }

            return anchors;
        }

        private int AnchorPermille(PositionRole role)
        {
            int[] table = _cfg.FormationAnchorXPermilleByRole;
            int index = (int)role;
            return index >= 0 && index < table.Length ? table[index] : 500;
        }

        private static void CountRolePeers(
            Lineup lineup, PositionRole role, int slotIndex, out int groupSize, out int indexInGroup)
        {
            groupSize = 0;
            indexInGroup = 0;
            for (int i = 0; i < lineup.Slots.Count; i++)
            {
                if (lineup.Slots[i].Role != role) continue;
                if (i < slotIndex) indexInGroup++;
                groupSize++;
            }
        }

        private void MoveTeam(
            Lineup lineup, PitchPoint[] positions, PitchPoint[] anchors,
            PitchPoint ball, IRandomSource rng, int shooterSlot, PitchPoint shooterTarget)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                bool keeper = lineup.Slots[i].Role == PositionRole.Goalkeeper;
                int pullX = keeper ? _cfg.GoalkeeperBallPullPercent : _cfg.PlayerBallPullXPercent;
                int pullY = keeper ? _cfg.GoalkeeperBallPullPercent : _cfg.PlayerBallPullYPercent;

                int tx = anchors[i].X + (ball.X - Pitch.CenterX) * pullX / 100;
                int ty = anchors[i].Y + (ball.Y - anchors[i].Y) * pullY / 100;
                int maxStep = _cfg.PlayerMaxStepDmPerTick;

                if (i == shooterSlot)
                {
                    tx = shooterTarget.X;
                    ty = shooterTarget.Y;
                    maxStep *= 2; // sprint to arrive on the ball
                }

                int dx = ClampStep(tx - positions[i].X, maxStep);
                int dy = ClampStep(ty - positions[i].Y, maxStep);
                int nx = rng.NextInt(-_cfg.PlayerNoiseDm, _cfg.PlayerNoiseDm + 1);
                int ny = rng.NextInt(-_cfg.PlayerNoiseDm, _cfg.PlayerNoiseDm + 1);

                positions[i] = new PitchPoint(
                    Pitch.ClampX(positions[i].X + dx + nx),
                    Pitch.ClampY(positions[i].Y + dy + ny));
            }
        }

        private static int ClampStep(int delta, int maxStep) =>
            delta > maxStep ? maxStep : (delta < -maxStep ? -maxStep : delta);

        private static int SlotIndexOf(Lineup lineup, int playerId)
        {
            for (int i = 0; i < lineup.Slots.Count; i++)
                if (lineup.Slots[i].Player.Id == playerId) return i;
            return 0;
        }

        private static PositionFrame MakeFrame(
            int tick, PitchPoint ball, PitchPoint[] home, PitchPoint[] away) => new PositionFrame
        {
            Tick = tick,
            Ball = ball,
            Home = (PitchPoint[])home.Clone(),
            Away = (PitchPoint[])away.Clone()
        };
    }
}
