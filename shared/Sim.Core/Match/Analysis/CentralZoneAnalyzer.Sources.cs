using System;
using Sim.Core.Config;
using Sim.Core.Match.Movement;
using Sim.Core.Match.Movement.Models;

namespace Sim.Core.Match.Analysis
{
    /// <summary>How the ball reached a central-zone or penalty-area spell (issue #210).</summary>
    public enum ChanceSource
    {
        /// <summary>He ran it in: the spell began outside the zone (or the box) and got there.</summary>
        Carry = 0,
        /// <summary>A team-mate's pass struck from behind the second-last opponent's line, taken beyond it.</summary>
        ThroughBall = 1,
        /// <summary>A team-mate's pass from the wide channel of the final third into the box (doc §11.3).</summary>
        Cross = 2,
        /// <summary>A team-mate's pass from within the box's depth, played back away from the goal line.</summary>
        CutBack = 3,
        /// <summary>Any other team-mate's pass.</summary>
        Pass = 4,
        /// <summary>A ball the other side last kicked or touched (a clearance, a block, an interception).</summary>
        SecondBall = 5,
        /// <summary>His own side's shot, last touched by nobody but the keeper or a blocker.</summary>
        Rebound = 6,
        /// <summary>The ball taken off a man of the other side who held it (a tackle).</summary>
        WonBack = 7,
        /// <summary>The first kick after a restart (a corner, a free kick, a throw-in).</summary>
        SetPiece = 8,
        /// <summary>Anything else (his own kick come back to him).</summary>
        Other = 9,
    }

    public sealed partial class CentralZoneAnalyzer
    {
        public const int SourceCount = 10;
        public const int CutBackMarginDm = 20;

        // What the last shot of a side was, when it was not a labelled spell's: one from outside the
        // box, or a set-piece taker's (a penalty, a direct free kick).
        private const int OutsideShot = -2;
        private const int RestartShot = -3;

        private static readonly ActionModelBalance XgModel = new ActionModelBalance();

        // The last kick heard (any side), whether it was a restart's, and per side whether that side
        // touched the ball since without a kick of its own.
        private BallAction _kick;
        private bool _hasKick, _kickFromRestart, _restartPending;
        private readonly bool[] _touchedSinceKick = new bool[2];
        private int _lastHolder, _lastHeldAt;
        private readonly int[] _lastShotSource = { -1, -1 };

        // The spell being followed: what brought him the ball, and the source of its box and zone arrival.
        private ChanceSource _source, _boxSource, _zoneSource;
        private bool _visited, _shot;
        private int _lastFrame, _startDm;
        private int _shotXg;

        private void ResetSources()
        {
            _hasKick = _kickFromRestart = _restartPending = false;
            _touchedSinceKick[0] = _touchedSinceKick[1] = false;
            _lastHolder = PositionStream.NoOwner;
            _lastHeldAt = -1;
            _lastShotSource[0] = _lastShotSource[1] = -1;
            _shot = false;
        }

        /// <summary>Every action the walk has dealt with, in order: the kick and touch history the next spell reads.</summary>
        private void Heard(BallAction a)
        {
            int side = a.Home ? 0 : 1;
            if (IsKick(a.Kind))
            {
                _kick = a;
                _hasKick = true;
                _kickFromRestart = _restartPending;
                _restartPending = false;
                _touchedSinceKick[0] = _touchedSinceKick[1] = false;
                return;
            }

            if (a.Kind == BallActionKind.Goal)
            {
                int source = _lastShotSource[side];
                if (source >= 0) _m.AddGoal(a.Home, (ChanceSource)source);
                else if (source == OutsideShot) _m.AddOutsideGoal(a.Home);
                else if (source == RestartShot) _m.AddRestartGoal(a.Home);
                _lastShotSource[side] = -1;
                return;
            }

            if (a.Slot >= 0 && IsTouch(a.Kind)) _touchedSinceKick[side] = true;
        }

        /// <summary>The spell ends with his own kick: a shot is credited to the spell's source.</summary>
        private void EndWith(BallAction a)
        {
            int side = a.Home ? 0 : 1;
            if (a.Kind != BallActionKind.Shot) return;
            int f = a.Tick > 0 ? a.Tick - 1 : 0;
            int[] xy = a.Home ? _s.HomeXY : _s.AwayXY;
            int xg = ExpectedGoals.Permille(_s.PlayerX(xy, f, a.Slot), _s.PlayerY(xy, f, a.Slot), a.Home, 0, 50, 50, XgModel);
            if (!_countable)
            {
                _lastShotSource[side] = RestartShot;
                return;
            }

            if (!_box && !_zone)
            {
                _lastShotSource[side] = OutsideShot;
                _m.AddOutsideShot(a.Home, xg);
                return;
            }

            _shot = true;
            _shotXg = xg;
            _lastShotSource[side] = (int)Label();
        }

        /// <summary>A spell's own label: its zone arrival's when it reached the zone, else its box arrival's.</summary>
        private ChanceSource Label() => _zone ? _zoneSource : _boxSource;

        private void AddSources(bool home)
        {
            if (_box) _m.AddBoxSource(home, _boxSource, !_boxFree);
            if (_zone) _m.AddZoneSource(home, _zoneSource, !_free);
            if (_zone && _zoneSource == ChanceSource.Carry) _m.AddZoneCarry(home, _source, _startDm);
            if (_shot) _m.AddShot(home, Label(), _shotXg);
        }

        /// <summary>What brought the man <paramref name="code"/> the ball, read before his spell is heard.</summary>
        private ChanceSource Classify(int code)
        {
            _s.TryOwner(code, out bool home, out int slot);
            int side = home ? 0 : 1;
            int kicker = _hasKick ? _s.OwnerCode(_kick.Home, _kick.Slot) : PositionStream.NoOwner;
            bool heldSince = _lastHolder != PositionStream.NoOwner
                             && (_lastHolder == kicker ? _lastHeldAt > _kick.Tick : _lastHeldAt >= _kick.Tick);

            if (!_hasKick || heldSince)
            {
                if (_lastHolder == PositionStream.NoOwner) return ChanceSource.Other;
                _s.TryOwner(_lastHolder, out bool holderHome, out _);
                return holderHome != home ? ChanceSource.WonBack : ChanceSource.Other;
            }

            if (_kick.Home != home) return ChanceSource.SecondBall;
            if (_touchedSinceKick[1 - side])
                return _kick.Kind == BallActionKind.Shot ? ChanceSource.Rebound : ChanceSource.SecondBall;
            if (_kickFromRestart) return ChanceSource.SetPiece;
            if (_kick.Kind == BallActionKind.Shot) return ChanceSource.Rebound;
            if (_kick.Slot == slot) return ChanceSource.Other;
            return PassSource(home, slot);
        }

        /// <summary>The team-mate's pass by its geometry: cut-back, cross, through ball, or a pass.</summary>
        private ChanceSource PassSource(bool home, int receiver)
        {
            int f = _kick.Tick > 0 ? _kick.Tick - 1 : 0;
            int[] own = home ? _s.HomeXY : _s.AwayXY;
            int fromX = _s.PlayerX(own, f, _kick.Slot), fromY = _s.PlayerY(own, f, _kick.Slot);
            int goalX = MovementGeometry.AttackedGoalX(home);
            int fromDepth = Math.Abs(goalX - fromX);

            // Where he meets it: where he stands now (a first-time kick's frame before it, else this one).
            int now = _lastFrame;
            int toX = _s.PlayerX(own, now, receiver), toY = _s.PlayerY(own, now, receiver);
            int toDepth = Math.Abs(goalX - toX);

            if (fromDepth <= MovementGeometry.BoxDepthDm && toDepth - fromDepth >= CutBackMarginDm) return ChanceSource.CutBack;
            if (GeometricCrossAnalyzer.WideInFinalThird(home, fromX, fromY) && GeometricCrossAnalyzer.InAttackedBox(home, toX, toY))
                return ChanceSource.Cross;
            if (IsThroughBall(home, f, fromX, toX, receiver)) return ChanceSource.ThroughBall;
            return ChanceSource.Pass;
        }

        /// <summary>
        /// Struck from behind the second-last opponent's line at release (keeper included) to a man
        /// not beyond it then, and taken beyond it: <see cref="BallFreedomAnalyzer"/>'s geometry.
        /// </summary>
        private bool IsThroughBall(bool home, int release, int fromX, int toX, int receiver)
        {
            int n = _s.PlayerCount;
            int[] own = home ? _s.HomeXY : _s.AwayXY;
            int[] opp = home ? _s.AwayXY : _s.HomeXY;
            int foe = home ? 1 : 0;
            int last = int.MinValue, line = int.MinValue;
            for (int j = 0; j < n; j++)
            {
                if (release >= _sentOffFrom[foe * n + j]) continue;
                int d = Depth(home, _s.PlayerX(opp, release, j));
                if (d > last)
                {
                    line = last;
                    last = d;
                }
                else if (d > line) line = d;
            }

            return line != int.MinValue && Depth(home, fromX) < line && Depth(home, toX) > line
                   && Depth(home, _s.PlayerX(own, release, receiver)) <= line;
        }

        private static int Depth(bool home, int x) => home ? x : Pitch.LengthDm - x;

        // A man of a side touching the ball without kicking it: what makes the next ball a second one.
        private static bool IsTouch(BallActionKind k) =>
            k == BallActionKind.Interception || k == BallActionKind.Recovery || k == BallActionKind.Save
            || k == BallActionKind.Block;
    }
}
