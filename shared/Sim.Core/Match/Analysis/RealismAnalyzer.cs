using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The realism readings of the watchable-match spec, counted off a finished match's position
    /// stream. Like <see cref="MatchAnalyzer"/> it is a measuring instrument: it reads the report,
    /// draws nothing from any RNG and touches nothing, so it can never move a result.
    ///
    /// Definitions (docs/specs/watchable-match-engine.md):
    ///   * R4 open goal — a carrier within 20 m of the centre of the goal he attacks, with no
    ///     outfield defender inside the triangle ball-to-posts. One chance per unbroken spell of
    ///     the same carrier in that state (a loose frame while he runs with the ball does not break
    ///     it); it is taken when he shoots within 1.5 s of it opening.
    ///   * R5 possession — an unbroken spell of one side holding the ball; loose frames between
    ///     two of its touches do not break it, the other side touching it or any restart does.
    ///     Sterile: 20 s or longer, net progress toward goal under 10 m, and no pass received in
    ///     the final third.
    ///   * R7 box entries — open-play possessions (not started by a corner, free kick or penalty)
    ///     that take the ball from outside the opponent's penalty area to a man holding it inside,
    ///     once per possession. A ball won back inside the box was never brought into it.
    ///   * R2 off-target seconds — see <see cref="OffTargetMeter"/>.
    ///
    /// Reusable: keep one analyzer and call <see cref="Measure"/> per match.
    /// </summary>
    public sealed class RealismAnalyzer
    {
        private const int MsPerMinute = 60_000;
        private const int OpenGoalRangeDm = 200;
        private const int OpenGoalWindowMs = 1_500;
        private const int SterileMinMs = 20_000;
        private const int SterileProgressDm = 100;

        private readonly OffTargetMeter _offTarget = new OffTargetMeter();
        private readonly int[] _keeper = new int[2];
        private int[] _sentOffFrom = Array.Empty<int>();

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static RealismMetrics? Analyze(MatchReport report) => new RealismAnalyzer().Measure(report);

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public RealismMetrics? Measure(MatchReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0) return null;

            int ticks = s.TickCount;
            _keeper[0] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: true);
            _keeper[1] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: false);
            FindSentOff(s);

            var m = new RealismMetrics();
            CountOpenGoals(s, ticks, m);
            WalkPossessions(s, ticks, m);
            m.MedianOffTargetSeconds = _offTarget.Measure(s, ticks, _keeper, _sentOffFrom);
            return m;
        }

        /// <summary>Off-target seconds of one man in the last <see cref="Measure"/> (0 for a keeper).</summary>
        public double OffTargetSeconds(bool home, int slot) => _offTarget.Seconds(home, slot);

        /// <summary>The frame each slot was sent off on (int.MaxValue when he never was), side-major.</summary>
        private void FindSentOff(PositionStream s)
        {
            int n = s.PlayerCount;
            if (_sentOffFrom.Length != 2 * n) _sentOffFrom = new int[2 * n];
            for (int i = 0; i < _sentOffFrom.Length; i++) _sentOffFrom[i] = int.MaxValue;

            foreach (BallAction a in s.Actions)
            {
                if (a.Kind != BallActionKind.RedCard || a.Slot < 0 || a.Slot >= n) continue;
                int k = (a.Home ? 0 : n) + a.Slot;
                if (a.Tick < _sentOffFrom[k]) _sentOffFrom[k] = a.Tick;
            }
        }

        // ------------------------------------------------------------------ R4: open goal

        private void CountOpenGoals(PositionStream s, int ticks, RealismMetrics m)
        {
            int tpm = s.TicksPerMinute > 0 ? s.TicksPerMinute : 1;
            int window = (OpenGoalWindowMs * tpm + MsPerMinute - 1) / MsPerMinute;
            int spellOwner = PositionStream.NoOwner;

            for (int t = 0; t < ticks; t++)
            {
                int code = s.Owner[t];
                if (code == PositionStream.NoOwner) continue;
                if (!LaneIsOpen(s, t, code))
                {
                    spellOwner = PositionStream.NoOwner;
                    continue;
                }

                if (code == spellOwner) continue;
                spellOwner = code;
                m.OpenGoalChances++;

                s.TryOwner(code, out bool home, out int slot);
                if (ShotWithin(s.Actions, home, slot, t, t + window)) m.OpenGoalShots++;
            }
        }

        private bool LaneIsOpen(PositionStream s, int t, int code)
        {
            s.TryOwner(code, out bool home, out int _);
            long bx = s.BallXY[t * 2], by = s.BallXY[t * 2 + 1];
            long goalX = MovementGeometry.AttackedGoalX(home);
            long dx = goalX - bx, dy = Pitch.CenterY - by;
            if (dx * dx + dy * dy > (long)OpenGoalRangeDm * OpenGoalRangeDm) return false;

            int n = s.PlayerCount;
            int foe = home ? 1 : 0;
            int[] xy = home ? s.AwayXY : s.HomeXY;
            long lowPost = Pitch.CenterY - MovementGeometry.GoalHalfWidthDm;
            long highPost = Pitch.CenterY + MovementGeometry.GoalHalfWidthDm;

            for (int slot = 0; slot < n; slot++)
            {
                if (slot == _keeper[foe] || t >= _sentOffFrom[foe * n + slot]) continue;
                if (InTriangle(s.PlayerX(xy, t, slot), s.PlayerY(xy, t, slot),
                        bx, by, goalX, lowPost, goalX, highPost))
                    return false;
            }

            return true;
        }

        private static bool ShotWithin(List<BallAction> actions, bool home, int slot, int from, int to)
        {
            foreach (BallAction a in actions)
            {
                if (a.Tick > to) break;
                if (a.Tick >= from && a.Kind == BallActionKind.Shot && a.Home == home && a.Slot == slot)
                    return true;
            }

            return false;
        }

        /// <summary>Inclusive of the edges: a defender standing on the line to a post blocks it.</summary>
        private static bool InTriangle(long px, long py, long ax, long ay, long bx, long by, long cx, long cy)
        {
            long d1 = Cross(px, py, ax, ay, bx, by);
            long d2 = Cross(px, py, bx, by, cx, cy);
            long d3 = Cross(px, py, cx, cy, ax, ay);
            bool negative = d1 < 0 || d2 < 0 || d3 < 0;
            bool positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive);
        }

        private static long Cross(long px, long py, long ax, long ay, long bx, long by) =>
            (bx - ax) * (py - ay) - (by - ay) * (px - ax);

        // ------------------------------------------------------------------ R5 / R7: possessions

        private struct Spell
        {
            public int Side;            // 0 home, 1 away, -1 nobody
            public int Start;
            public int Last;
            public int StartX;
            public bool PassPending;
            public bool PassIntoFinalThird;
            public bool OpenPlay;
            public bool HeldOutsideBox;
            public bool Entered;
        }

        private void WalkPossessions(PositionStream s, int ticks, RealismMetrics m)
        {
            int tpm = s.TicksPerMinute > 0 ? s.TicksPerMinute : 1;
            var spell = new Spell { Side = -1 };
            bool homeSetPiece = false, awaySetPiece = false;
            List<BallAction> actions = s.Actions;
            int next = 0;

            for (int t = 0; t < ticks; t++)
            {
                int code = s.Owner[t];
                if (code != PositionStream.NoOwner)
                {
                    s.TryOwner(code, out bool home, out int _);
                    int side = home ? 0 : 1;
                    int x = s.BallXY[t * 2], y = s.BallXY[t * 2 + 1];

                    if (spell.Side != side)
                    {
                        Close(ref spell, s, tpm, m);
                        bool setPiece = home ? homeSetPiece : awaySetPiece;
                        homeSetPiece = awaySetPiece = false;
                        spell = new Spell { Side = side, Start = t, StartX = x, OpenPlay = !setPiece };
                    }
                    else if (spell.PassPending)
                    {
                        spell.PassPending = false;
                        if (InFinalThird(home, x)) spell.PassIntoFinalThird = true;
                    }

                    spell.Last = t;
                    if (!InOpponentBox(home, x, y)) spell.HeldOutsideBox = true;
                    else if (spell.OpenPlay && spell.HeldOutsideBox && !spell.Entered)
                    {
                        spell.Entered = true;
                        if (home) m.HomeBoxEntries++;
                        else m.AwayBoxEntries++;
                    }
                }

                for (; next < actions.Count && actions[next].Tick <= t; next++)
                {
                    BallAction a = actions[next];
                    if (IsPass(a.Kind))
                    {
                        if (spell.Side == (a.Home ? 0 : 1)) spell.PassPending = true;
                    }
                    else if (IsRestart(a.Kind))
                    {
                        Close(ref spell, s, tpm, m);
                        if (IsSetPiece(a.Kind))
                        {
                            if (a.Home) homeSetPiece = true;
                            else awaySetPiece = true;
                        }
                    }
                }
            }

            Close(ref spell, s, tpm, m);
        }

        private static void Close(ref Spell spell, PositionStream s, int tpm, RealismMetrics m)
        {
            if (spell.Side < 0) return;
            m.Possessions++;

            long durationMs = (long)(spell.Last - spell.Start) * MsPerMinute / tpm;
            int endX = s.BallXY[spell.Last * 2];
            int progress = spell.Side == 0 ? endX - spell.StartX : spell.StartX - endX;
            if (durationMs >= SterileMinMs && progress < SterileProgressDm && !spell.PassIntoFinalThird)
                m.SterilePossessions++;

            spell = new Spell { Side = -1 };
        }

        private static bool InFinalThird(bool home, int x) =>
            home ? x >= 2 * Pitch.LengthDm / 3 : x < Pitch.LengthDm / 3;

        private static bool InOpponentBox(bool home, int x, int y)
        {
            bool deep = home ? x >= Pitch.LengthDm - MovementGeometry.BoxDepthDm : x <= MovementGeometry.BoxDepthDm;
            int across = y - Pitch.CenterY;
            return deep && across <= MovementGeometry.BoxHalfWidthDm && -across <= MovementGeometry.BoxHalfWidthDm;
        }

        private static bool IsPass(BallActionKind k) =>
            k == BallActionKind.Pass || k == BallActionKind.LongBall || k == BallActionKind.Cross;

        private static bool IsSetPiece(BallActionKind k) =>
            k == BallActionKind.Corner || k == BallActionKind.FreeKick || k == BallActionKind.Penalty;

        private static bool IsRestart(BallActionKind k) =>
            IsSetPiece(k)
            || k == BallActionKind.ThrowIn
            || k == BallActionKind.GoalKick
            || k == BallActionKind.Kickoff
            || k == BallActionKind.Goal
            || k == BallActionKind.HalfTime;
    }
}
