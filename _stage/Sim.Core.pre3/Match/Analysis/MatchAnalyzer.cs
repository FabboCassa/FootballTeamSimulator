using System;
using System.Collections.Generic;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Reads a finished match and counts what happened in it (phase 0 of the match engine rework,
    /// docs/engine/MATCH_ENGINE_PLAN.md). It is a MEASURING INSTRUMENT: it consumes a
    /// <see cref="MatchReport"/> and its <see cref="PositionStream"/> and touches nothing, so a
    /// reading added here can never move a result.
    ///
    /// Two families of reading:
    ///   * events — goals, shots, passes and their accuracy, restarts, duels — counted off the
    ///     ball's action list and the per-tick owner track, which together are the only honest
    ///     record of what the picture showed;
    ///   * shape — width, depth, the back line, the holes between the lines, how much of the time
    ///     a player has an opponent breathing on him — sampled off the position arrays.
    ///
    /// The point of the second family is that "the movement makes no sense" is not a number. Width,
    /// depth, back-line spread and opponent proximity ARE numbers, and they say the same thing in a
    /// form we can hold a rewrite to.
    ///
    /// Distances are reported in metres and kilometres; the stream stores decimetres.
    ///
    /// Reusable: keep one analyzer and call <see cref="Measure"/> per match, and the per-tick work
    /// allocates nothing.
    /// </summary>
    public sealed class MatchAnalyzer
    {
        /// <summary>Decimetres in a metre. The stream is in decimetres; every reading here is not.</summary>
        private const int DmPerM = 10;

        /// <summary>An opponent inside this distance counts as being ON a player, for the marking probe.</summary>
        private const int MarkedRadiusDm = 30;

        /// <summary>How long a pass is followed before it is written off as never having arrived.</summary>
        private const int PassFollowMinutes = 3;

        private int _n;
        private int[] _depth = Array.Empty<int>();
        private int[] _keeperSlot = new int[2];
        private double[] _distance = Array.Empty<double>();

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static MatchMetrics? Analyze(MatchReport report) => new MatchAnalyzer().Measure(report);

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public MatchMetrics? Measure(MatchReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            PositionStream? stream = report.Positions;
            if (stream == null || stream.PlayerCount <= 0 || stream.TickCount <= 0) return null;

            EnsureBuffers(stream.PlayerCount);
            int ticks = stream.TickCount;

            var metrics = new MatchMetrics
            {
                Ticks = ticks,
                TicksPerMinute = stream.TicksPerMinute,
                PlayerCount = _n,
                ReportGoals = report.HomeGoals + report.AwayGoals
            };

            var home = new SideMetrics { Goals = report.HomeGoals };
            var away = new SideMetrics { Goals = report.AwayGoals };

            FindKeepers(stream, ticks);
            CountActions(stream, ref home, ref away, ref metrics);
            WalkTicks(stream, ticks, ref home, ref away, ref metrics);

            metrics.Home = home;
            metrics.Away = away;
            return metrics;
        }

        private void EnsureBuffers(int playerCount)
        {
            if (_n == playerCount && _depth.Length == playerCount) return;
            _n = playerCount;
            _depth = new int[playerCount];
            _distance = new double[playerCount * 2];
        }

        // ------------------------------------------------------------------ who is in goal

        /// <summary>
        /// The keeper is inferred rather than asked for, so the analyzer needs nothing but the
        /// stream: over ninety minutes the man who stays nearest his own goal line IS the keeper,
        /// by a margin no outfielder comes close to. Ties break on the lower slot, so the reading
        /// is deterministic.
        /// </summary>
        private void FindKeepers(PositionStream stream, int ticks)
        {
            for (int side = 0; side < 2; side++)
            {
                int[] xy = side == 0 ? stream.HomeXY : stream.AwayXY;
                int best = 0;
                long bestDepth = long.MaxValue;

                for (int slot = 0; slot < _n; slot++)
                {
                    long sum = 0;
                    for (int t = 0; t < ticks; t++)
                    {
                        int x = stream.PlayerX(xy, t, slot);
                        sum += side == 0 ? x : Pitch.LengthDm - x;
                    }

                    if (sum < bestDepth)
                    {
                        bestDepth = sum;
                        best = slot;
                    }
                }

                _keeperSlot[side] = best;
            }
        }

        /// <summary>Which slot each side keeps in goal, as inferred by the last <see cref="Measure"/>.</summary>
        public int KeeperSlot(bool home) => _keeperSlot[home ? 0 : 1];

        // ------------------------------------------------------------------ events

        private void CountActions(PositionStream stream, ref SideMetrics home, ref SideMetrics away, ref MatchMetrics metrics)
        {
            List<BallAction> actions = stream.Actions;
            int follow = PassFollowMinutes * (stream.TicksPerMinute > 0 ? stream.TicksPerMinute : 1);
            int streamGoals = 0;

            for (int i = 0; i < actions.Count; i++)
            {
                BallAction a = actions[i];
                bool isHome = a.Home;

                switch (a.Kind)
                {
                    case BallActionKind.Pass:
                    case BallActionKind.LongBall:
                    case BallActionKind.Cross:
                        {
                            bool arrived = PassArrived(stream, actions, i, follow);
                            if (isHome)
                            {
                                home.PassesAttempted++;
                                if (arrived) home.PassesCompleted++;
                                if (a.Kind == BallActionKind.LongBall) home.LongBalls++;
                                if (a.Kind == BallActionKind.Cross) home.Crosses++;
                            }
                            else
                            {
                                away.PassesAttempted++;
                                if (arrived) away.PassesCompleted++;
                                if (a.Kind == BallActionKind.LongBall) away.LongBalls++;
                                if (a.Kind == BallActionKind.Cross) away.Crosses++;
                            }

                            break;
                        }

                    case BallActionKind.Shot:
                        {
                            bool onTarget = ShotWasOnTarget(actions, i);
                            if (isHome)
                            {
                                home.Shots++;
                                if (onTarget) home.ShotsOnTarget++;
                            }
                            else
                            {
                                away.Shots++;
                                if (onTarget) away.ShotsOnTarget++;
                            }

                            break;
                        }

                    case BallActionKind.Goal: streamGoals++; break;
                    case BallActionKind.Dribble: Bump(ref home, ref away, isHome, SideCounter.Dribble); break;
                    case BallActionKind.Clearance: Bump(ref home, ref away, isHome, SideCounter.Clearance); break;
                    case BallActionKind.Tackle: Bump(ref home, ref away, isHome, SideCounter.Tackle); break;
                    case BallActionKind.Interception: Bump(ref home, ref away, isHome, SideCounter.Interception); break;
                    case BallActionKind.ThrowIn: Bump(ref home, ref away, isHome, SideCounter.ThrowIn); break;
                    case BallActionKind.Corner: Bump(ref home, ref away, isHome, SideCounter.Corner); break;
                    case BallActionKind.GoalKick: Bump(ref home, ref away, isHome, SideCounter.GoalKick); break;
                }
            }

            metrics.StreamGoals = streamGoals;
        }

        private enum SideCounter
        {
            Dribble, Clearance, Tackle, Interception, ThrowIn, Corner, GoalKick
        }

        private static void Bump(ref SideMetrics home, ref SideMetrics away, bool isHome, SideCounter counter)
        {
            if (isHome) Bump(ref home, counter);
            else Bump(ref away, counter);
        }

        private static void Bump(ref SideMetrics side, SideCounter counter)
        {
            switch (counter)
            {
                case SideCounter.Dribble: side.Dribbles++; break;
                case SideCounter.Clearance: side.Clearances++; break;
                case SideCounter.Tackle: side.TacklesWon++; break;
                case SideCounter.Interception: side.Interceptions++; break;
                case SideCounter.ThrowIn: side.ThrowIns++; break;
                case SideCounter.Corner: side.Corners++; break;
                case SideCounter.GoalKick: side.GoalKicks++; break;
            }
        }

        /// <summary>
        /// A pass is completed when the NEXT man to hold the ball plays for the side that struck
        /// it. Anything else is not: an opponent gets it, or the ball leaves the pitch first and
        /// the restart is recorded before anybody takes possession.
        /// </summary>
        private bool PassArrived(PositionStream stream, List<BallAction> actions, int index, int follow)
        {
            int from = actions[index].Tick;
            bool byHome = actions[index].Home;

            // The ball leaving the pitch settles the pass before any owner can appear.
            int restartTick = int.MaxValue;
            for (int j = index + 1; j < actions.Count; j++)
            {
                if (actions[j].Tick > from + follow) break;
                if (IsRestart(actions[j].Kind))
                {
                    restartTick = actions[j].Tick;
                    break;
                }
            }

            int last = from + follow;
            if (last >= stream.Owner.Length) last = stream.Owner.Length - 1;

            for (int t = from + 1; t <= last; t++)
            {
                if (t > restartTick) return false;
                int code = stream.Owner[t];
                if (code == PositionStream.NoOwner) continue;
                stream.TryOwner(code, out bool ownerHome, out int _);
                return ownerHome == byHome;
            }

            return false;
        }

        private static bool IsRestart(BallActionKind kind) =>
            kind == BallActionKind.ThrowIn
            || kind == BallActionKind.Corner
            || kind == BallActionKind.GoalKick
            || kind == BallActionKind.FreeKick
            || kind == BallActionKind.Goal
            || kind == BallActionKind.Kickoff;

        /// <summary>
        /// A strike is on target when the thing that settles it is a goal or a save. A miss is a
        /// miss; a strike that settles as nothing at all is not counted on target either.
        /// </summary>
        private static bool ShotWasOnTarget(List<BallAction> actions, int index)
        {
            for (int j = index + 1; j < actions.Count; j++)
            {
                BallActionKind kind = actions[j].Kind;
                if (kind == BallActionKind.Goal || kind == BallActionKind.Save) return true;
                if (kind == BallActionKind.Miss) return false;
                if (kind == BallActionKind.Shot) return false;   // the next strike: this one was never settled
            }

            return false;
        }

        // ------------------------------------------------------------------ possession, ground, shape

        private void WalkTicks(PositionStream stream, int ticks, ref SideMetrics home, ref SideMetrics away, ref MatchMetrics metrics)
        {
            var homeDefending = new ShapeAccumulator();
            var homeAttacking = new ShapeAccumulator();
            var awayDefending = new ShapeAccumulator();
            var awayAttacking = new ShapeAccumulator();

            for (int i = 0; i < _distance.Length; i++) _distance[i] = 0;

            for (int t = 0; t < ticks; t++)
            {
                int code = stream.Owner[t];
                bool held = code != PositionStream.NoOwner;
                bool ownerIsHome = false;
                if (held) stream.TryOwner(code, out ownerIsHome, out int _);

                if (!held) metrics.LooseTicks++;
                else if (ownerIsHome) home.PossessionTicks++;
                else away.PossessionTicks++;

                // Territory, from the home side's point of view.
                int ballX = stream.BallXY[t * 2];
                int ballY = stream.BallXY[t * 2 + 1];
                if (ballX < Pitch.LengthDm / 3) metrics.HomeThirdTicks++;
                else if (ballX < 2 * Pitch.LengthDm / 3) metrics.MiddleThirdTicks++;
                else metrics.AwayThirdTicks++;

                // The laws say a held ball on a line is out. The engine only tests a FREE ball,
                // so a carrier is clamped back inside instead — counted here, not fixed here.
                if (held && OnBoundary(ballX, ballY)) metrics.BallOnLineWhileHeldTicks++;

                if (t > 0) AccumulateDistance(stream, t);

                if (held)
                {
                    Sample(stream, t, side: 0, into: ownerIsHome ? homeAttacking : homeDefending);
                    Sample(stream, t, side: 1, into: ownerIsHome ? awayDefending : awayAttacking);
                }
            }

            home.Defending = homeDefending.ToMetrics();
            home.Attacking = homeAttacking.ToMetrics();
            away.Defending = awayDefending.ToMetrics();
            away.Attacking = awayAttacking.ToMetrics();

            FinishDistance(ref home, 0);
            FinishDistance(ref away, 1);
        }

        private static bool OnBoundary(int x, int y) =>
            x <= 0 || x >= Pitch.LengthDm || y <= 0 || y >= Pitch.WidthDm;

        private void AccumulateDistance(PositionStream stream, int t)
        {
            for (int side = 0; side < 2; side++)
            {
                int[] xy = side == 0 ? stream.HomeXY : stream.AwayXY;
                for (int slot = 0; slot < _n; slot++)
                {
                    double dx = stream.PlayerX(xy, t, slot) - stream.PlayerX(xy, t - 1, slot);
                    double dy = stream.PlayerY(xy, t, slot) - stream.PlayerY(xy, t - 1, slot);
                    _distance[side * _n + slot] += Math.Sqrt(dx * dx + dy * dy);
                }
            }
        }

        private void FinishDistance(ref SideMetrics side, int index)
        {
            double total = 0, max = 0, min = double.MaxValue;
            int keeper = _keeperSlot[index];

            for (int slot = 0; slot < _n; slot++)
            {
                double km = _distance[index * _n + slot] / (DmPerM * 1000.0);
                total += km;
                if (slot == keeper) continue;
                if (km > max) max = km;
                if (km < min) min = km;
            }

            side.TeamDistanceKm = total;
            side.MaxPlayerDistanceKm = max;
            side.MinPlayerDistanceKm = min == double.MaxValue ? 0 : min;
        }

        /// <summary>
        /// One tick of one side's shape: the ten outfielders only, measured from the goal that
        /// side defends so home and away readings mean the same thing and can be averaged together.
        /// </summary>
        private void Sample(PositionStream stream, int t, int side, ShapeAccumulator into)
        {
            int[] mine = side == 0 ? stream.HomeXY : stream.AwayXY;
            int[] theirs = side == 0 ? stream.AwayXY : stream.HomeXY;
            int keeper = _keeperSlot[side];

            int minY = int.MaxValue, maxY = int.MinValue;
            long sumDepth = 0, sumY = 0;
            int count = 0;
            double nearestSum = 0;
            int marked = 0;

            for (int slot = 0; slot < _n; slot++)
            {
                if (slot == keeper) continue;

                int x = stream.PlayerX(mine, t, slot);
                int y = stream.PlayerY(mine, t, slot);

                // Depth from the goal this side defends, and Y mirrored with it, so the two sides
                // are described in one frame of reference.
                int depth = side == 0 ? x : Pitch.LengthDm - x;
                int acrossY = side == 0 ? y : Pitch.WidthDm - y;

                _depth[count] = depth;
                count++;

                if (acrossY < minY) minY = acrossY;
                if (acrossY > maxY) maxY = acrossY;
                sumDepth += depth;
                sumY += acrossY;

                double nearestMate = double.MaxValue;
                for (int other = 0; other < _n; other++)
                {
                    if (other == slot || other == keeper) continue;
                    double d = Distance(stream, mine, t, slot, mine, t, other);
                    if (d < nearestMate) nearestMate = d;
                }

                if (nearestMate < double.MaxValue) nearestSum += nearestMate;

                double nearestFoe = double.MaxValue;
                for (int foe = 0; foe < _n; foe++)
                {
                    double d = Distance(stream, mine, t, slot, theirs, t, foe);
                    if (d < nearestFoe) nearestFoe = d;
                }

                if (nearestFoe <= MarkedRadiusDm) marked++;
            }

            if (count == 0) return;

            InsertionSort(_depth, count);

            int backLine = count < 4 ? count : 4;
            int backSpread = _depth[backLine - 1] - _depth[0];

            int largestGap = 0;
            for (int i = 1; i < count; i++)
            {
                int gap = _depth[i] - _depth[i - 1];
                if (gap > largestGap) largestGap = gap;
            }

            into.Samples++;
            into.WidthDm += maxY - minY;
            into.DepthDm += _depth[count - 1] - _depth[0];
            into.BackLineSpreadDm += backSpread;
            into.LargestLineGapDm += largestGap;
            into.NearestTeammateDm += nearestSum / count;
            into.CentroidXDm += (double)sumDepth / count;
            into.CentroidYDm += (double)sumY / count;
            into.PlayerSamples += count;
            into.MarkedSamples += marked;
        }

        private double Distance(PositionStream stream, int[] a, int ta, int sa, int[] b, int tb, int sb)
        {
            double dx = stream.PlayerX(a, ta, sa) - stream.PlayerX(b, tb, sb);
            double dy = stream.PlayerY(a, ta, sa) - stream.PlayerY(b, tb, sb);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static void InsertionSort(int[] values, int count)
        {
            for (int i = 1; i < count; i++)
            {
                int v = values[i];
                int j = i - 1;
                while (j >= 0 && values[j] > v)
                {
                    values[j + 1] = values[j];
                    j--;
                }

                values[j + 1] = v;
            }
        }

        private sealed class ShapeAccumulator
        {
            public int Samples;
            public double WidthDm;
            public double DepthDm;
            public double BackLineSpreadDm;
            public double LargestLineGapDm;
            public double NearestTeammateDm;
            public double CentroidXDm;
            public double CentroidYDm;
            public long PlayerSamples;
            public long MarkedSamples;

            public ShapeMetrics ToMetrics()
            {
                if (Samples == 0) return default;
                double n = Samples;
                return new ShapeMetrics
                {
                    Samples = Samples,
                    WidthM = WidthDm / n / DmPerM,
                    DepthM = DepthDm / n / DmPerM,
                    BackLineSpreadM = BackLineSpreadDm / n / DmPerM,
                    LargestLineGapM = LargestLineGapDm / n / DmPerM,
                    NearestTeammateM = NearestTeammateDm / n / DmPerM,
                    CentroidXM = CentroidXDm / n / DmPerM,
                    CentroidYM = CentroidYDm / n / DmPerM,
                    WithinThreeMetresOfOpponentPercent =
                        PlayerSamples <= 0 ? 0 : 100.0 * MarkedSamples / PlayerSamples
                };
            }
        }
    }
}
