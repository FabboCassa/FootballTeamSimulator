using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Where a man goes without the ball under the V11 brain (watchable-match spec, R2). Pure and
    /// draw-free, in decimetres on the pitch, so every decision can be scripted in a test:
    ///
    /// - <see cref="PhaseSpot"/>: his phase target spot — the formation shape (his line, his place
    ///   across it) placed by the phase his side is in and by the instructions: mentality and, out
    ///   of possession, the Pressing instruction set the line height; width spreads the shape.
    /// - <see cref="Onside"/>: with the ball, nobody waits in an offside position.
    /// - <see cref="RunInBehind"/>: a forward, onside, runs into the space behind the line when a
    ///   pass could reach it.
    /// - <see cref="Overlap"/>: a full-back goes past the man on the ball, on the outside, when the
    ///   width and mentality instructions ask for it.
    ///
    /// Depths are measured from the side's own goal line in its attacking direction, so one
    /// formula serves both sides; the away side is the home side mirrored.
    /// </summary>
    public sealed class V11Positioning
    {
        private readonly MatchBalance _cfg;

        public V11Positioning(MatchBalance cfg)
        {
            _cfg = cfg;
        }

        public void PhaseSpot(
            V11Slot man, bool home, TeamPhase phase, bool inPossession, TacticInstructions instructions,
            int ballXDm, int ballYDm, out int xDm, out int yDm) =>
            PhaseSpot(man, home, phase, inPossession ? 1000 : 0, instructions, ballXDm, ballYDm, out xDm, out yDm);

        /// <summary>
        /// The spot part-way between the side's shape without the ball (0) and with it (1000). A
        /// shape cannot teleport: a side that has just lost the ball takes seconds to drop into
        /// its block, and a target that jumped there at once would strand every forward 30 m from
        /// it after each turnover.
        /// </summary>
        public void PhaseSpot(
            V11Slot man, bool home, TeamPhase phase, int expansionPermille, TacticInstructions instructions,
            int ballXDm, int ballYDm, out int xDm, out int yDm)
        {
            int e = MovementGeometry.Clamp(expansionPermille, 0, 1000);
            if (e == 0 || e == 1000)
            {
                Spot(man, home, phase, e == 1000, instructions, ballXDm, ballYDm, out xDm, out yDm);
                return;
            }

            Spot(man, home, phase, false, instructions, ballXDm, ballYDm, out int outX, out int outY);
            Spot(man, home, phase, true, instructions, ballXDm, ballYDm, out int inX, out int inY);
            xDm = outX + (inX - outX) * e / 1000;
            yDm = outY + (inY - outY) * e / 1000;
        }

        private void Spot(
            V11Slot man, bool home, TeamPhase phase, bool inPossession, TacticInstructions instructions,
            int ballXDm, int ballYDm, out int xDm, out int yDm)
        {
            if (man.Role == PositionRole.Goalkeeper)
            {
                xDm = home ? _cfg.KeeperDepthDm : Pitch.LengthDm - _cfg.KeeperDepthDm;
                yDm = Pitch.CenterY;
                return;
            }

            int mentality = (int)instructions.Mentality;
            int ballDepth = Depth(home, ballXDm);

            int line = _cfg.BackLineMinDepthDm
                       + (ballDepth - _cfg.BackLineBallLagDm) * _cfg.BackLineBallFollowPercent / 100
                       + MovementTactics.Pick(_cfg.MentalityLinePushDm, mentality);
            line += inPossession
                ? MovementTactics.Pick(_cfg.V11LineShiftInPossessionDm, (int)phase)
                : MovementTactics.Pick(_cfg.V11LineShiftOutOfPossessionDm, (int)phase)
                  + MovementTactics.Pick(_cfg.V11PressingLinePushDm, (int)instructions.Pressing);
            line = MovementGeometry.Clamp(line, _cfg.BackLineMinDepthDm, _cfg.BackLineMaxDepthDm);

            int spacing = _cfg.LineSpacingDm
                          * (inPossession ? _cfg.V11AttackLineSpacingPercent : _cfg.V11DefendLineSpacingPercent) / 100;

            // The front line stands no nearer the goal than mentality allows; the shape compresses.
            int reach = Pitch.LengthDm
                        - _cfg.FrontLineGoalGapDm * MovementTactics.Percent(_cfg.MentalityFrontLineGapPercent, mentality) / 100;
            int lines = man.LineCount;
            if (lines > 1 && line + (lines - 1) * spacing > reach)
                spacing = reach > line ? (reach - line) / (lines - 1) : 0;

            int depth = line + man.LineRank * spacing + man.OffsetXDm;

            int widthPercent = MovementTactics.Percent(_cfg.WidthSpreadPercent, (int)instructions.Width)
                               * (inPossession ? _cfg.V11AttackWidthPercent : _cfg.V11DefendWidthPercent) / 100;
            int shift = MovementGeometry.Clamp(
                ballYDm - Pitch.CenterY, -_cfg.BlockLateralShiftMaxDm, _cfg.BlockLateralShiftMaxDm);
            int spread = (man.BaseYPermille - 500) * Pitch.WidthDm / 1000;

            xDm = Pitch.ClampX(home ? depth : Pitch.LengthDm - depth);
            yDm = Pitch.ClampY(Pitch.CenterY + shift + MovementGeometry.Direction(home) * spread * widthPercent / 100);
        }

        /// <summary>A spot beyond the offside line is pulled back to just onside of it.</summary>
        public int Onside(bool home, int xDm, int offsideLineXDm)
        {
            int limit = Depth(home, offsideLineXDm) - _cfg.V11OnsideHoldDm;
            return Depth(home, xDm) > limit ? Undepth(home, limit) : xDm;
        }

        /// <summary>
        /// Whether a forward, onside, starts a run in behind: there is room between the line and
        /// the goal, nobody is already in it, and no opponent stands on the pass from the ball to
        /// it. The opponents are every man of the other side still on the pitch.
        /// </summary>
        public bool RunInBehind(
            bool home, int forwardXDm, int forwardYDm, int carrierXDm, int carrierYDm, int offsideLineXDm,
            int[] opponentXDm, int[] opponentYDm, int opponents, out int targetXDm, out int targetYDm)
        {
            targetXDm = 0;
            targetYDm = 0;

            int line = Depth(home, offsideLineXDm);
            int forward = Depth(home, forwardXDm);
            if (forward > line || forward < line - _cfg.V11RunStartBandDm) return false;
            if (Depth(home, carrierXDm) >= line) return false;

            int end = line + _cfg.V11RunDepthDm;
            int last = Pitch.LengthDm - _cfg.V11RunGoalGapDm;
            if (end > last) end = last;
            if (end - line < _cfg.V11RunMinSpaceDm) return false;

            int x = Undepth(home, end);
            int y = MovementGeometry.Clamp(
                forwardYDm, Pitch.CenterY - _cfg.V11RunChannelDm, Pitch.CenterY + _cfg.V11RunChannelDm);
            if (U.DistanceSq(carrierXDm, carrierYDm, x, y) > (long)_cfg.V11RunPassMaxDm * _cfg.V11RunPassMaxDm)
                return false;

            long space = (long)_cfg.V11RunSpaceRadiusDm * _cfg.V11RunSpaceRadiusDm;
            long lane = (long)_cfg.V11RunLaneHalfWidthDm * _cfg.V11RunLaneHalfWidthDm;
            for (int j = 0; j < opponents; j++)
            {
                int ox = opponentXDm[j], oy = opponentYDm[j];
                if (U.DistanceSq(ox, oy, x, y) < space) return false;
                if (U.DistanceSqToSegment(ox, oy, carrierXDm, carrierYDm, x, y) < lane) return false;
            }

            targetXDm = x;
            targetYDm = y;
            return true;
        }

        /// <summary>Whether a side's width and mentality send its full-backs on the overlap at all.</summary>
        public bool OverlapAllowed(TacticInstructions instructions) =>
            MovementTactics.Pick(_cfg.V11OverlapWidthScore, (int)instructions.Width)
            + MovementTactics.Pick(_cfg.V11OverlapMentalityScore, (int)instructions.Mentality)
            >= _cfg.V11OverlapThreshold;

        /// <summary>
        /// Whether a full-back overlaps the man on the ball: his side has it in the progression or
        /// the final third, the instructions allow it, the ball is out on his flank and just ahead
        /// of him (<paramref name="fullBackXDm"/> is where he stands in the shape). The run goes
        /// past the ball, down the outside.
        /// </summary>
        public bool Overlap(
            V11Slot man, bool home, TeamPhase phase, bool inPossession, TacticInstructions instructions,
            int fullBackXDm, int carrierXDm, int carrierYDm, out int targetXDm, out int targetYDm)
        {
            targetXDm = 0;
            targetYDm = 0;

            if (man.Role != PositionRole.FullBack || !inPossession) return false;
            if (phase != TeamPhase.Progression && phase != TeamPhase.FinalThird) return false;
            if (!OverlapAllowed(instructions)) return false;

            int flank = MovementGeometry.Direction(home) * (man.BaseYPermille - 500);
            int ballOff = carrierYDm - Pitch.CenterY;
            bool sameFlank = flank > 0 ? ballOff >= _cfg.V11OverlapFlankMinDm : flank < 0 && ballOff <= -_cfg.V11OverlapFlankMinDm;
            if (!sameFlank) return false;

            int carrier = Depth(home, carrierXDm);
            int own = Depth(home, fullBackXDm);
            if (carrier <= own || carrier - own > _cfg.V11OverlapReachDm) return false;

            int end = carrier + _cfg.V11OverlapAheadDm;
            int last = Pitch.LengthDm - _cfg.V11RunGoalGapDm;
            if (end > last) end = last;

            int touchline = Pitch.CenterY - _cfg.V11OverlapTouchlineGapDm;
            targetXDm = Undepth(home, end);
            targetYDm = Pitch.CenterY + (flank > 0 ? touchline : -touchline);
            return true;
        }

        private static int Depth(bool home, int xDm) => home ? xDm : Pitch.LengthDm - xDm;

        private static int Undepth(bool home, int depth) => home ? depth : Pitch.LengthDm - depth;
    }
}
