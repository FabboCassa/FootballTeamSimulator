# Engine rework, phase 3 - defending: zones and triggers.
# Applied to shared/Sim.Core in place. Every replacement asserts its anchor.
import io, os, sys

ROOT = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(ROOT)          # repo root
CORE = os.path.join(ROOT, "shared", "Sim.Core")

def edit(path, pairs):
    p = os.path.join(CORE, path)
    s = io.open(p, encoding="utf-8-sig").read()
    for old, new in pairs:
        if old not in s:
            sys.exit("ANCHOR NOT FOUND in %s:\n%s" % (path, old[:160]))
        if s.count(old) != 1:
            sys.exit("ANCHOR NOT UNIQUE (%d) in %s:\n%s" % (s.count(old), path, old[:160]))
        s = s.replace(old, new)
    io.open(p, "w", encoding="utf-8-sig", newline="\r\n").write(s.replace("\r\n", "\n"))
    print("patched", path)

# ---------------------------------------------------------------- BalanceConfig
CFG_NEW = '''        public int BlockLateralShiftMaxDm { get; set; } = 100;

        // --- Defending: duties, zones and triggers (engine phase 3) ---
        //
        // Football is zonal. Before this the team brain put a marker on every one of the ten
        // opponents, wherever he stood (§1.5), so ten individual duels wandered the pitch and a
        // defending side's shape WAS the attacking side's shape moved five metres back - which is
        // exactly what the measurement read (defending 38.8 x 47.8 m against attacking 42.0 x
        // 48.3). A man now gets ONE duty per brain tick: he goes to the ball, he covers the man
        // who does, he picks up an opponent who is genuinely dangerous, or he holds his place in
        // the block. The last of those is what nearly everybody does, and it is the branch of the
        // movement that used to run 0.0% of the time.

        /// <summary>
        /// How deep into our own half an opponent has to be before anybody leaves the block to
        /// pick him up, in decimetres from our own goal line. Beyond it he is somebody's zone,
        /// not somebody's man.
        /// </summary>
        public int MarkOwnThirdDepthDm { get; set; } = 350;

        /// <summary>
        /// How far past the back line counts as running in behind. A man who has got there is
        /// marked wherever he is: it is the one thing the line cannot deal with by holding.
        /// </summary>
        public int MarkBehindLineDm { get; set; } = 40;

        /// <summary>Most men who may leave their zone to take an opponent at the same time.</summary>
        public int MaxMarkers { get; set; } = 4;

        /// <summary>
        /// Where the covering man stands - goal-side of the ball, a few metres off it - and how
        /// far he will travel to get there. A single presser is walked around; a presser with
        /// cover behind him is a press.
        /// </summary>
        public int CoverDistanceDm { get; set; } = 95;
        public int CoverMaxRangeDm { get; set; } = 320;

        /// <summary>
        /// How far up the pitch a side will chase the man on the ball, in decimetres from its own
        /// goal, per Pressing instruction (low / medium / high). This is the trigger ZONE, and it
        /// is what the instruction has always meant: a low block lets the centre-back have it and
        /// keeps its shape, a high press goes and gets him.
        /// </summary>
        public int[] PressTriggerDepthDm { get; set; } = { 350, 620, 1050 };

        /// <summary>
        /// The situations that switch the press on outside its zone. A ball played backwards is
        /// the moment a side steps up, and a man receiving with a touchline behind him has half
        /// the options he would have in the middle. The third real trigger - a dirty touch - needs
        /// execution error to exist at all, which is phase 4.
        /// </summary>
        public int PressBackPassPercent { get; set; } = 150;
        public int PressWideReceptionPercent { get; set; } = 120;
        public int PressWideThresholdDm { get; set; } = 200;
        public int BackPassMinDm { get; set; } = 60;
        public int PressBackPassMs { get; set; } = 3000;
        public int PressBackPassTicks => TicksOfMs(PressBackPassMs);

        /// <summary>
        /// Bodies. Separation used to push team-mates apart and never opponents, so a marker stood
        /// literally on top of his man (§1.5). The radius is deliberately shorter than the
        /// distance a presser stands off the ball, so keeping men out of each other cannot stop
        /// anybody from making a challenge.
        /// </summary>
        public int OpponentSeparationRadiusDm { get; set; } = 24;
        public int OpponentSeparationStrengthPercent { get; set; } = 70;

        // --- Goalkeeper ---'''

edit("Config/BalanceConfig.cs", [(
    '''        public int BlockLateralShiftMaxDm { get; set; } = 100;

        // --- Goalkeeper ---''', CFG_NEW)])

# ---------------------------------------------------------------- MovementTactics
edit("Match/Movement/MovementTactics.cs", [
    ('''        /// <summary>How far from his position a player will go to press the ball, in units.</summary>
        public readonly int PressReachU;''',
     '''        /// <summary>How far from his position a player will go to press the ball, in units.</summary>
        public readonly int PressReachU;

        /// <summary>
        /// How far up the pitch the side will chase the man on the ball at all, in decimetres
        /// from its own goal (engine phase 3). Past it a low block simply keeps its shape.
        /// </summary>
        public readonly int PressTriggerDepthDm;'''),
    ('''        private MovementTactics(
            int linePushDm, int widthPercent, int pressReachU,
            int supporters, int holdMin, int holdMax, int forwardBias)
        {
            LinePushDm = linePushDm;
            WidthPercent = widthPercent;
            PressReachU = pressReachU;''',
     '''        private MovementTactics(
            int linePushDm, int widthPercent, int pressReachU, int pressTriggerDepthDm,
            int supporters, int holdMin, int holdMax, int forwardBias)
        {
            LinePushDm = linePushDm;
            WidthPercent = widthPercent;
            PressReachU = pressReachU;
            PressTriggerDepthDm = pressTriggerDepthDm;'''),
    ('''                pressReachU: U.Units(Pick(cfg.PressReachDm, pressing)),''',
     '''                pressReachU: U.Units(Pick(cfg.PressReachDm, pressing)),
                pressTriggerDepthDm: Pick(cfg.PressTriggerDepthDm, pressing),'''),
])

# ---------------------------------------------------------------- MatchEngine
edit("Match/MatchEngine.cs", [("Version = 5", "Version = 6")])

# ---------------------------------------------------------------- MatchSimulator
SIM = "Match/Movement/MatchSimulator.cs"

FIELDS_OLD = '''        private int[] _hold = System.Array.Empty<int>();
        private int[] _mark = System.Array.Empty<int>();'''
FIELDS_NEW = '''        private int[] _hold = System.Array.Empty<int>();
        private int[] _mark = System.Array.Empty<int>();

        // One duty per man per brain tick, for the side without the ball (engine phase 3).
        // Going to the ball is decided every tick and is not stored here; these are the three
        // standing jobs: cover the presser, pick a man up, or hold your place in the block.
        private const int DutyZone = 0;
        private const int DutyCover = 1;
        private const int DutyMark = 2;
        private int[] _duty = System.Array.Empty<int>();

        /// <summary>Until when a ball played backwards keeps the opposing press switched on.</summary>
        private readonly int[] _pressUntil = new int[SideCount];'''

ALLOC_OLD = '''            _mark = new int[total];
'''
ALLOC_NEW = '''            _mark = new int[total];
            _duty = new int[total];
            _pressUntil[0] = -1;
            _pressUntil[1] = -1;
'''

SEPU_OLD = '''            _separationU = U.Units(_cfg.SeparationRadiusDm);
            _separationSq = _separationU * _separationU;'''
SEPU_NEW = '''            _separationU = U.Units(_cfg.SeparationRadiusDm);
            _separationSq = _separationU * _separationU;
            _foeSeparationU = U.Units(_cfg.OpponentSeparationRadiusDm);
            if (_foeSeparationU < 1) _foeSeparationU = 1;
            _foeSeparationSq = (long)_foeSeparationU * _foeSeparationU;'''

DECL_OLD = '''        private int _controlU, _kickU, _separationU, _interceptU, _arrivalU, _dribbleReportU;'''
DECL_NEW = '''        private int _controlU, _kickU, _separationU, _interceptU, _arrivalU, _dribbleReportU;
        private int _foeSeparationU;
        private long _foeSeparationSq;'''

BRAIN_OLD = '''                if (_attacking[side])
                {
                    if (rethink) UpdateSupport(tick, side);
                }
                else if (rethink)
                {
                    AssignMarks(side);
                }

                // The shape, once for the tick. Everything that asks where a man belongs reads
                // it; nothing recomputes it.
                UpdateBlock(side);'''
BRAIN_NEW = '''                // The shape, once for the tick, and FIRST: everything that asks where a man
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
                }'''

DUTIES_NEW = '''        /// <summary>
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
                    if (_keeper[ok] || taken[j]) continue;

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
                    if (_keeper[k] || _duty[k] != DutyZone || _chaser[side] == i) continue;
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
                if (_keeper[k] || _duty[k] != DutyZone || _chaser[side] == i) continue;
                long d = U.DistanceSq(_px[k], _py[k], cx, cy);
                if (d < coverRange) { coverRange = d; cover = i; }
            }

            if (cover >= 0) _duty[side * _n + cover] = DutyCover;
        }

'''

MOVE_PRESS_OLD = '''            else if (!_attacking[side] && Pressing(side, slot, out pressHomeX, out pressHomeY))'''
MOVE_PRESS_NEW = '''            else if (!_attacking[side] && Pressing(tick, side, slot, out pressHomeX, out pressHomeY))'''

MOVE_MARK_OLD = '''            else if (!_attacking[side] && _mark[k] >= 0)
            {
                MarkSpot(side, k, out tx, out ty);
            }'''
MOVE_MARK_NEW = '''            else if (!_attacking[side] && _duty[k] == DutyCover)
            {
                CoverSpot(side, out tx, out ty);
            }
            else if (!_attacking[side] && _duty[k] == DutyMark && _mark[k] >= 0)
            {
                MarkSpot(side, k, out tx, out ty);
            }'''

PRESS_OLD = '''        private bool Pressing(int side, int slot, out int homeX, out int homeY)
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
            if (_urgent[side]) reach = reach * _cfg.ChancePressPercent / 100;
            return U.DistanceSq(homeX, homeY, _ball.X, _ball.Y) <= reach * reach;
        }'''
PRESS_NEW = '''        private bool Pressing(int tick, int side, int slot, out int homeX, out int homeY)
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
        }'''

SEP_OLD = '''            wx += pushX * _cfg.SeparationStrengthPercent / 100;
            wy += pushY * _cfg.SeparationStrengthPercent / 100;
            U.Cap(ref wx, ref wy, top);'''
SEP_NEW = '''            // And off the opposition (engine phase 3). Separation used to push team-mates apart
            // and never opponents, which is why a marker stood literally on top of his man and
            // the video showed red/blue pairs moving as one body (§1.5). The radius is shorter
            // than the distance a presser stands off the ball, so this keeps men out of each
            // other without ever stopping a challenge.
            int foePushX = 0, foePushY = 0;
            int foes = (1 - side) * _n;
            for (int j = 0; j < _n; j++)
            {
                int other = foes + j;
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
            U.Cap(ref wx, ref wy, top);'''

BACKPASS_OLD = '''            _ball.Kick(side, slot, bestX - _px[k], bestY - _py[k], passForce);
            Release(tick, side, slot);'''
BACKPASS_NEW = '''            _ball.Kick(side, slot, bestX - _px[k], bestY - _py[k], passForce);
            Release(tick, side, slot);

            // A ball played backwards is the moment the other side steps up (engine phase 3).
            if (dir * (bestX - _px[k]) < -U.Units(_cfg.BackPassMinDm))
                _pressUntil[1 - side] = tick + _cfg.PressBackPassTicks;'''

import re
p = os.path.join(CORE, SIM)
s = io.open(p, encoding="utf-8-sig").read()

# AssignMarks is replaced wholesale by AssignDuties.
start = s.index("        /// <summary>\n        /// Who picks up whom.")
end = s.index("        // ------------------------------------------------------------------ acting")
s = s[:start] + DUTIES_NEW + s[end:]
io.open(p, "w", encoding="utf-8-sig", newline="\r\n").write(s.replace("\r\n", "\n"))

edit(SIM, [
    (FIELDS_OLD, FIELDS_NEW),
    (ALLOC_OLD, ALLOC_NEW),
    (DECL_OLD, DECL_NEW),
    (SEPU_OLD, SEPU_NEW),
    (BRAIN_OLD, BRAIN_NEW),
    (MOVE_PRESS_OLD, MOVE_PRESS_NEW),
    (MOVE_MARK_OLD, MOVE_MARK_NEW),
    (PRESS_OLD, PRESS_NEW),
    (SEP_OLD, SEP_NEW),
    (BACKPASS_OLD, BACKPASS_NEW),
])
print("phase 3 applied")

# ---------------------------------------------------------------- the block, defending
# The measurement asked for these two: with everybody finally holding a zone, the block's own
# nominal shape IS what gets measured, and it was the attacking one. A side without the ball
# squeezes - that is what "compact" means - and it defends narrower than it attacks.
edit("Config/BalanceConfig.cs", [
    ('''        public int AttackLineSpacingPercent { get; set; } = 120;''',
     '''        public int AttackLineSpacingPercent { get; set; } = 120;

        /// <summary>
        /// And how much of it is left when the side is defending (engine phase 3). Until the
        /// duties existed this could not be seen: every man was following an opponent, so what
        /// the harness measured as "the defending block" was the attacking block moved five
        /// metres back. With everybody holding a zone the block's own depth IS what gets
        /// measured, and a side without the ball squeezes - thirty metres from the back line to
        /// the strikers, not forty-five.
        /// </summary>
        public int DefendLineSpacingPercent { get; set; } = 55;'''),
    ('''        public int DefendWidthPercent { get; set; } = 66;''',
     '''        public int DefendWidthPercent { get; set; } = 60;'''),
])

edit("Match/Movement/MatchSimulator.cs", [
    ('''            int spacing = kickoff
                ? _cfg.LineSpacingDm
                : _cfg.LineSpacingDm * (100 + (_cfg.AttackLineSpacingPercent - 100) * e / 1000) / 100;''',
     '''            int spacing = kickoff
                ? _cfg.LineSpacingDm
                : _cfg.LineSpacingDm
                  * (_cfg.DefendLineSpacingPercent
                     + (_cfg.AttackLineSpacingPercent - _cfg.DefendLineSpacingPercent) * e / 1000)
                  / 100;'''),
    ('''            // A man on the back line HOLDS THE LINE (engine phase 2). He picks his opponent up''',
     '''            // A man who is NOT on the back line does not drop in behind it (engine phase 3).
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

            // A man on the back line HOLDS THE LINE (engine phase 2). He picks his opponent up'''),
    ('''            if (_lineRank[k] != 0) return;

            int lineU = _blockLineU[side];
            int lineDepth = home ? lineU : U.LengthU - lineU;''',
     '''            int lineU = ownLineU;
            int lineDepth = home ? lineU : U.LengthU - lineU;'''),
])
print("block tuning applied")

# The scratch array's doc comment named the method that no longer exists (CS1574 under
# TreatWarningsAsErrors).
edit(SIM, [('Scratch for <see cref="AssignMarks"/>', 'Scratch for <see cref="AssignDuties"/>')])
print("done")

# ---------------------------------------------------------------- getting back
# And the third thing the measurement asked for. With the duties in place the block's own shape
# is 26 m deep, but the ten men measured 42.6: the deepest stood 4.5 m behind the deepest spot
# and the most advanced 11.9 m in front of the front one. The block was not the wrong shape - it
# was a squad still strung out from attacking, jogging home at 2.6 m/s while the ball went the
# other way. A man caught up the pitch when possession turns over RUNS back.
edit("Config/BalanceConfig.cs", [
    ('''        public int MarkDistanceDm { get; set; } = 58;''',
     '''        public int MarkDistanceDm { get; set; } = 32;'''),
    ('''        public int OpponentSeparationRadiusDm { get; set; } = 24;''',
     '''        /// <summary>
        /// How far out of his defensive place a man has to be caught before he stops jogging
        /// home and RUNS, in decimetres. Off the ball a footballer jogs; a recovery run is the
        /// one thing off the ball he sprints for, and without it the block took a dozen seconds
        /// to re-form after every turnover - which is measured as a block that is not compact.
        /// </summary>
        public int RecoveryRunDm { get; set; } = 180;

        public int OpponentSeparationRadiusDm { get; set; } = 24;'''),
])

edit(SIM, [
    ('''            _foeSeparationU = U.Units(_cfg.OpponentSeparationRadiusDm);''',
     '''            _recoveryU = U.Units(_cfg.RecoveryRunDm);
            if (_recoveryU < 1) _recoveryU = 1;
            _foeSeparationU = U.Units(_cfg.OpponentSeparationRadiusDm);'''),
    ('''        private int _foeSeparationU;''',
     '''        private int _foeSeparationU;
        private int _recoveryU;'''),
    ('''            Steer(k, U.ClampX(tx), U.ClampY(ty), sprint);
        }''',
     '''            // The recovery run (engine phase 3). A man who has been caught up the pitch does
            // not jog home while the ball goes the other way, and the difference is not cosmetic:
            // at a jog the block takes a dozen seconds to re-form, which is a dozen seconds in
            // which the side defending is a side still strung out from attacking.
            tx = U.ClampX(tx);
            ty = U.ClampY(ty);
            if (!sprint && !_attacking[side] && !_keeper[k]
                && U.DistanceSq(_px[k], _py[k], tx, ty) > (long)_recoveryU * _recoveryU)
                sprint = true;

            Steer(k, tx, ty, sprint);
        }'''),
])
print("recovery run applied")

# ---------------------------------------------------------------- the last three, all measured
# (1) A block COLLAPSES faster than it expands. Phase 2 gave the shape inertia so it would stop
#     flapping sideways on every turnover; symmetric, that inertia meant a side that had just lost
#     the ball was still carrying the attacking shape's width and spacing for four seconds, which
#     with a turnover every few seconds is most of the time it spends defending. A real side drops
#     into its block in a second and a half and takes its time coming out.
# (2) The COVER comes from midfield, not from the back four. A centre-back who steps out to cover
#     the presser is a centre-back out of the line, and the line is measured.
# (3) A defender on the back line does not leave it for a man who is a metre or two behind it —
#     only for one who is genuinely through.
edit("Config/BalanceConfig.cs", [
    ('''        public int ShapeTransitionTicks => TicksOfMs(ShapeTransitionMs);''',
     '''        public int ShapeTransitionTicks => TicksOfMs(ShapeTransitionMs);

        /// <summary>
        /// And how long it takes to CLOSE DOWN into the defensive one (engine phase 3). Not the
        /// same number: a side drops into its block in a second and a half and comes out of it in
        /// four. Symmetric, the inertia phase 2 added meant a side that had just lost the ball
        /// kept the attacking shape's width and line spacing for four seconds — and with a
        /// turnover every few seconds that was most of the time it spent defending, which is why
        /// the harness kept reading the defending block as the attacking one.
        /// </summary>
        public int ShapeCollapseMs { get; set; } = 1500;
        public int ShapeCollapseTicks => TicksOfMs(ShapeCollapseMs);'''),
])

edit(SIM, [
    ('''            int e = _expansion[side];
            if (_attacking[side]) { e += step; if (e > 1000) e = 1000; }
            else { e -= step; if (e < 0) e = 0; }''',
     '''            int collapse = 1000 / _cfg.ShapeCollapseTicks;
            if (collapse < 1) collapse = 1;
            int e = _expansion[side];
            if (_attacking[side]) { e += step; if (e > 1000) e = 1000; }
            else { e -= collapse; if (e < 0) e = 0; }'''),
    ('''                if (_keeper[k] || _duty[k] != DutyZone || _chaser[side] == i) continue;
                long d = U.DistanceSq(_px[k], _py[k], cx, cy);''',
     '''                if (_keeper[k] || _duty[k] != DutyZone || _chaser[side] == i) continue;
                if (_lineRank[k] == 0) continue;   // a centre-back covering is a centre-back out of the line
                long d = U.DistanceSq(_px[k], _py[k], cx, cy);'''),
    ('''            int manDepth = home ? _px[ok] : U.LengthU - _px[ok];
            if (manDepth >= lineDepth) x = lineU;''',
     '''            int manDepth = home ? _px[ok] : U.LengthU - _px[ok];
            if (manDepth >= lineDepth - U.Units(_cfg.MarkBehindLineDm)) x = lineU;'''),
])
print("collapse, cover and line discipline applied")
