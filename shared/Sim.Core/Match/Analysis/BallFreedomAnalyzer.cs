using System;
using System.Collections.Generic;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The ball-freedom, crowding, height, pass-flight and loose-ball readings of the
    /// free-ball-real-movement spec (R2, against the R1 bands of <see cref="RealismReference.Tracking"/>),
    /// counted off a finished match's position stream with the definitions of
    /// docs/research/football-reference.md §10.3 as tools/tracking_reference.py computes them, so the
    /// engine and the tracking data are read alike. A measuring instrument: it reads the report and
    /// touches nothing.
    ///
    /// Where the stream differs from tracking data:
    ///   * Ball in play: the tracking's ball status (DFL), which goes live on the restart's kick. So
    ///     dead from a restart's whistle, a goal or half-time until the next action that plays the
    ///     ball is filed (the taker's kick), as the broadcast's dead-ball reader reads it; unlike
    ///     the movement analyzers' open play, the taker standing over the ball is not in play.
    ///     Every in-play frame is measured (the engine knows every position), so a per-match count
    ///     needs no rescaling.
    ///   * Players: both sides, keepers included, men already sent off left out (tracking loses them).
    ///   * Holder: the nearest player by position, within reach, not the stream's Owner: the
    ///     reference has no owner, only positions. Ties go to the lower index, home before away.
    ///   * Spans: the script turns each span into frames as round(span × fps), Python's
    ///     half-to-even rounding, which <see cref="Math.Round(double)"/> matches.
    ///
    /// Readings:
    ///   * Nobody within 1.5 m / 3 m: share of in-play frames whose nearest player is further.
    ///   * Players within 5 m / 10 m: mean count, strictly closer, over in-play frames.
    ///   * Ball above 1 m: share of in-play frames with BallZ over 10 dm (no BallZ: on the grass).
    ///   * Pass: frames are labelled holder, free or dead. A holder run of at least 0.2 s, then free
    ///     frames only, then a run of a different team-mate of at least 0.2 s. A shorter run by
    ///     anybody in between, or a dead frame, drops it. Release: the passer's last frame; arrival:
    ///     the receiver's first; flights over 6 s are dropped. Flight p50/p90 and the receiver's
    ///     distance at release to the ball at arrival (p50, and the share at or over 3 m), per match,
    ///     with numpy's linear percentile.
    ///   * Loose-ball episode: a free run of at least 1 s whose last holder held the ball under
    ///     0.4 s, either in a duel (both teams within reach on one of the 0.5 s of frames before it)
    ///     or, failing that, after a holder of the other team than his predecessor.
    ///
    /// Reusable: keep one analyzer and call <see cref="Measure"/> per match.
    /// </summary>
    public sealed class BallFreedomAnalyzer
    {
        public const int ReachDm = 15;
        public const int ClearDm = 30;
        public const int NearDm = 50;
        public const int WideDm = 100;
        public const int HighDm = 10;
        public const int SpaceDm = 30;
        public const int ControlMinMs = 200;
        public const int FlightMaxMs = 6000;
        public const int LooseMinMs = 1000;
        public const int DuelLookMs = 500;
        public const int TouchMaxMs = 400;

        private const int Free = -1;
        private const int Dead = -2;

        private int[] _label = Array.Empty<int>();
        private bool[] _duel = Array.Empty<bool>();
        private int[] _sentOffFrom = Array.Empty<int>();
        private readonly List<double> _flights = new List<double>();
        private readonly List<double> _spaces = new List<double>();
        private int _n;

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static BallFreedomMetrics? Analyze(MatchReport report) => new BallFreedomAnalyzer().Measure(report);

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public BallFreedomMetrics? Measure(MatchReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0 || s.TicksPerMinute <= 0) return null;

            int ticks = s.TickCount;
            Ensure(s.PlayerCount, ticks);
            FindSentOff(s);
            var m = new BallFreedomMetrics();
            LabelFrames(s, ticks, m);
            ReadPasses(s, ticks, m);
            CountLooseBalls(s, ticks, m);
            return m;
        }

        private void Ensure(int n, int ticks)
        {
            _n = n;
            if (_label.Length < ticks)
            {
                _label = new int[ticks];
                _duel = new bool[ticks];
            }

            if (_sentOffFrom.Length != 2 * n) _sentOffFrom = new int[2 * n];
        }

        private void FindSentOff(PositionStream s)
        {
            for (int i = 0; i < _sentOffFrom.Length; i++) _sentOffFrom[i] = int.MaxValue;
            foreach (BallAction a in s.Actions)
            {
                if (a.Kind != BallActionKind.RedCard || a.Slot < 0 || a.Slot >= _n) continue;
                int k = (a.Home ? 0 : _n) + a.Slot;
                if (a.Tick < _sentOffFrom[k]) _sentOffFrom[k] = a.Tick;
            }
        }

        // ------------------------------------------------------------------ frames: freedom, crowding, height

        private void LabelFrames(PositionStream s, int ticks, BallFreedomMetrics m)
        {
            const long reach2 = (long)ReachDm * ReachDm, clear2 = (long)ClearDm * ClearDm;
            const long near2 = (long)NearDm * NearDm, wide2 = (long)WideDm * WideDm;
            bool dead = false;
            int next = 0;
            for (int t = 0; t < ticks; t++)
            {
                for (; next < s.Actions.Count && s.Actions[next].Tick <= t; next++)
                {
                    BallActionKind k = s.Actions[next].Kind;
                    if (IsRestart(k) || k == BallActionKind.Goal || k == BallActionKind.HalfTime) dead = true;
                    else if (IsPlay(k)) dead = false;
                }

                int bx = s.BallXY[t * 2], by = s.BallXY[t * 2 + 1];
                long nearest2 = long.MaxValue;
                int holder = Free, within5 = 0, within10 = 0;
                bool homeInReach = false, awayInReach = false;
                for (int i = 0; i < 2 * _n; i++)
                {
                    if (t >= _sentOffFrom[i]) continue;
                    int[] xy = i < _n ? s.HomeXY : s.AwayXY;
                    long dx = s.PlayerX(xy, t, i % _n) - bx, dy = s.PlayerY(xy, t, i % _n) - by;
                    long d2 = dx * dx + dy * dy;
                    if (d2 < nearest2)
                    {
                        nearest2 = d2;
                        holder = i;
                    }

                    if (d2 < near2) within5++;
                    if (d2 < wide2) within10++;
                    if (d2 <= reach2)
                    {
                        if (i < _n) homeInReach = true;
                        else awayInReach = true;
                    }
                }

                _duel[t] = homeInReach && awayInReach;
                if (dead || holder == Free)
                {
                    _label[t] = Dead;
                    continue;
                }

                _label[t] = nearest2 <= reach2 ? holder : Free;
                m.InPlayFrames++;
                if (nearest2 > reach2) m.Free15Frames++;
                if (nearest2 > clear2) m.Free3Frames++;
                m.Within5Sum += within5;
                m.Within10Sum += within10;
                if (s.BallZAt(t) > HighDm) m.Above1mFrames++;
            }
        }

        // ------------------------------------------------------------------ passes

        private void ReadPasses(PositionStream s, int ticks, BallFreedomMetrics m)
        {
            int ctrl = Frames(s, ControlMinMs);
            double flightMax = (double)FlightMaxMs * s.TicksPerMinute / 60_000;
            _flights.Clear();
            _spaces.Clear();

            int prevEnd = 0, prevLabel = Dead;
            bool clean = true;
            for (int a = 0; a < ticks;)
            {
                int lab = _label[a], b = a;
                while (b < ticks && _label[b] == lab) b++;

                if (lab == Free)
                {
                    a = b;
                    continue;
                }

                if (lab == Dead || b - a < ctrl)
                {
                    if (lab == Dead) prevLabel = Dead;
                    clean = false;
                    a = b;
                    continue;
                }

                if (prevLabel != Dead && clean && prevLabel != lab && (prevLabel < _n) == (lab < _n))
                    AddPass(s, prevEnd - 1, a, lab, flightMax);
                prevEnd = b;
                prevLabel = lab;
                clean = true;
                a = b;
            }

            m.Passes = _flights.Count;
            if (_flights.Count == 0) return;
            foreach (double d in _spaces)
                if (d >= SpaceDm / 10.0) m.PassesIntoSpace++;
            m.FlightP50Seconds = Percentile(_flights, 50);
            m.FlightP90Seconds = Percentile(_flights, 90);
            m.ReceiverToArrivalP50Meters = Percentile(_spaces, 50);
        }

        private void AddPass(PositionStream s, int release, int arrival, int receiver, double flightMax)
        {
            if (arrival - release > flightMax) return;
            int[] xy = receiver < _n ? s.HomeXY : s.AwayXY;
            double dx = s.PlayerX(xy, release, receiver % _n) - s.BallXY[arrival * 2];
            double dy = s.PlayerY(xy, release, receiver % _n) - s.BallXY[arrival * 2 + 1];
            _flights.Add((arrival - release) * 60.0 / s.TicksPerMinute);
            _spaces.Add(Math.Sqrt(dx * dx + dy * dy) / 10.0);
        }

        /// <summary>numpy's default percentile: linear between the closest ranks of the sorted values.</summary>
        private static double Percentile(List<double> values, double p)
        {
            values.Sort();
            double rank = p / 100 * (values.Count - 1);
            int lo = (int)Math.Floor(rank);
            int hi = Math.Min(lo + 1, values.Count - 1);
            return values[lo] + (values[hi] - values[lo]) * (rank - lo);
        }

        // ------------------------------------------------------------------ loose balls

        private void CountLooseBalls(PositionStream s, int ticks, BallFreedomMetrics m)
        {
            int look = Frames(s, DuelLookMs), touch = Frames(s, TouchMaxMs);
            double minRun = (double)LooseMinMs * s.TicksPerMinute / 60_000;
            int holders = 0, lastLength = 0;
            bool lastHome = false, beforeHome = false;
            for (int a = 0; a < ticks;)
            {
                int lab = _label[a], b = a;
                while (b < ticks && _label[b] == lab) b++;
                int start = a;
                a = b;

                if (lab == Dead)
                {
                    holders = 0;
                    continue;
                }

                if (lab != Free)
                {
                    beforeHome = lastHome;
                    lastHome = lab < _n;
                    lastLength = b - start;
                    holders++;
                    continue;
                }

                // A controlled release (the last holder kept it >= TouchMaxMs) is a pass, not a loose ball.
                if (b - start < minRun || holders == 0 || lastLength >= touch) continue;
                if (DuelBefore(start, look)) m.LooseDuels++;
                else if (holders >= 2 && lastHome != beforeHome) m.LooseDeflections++;
            }
        }

        private bool DuelBefore(int start, int look)
        {
            for (int t = Math.Max(0, start - look); t < start; t++)
                if (_duel[t]) return true;
            return false;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A span in frames as the script rounds it: round(seconds × fps), half to even.</summary>
        private static int Frames(PositionStream s, int ms) => (int)Math.Round((double)ms * s.TicksPerMinute / 60_000);

        /// <summary>A touch that plays the ball: the kinds the broadcast's dead-ball reader ends a stoppage on.</summary>
        internal static bool IsPlay(BallActionKind k) =>
            k == BallActionKind.Pass || k == BallActionKind.LongBall || k == BallActionKind.Cross
            || k == BallActionKind.Dribble || k == BallActionKind.Recovery || k == BallActionKind.Interception
            || k == BallActionKind.Clearance || k == BallActionKind.Shot || k == BallActionKind.Save
            || k == BallActionKind.Miss || k == BallActionKind.Block;

        internal static bool IsRestart(BallActionKind k) =>
            k == BallActionKind.Kickoff || k == BallActionKind.ThrowIn || k == BallActionKind.GoalKick
            || k == BallActionKind.FreeKick || k == BallActionKind.Corner || k == BallActionKind.Penalty;
    }
}
