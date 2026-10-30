using System;
using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The striker offside-position reading of the free-ball spec (R2, against the R1 band
    /// <see cref="RealismReference.Tracking.StrikerOffsidePercent"/>), counted off a finished match's
    /// position stream with the definition of docs/research/football-reference.md §10.3, and the
    /// offsides the referee called. A measuring instrument: it reads the report and touches nothing.
    ///
    /// Definitions (each side attacks the same goal all match, home toward X = LengthDm):
    ///   * Striker: every slot of the side's lineup played as <see cref="PositionRole.Striker"/>,
    ///     the centre-forward role (the reference counts every declared forward: DFL STZ, STL, STR),
    ///     while he is on the pitch. A substitute inherits the slot and so the role.
    ///   * Counted frames: open play (<see cref="OpenPlayFrames"/>) with his team in possession: the
    ///     side of the man holding the ball, carried through a loose ball or a pass in flight until
    ///     the other side holds it; nobody's from a dead ball until somebody holds it again.
    ///   * Offside position: more than <see cref="MarginDm"/> beyond the second-last opponent on the
    ///     pitch (keeper included), beyond the ball and in the opponent half, all strictly.
    ///   * Offsides: every <see cref="BallActionKind.Offside"/> the referee filed, both sides.
    ///
    /// Reusable: keep one analyzer and call <see cref="Measure"/> per match.
    /// </summary>
    public sealed class StrikerOffsideAnalyzer
    {
        /// <summary>0.3 m beyond the second-last opponent, doc §10.3.</summary>
        public const int MarginDm = 3;

        private const int NoSide = -1;

        private bool[] _open = Array.Empty<bool>();
        private int[] _sentOffFrom = Array.Empty<int>();
        private readonly List<int>[] _strikers = { new List<int>(), new List<int>() };

        /// <summary>
        /// Measures one match. <paramref name="homeRoles"/> and <paramref name="awayRoles"/> are the
        /// sides' lineup roles by slot (the stream carries no roles). Returns null when the report
        /// carries no position stream.
        /// </summary>
        public static StrikerOffsideMetrics? Analyze(
            MatchReport report, IReadOnlyList<PositionRole> homeRoles, IReadOnlyList<PositionRole> awayRoles) =>
            new StrikerOffsideAnalyzer().Measure(report, homeRoles, awayRoles);

        /// <inheritdoc cref="Analyze"/>
        public StrikerOffsideMetrics? Measure(
            MatchReport report, IReadOnlyList<PositionRole> homeRoles, IReadOnlyList<PositionRole> awayRoles)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (homeRoles == null) throw new ArgumentNullException(nameof(homeRoles));
            if (awayRoles == null) throw new ArgumentNullException(nameof(awayRoles));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0) return null;

            int ticks = s.TickCount, n = s.PlayerCount;
            if (_open.Length < ticks) _open = new bool[ticks];
            if (_sentOffFrom.Length != 2 * n) _sentOffFrom = new int[2 * n];
            OpenPlayFrames.Mark(s, ticks, _open);
            OpenPlayFrames.SentOff(s, _sentOffFrom);
            FindStrikers(homeRoles, n, _strikers[0]);
            FindStrikers(awayRoles, n, _strikers[1]);

            var m = new StrikerOffsideMetrics();
            foreach (BallAction a in s.Actions)
                if (a.Kind == BallActionKind.Offside) m.Offsides++;

            int possession = NoSide;
            for (int t = 0; t < ticks; t++)
            {
                if (!_open[t])
                {
                    possession = NoSide;
                    continue;
                }

                if (s.TryOwner(s.Owner[t], out bool ownerHome, out _)) possession = ownerHome ? 0 : 1;
                if (possession == NoSide) continue;

                foreach (int k in _strikers[possession])
                {
                    if (!OnPitch(possession, k, t, n)) continue;
                    m.PossessionFrames++;
                    if (InOffsidePosition(s, t, possession, k)) m.OffsideFrames++;
                }
            }

            return m;
        }

        private static void FindStrikers(IReadOnlyList<PositionRole> roles, int n, List<int> into)
        {
            into.Clear();
            for (int k = 0; k < roles.Count && k < n; k++)
                if (roles[k] == PositionRole.Striker) into.Add(k);
        }

        private bool OnPitch(int side, int k, int t, int n) => t < _sentOffFrom[side * n + k];

        /// <summary>The striker of <paramref name="side"/> in slot k, at frame t, against §10.3.</summary>
        private bool InOffsidePosition(PositionStream s, int t, int side, int k)
        {
            bool home = side == 0;
            int[] own = home ? s.HomeXY : s.AwayXY;
            int[] opp = home ? s.AwayXY : s.HomeXY;
            int striker = Depth(home, s.PlayerX(own, t, k));
            if (striker <= Pitch.CenterX) return false;
            if (striker <= Depth(home, s.BallAt(t).X)) return false;

            int last = int.MinValue, secondLast = int.MinValue;
            for (int j = 0; j < s.PlayerCount; j++)
            {
                if (!OnPitch(1 - side, j, t, s.PlayerCount)) continue;
                int d = Depth(home, s.PlayerX(opp, t, j));
                if (d > last)
                {
                    secondLast = last;
                    last = d;
                }
                else if (d > secondLast) secondLast = d;
            }

            return secondLast != int.MinValue && striker > secondLast + MarginDm;
        }

        /// <summary>How far toward the goal the side attacks, from its own goal line.</summary>
        private static int Depth(bool home, int x) => home ? x : Pitch.LengthDm - x;
    }
}
