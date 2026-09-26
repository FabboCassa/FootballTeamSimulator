namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V10Brain
        {
            /// <summary>
            /// The block, for one side, for both phases — worked out once a tick (engine phase 2).
            ///
            /// A team is a shape with three separate properties: WHERE it is, how DEEP it is and how
            /// WIDE it is. Where it is has exactly one degree of freedom along the pitch, the height
            /// of the back line, because that is the line a coach actually instructs — "hold on the
            /// halfway line", "drop off" — and every other line is spaced forward off it, which is
            /// what makes the back four a line by construction instead of by luck. How deep and how
            /// wide follow from one thing: whether the team has the ball.
            ///
            /// The asymmetry of the shift falls out of measuring the spacing FORWARD from the back
            /// line: losing the ball drops the striker forty-odd metres and moves his centre-backs
            /// fifteen, which is what a team collapsing into its block looks like.
            /// </summary>
            private void UpdateBlock(int side)
            {
                bool home = side == 0;
                int dir = MovementGeometry.Direction(home);
                MovementTactics t = _tactics[side];
                int lines = _lineCount[side];

                // The ball's depth measured from THIS side's own goal, so one formula serves both
                // sides and neither has a mirrored copy of the rule.
                int ballDepth = home ? U.Dm(_ball.X) : Pitch.LengthDm - U.Dm(_ball.X);

                // Toward the attacking shape, or back toward the defensive one, a step at a time.
                int step = 1000 / _cfg.ShapeTransitionTicks;
                if (step < 1) step = 1;
                int collapse = 1000 / _cfg.ShapeCollapseTicks;
                if (collapse < 1) collapse = 1;
                int e = _expansion[side];
                if (_attacking[side]) { e += step; if (e > 1000) e = 1000; }
                else { e -= collapse; if (e < 0) e = 0; }
                _expansion[side] = e;

                // Law 8: a kickoff is taken with both sides in their own half. The shape is squeezed
                // back behind the halfway line until the ball is in play — the taker is on the ball
                // and exempt.
                bool kickoff = _ball.Dead && _ctx.DeadKind == BallActionKind.Kickoff;

                // Where the line holds, as a function of where the ball is — and NOT one for one. A
                // line that followed the ball metre for metre would spend the match chasing a ball
                // being passed up and down the pitch, which is two kilometres a man of running that
                // no defender does. It is anchored near its own goal and pulled up by the ball,
                // which is what a defender's two jobs — protect the goal, squeeze the space in front
                // of it — add up to.
                int line = _cfg.BackLineMinDepthDm
                         + (ballDepth - _cfg.BackLineBallLagDm) * _cfg.BackLineBallFollowPercent / 100
                         + t.LinePushDm
                         + _cfg.PossessionLinePushDm * e / 1000;
                if (_driving[side]) line += _cfg.PitchDriveShiftPermille * Pitch.LengthDm * e / 1000_000;
                line = MovementGeometry.Clamp(line, _cfg.BackLineMinDepthDm, _cfg.BackLineMaxDepthDm);

                int spacing = kickoff
                    ? _cfg.LineSpacingDm
                    : _cfg.LineSpacingDm
                      * (_cfg.DefendLineSpacingPercent
                         + (_cfg.AttackLineSpacingPercent - _cfg.DefendLineSpacingPercent) * e / 1000)
                      / 100;

                // How much room the front line has. Without a ceiling the most advanced line ends up
                // standing on the goal line instead of on the edge of the box.
                //
                // MENTALITY OWNS THAT CEILING (engine phase 8), and it has to, because the push it
                // gives the back line is spent the moment the block hits this cap — which is exactly
                // when the side is attacking and exactly when the instruction is supposed to show.
                // A defensive side keeps its furthest man further off the goal it is attacking; an
                // attacking one lets him stand on the edge of the box. Neutral reads the config
                // value unchanged.
                int reach = kickoff
                    ? Pitch.CenterX - _cfg.KickoffHalfwayGapDm
                    : Pitch.LengthDm - t.FrontLineGapDm;

                if (lines > 1 && line + (lines - 1) * spacing > reach)
                {
                    // Behind the halfway line at a kickoff the whole block moves back; in open play
                    // the line stays where the game put it and the shape compresses in front of it.
                    if (kickoff) line = reach - (lines - 1) * spacing;
                    else spacing = (reach - line) / (lines - 1);
                }

                if (line < 0) line = 0;
                if (spacing < 0) spacing = 0;

                // And the line STEPS. It holds its height, and when the game has genuinely moved it
                // moves as a unit — which is both what a back four does and the difference between
                // ten men covering eleven kilometres in a match and covering thirteen. Filtering the
                // BALL instead of the line does not do it: the ball crosses any deadband every
                // second or two, so the line ends up creeping after it a metre at a time.
                int held = _blockLine[side];
                int drift = line - held;
                if (drift < 0) drift = -drift;

                // A KICKOFF re-forms the shape rather than holding it. The deadband is what stops the
                // line creeping after every sideways pass, but at a kickoff the block has just been
                // squeezed behind the halfway line by the cap above, and holding the old line six metres
                // deeper leaves the front two men standing in the other side's half — which is against
                // Law 8, and was measured doing exactly that (engine phase 5).
                if (!_blockSet[side] || drift > _cfg.BackLineHoldDm || kickoff)
                {
                    held = line;
                    _blockSet[side] = true;
                }

                _blockLine[side] = held;
                line = held;

                _blockLineU[side] = U.Units(home ? line : Pitch.LengthDm - line);
                _blockSpacingU[side] = dir * U.Units(spacing);
                _blockWidthPercent[side] = t.WidthPercent
                    * (_cfg.DefendWidthPercent
                       + (_cfg.AttackWidthPercent - _cfg.DefendWidthPercent) * e / 1000) / 100;

                // The shape slides toward the ball's side of the pitch, as one piece and no further
                // than the cap. What this replaced was a per-player lerp toward the ball's Y: the man
                // furthest from the ball moved MORE than the man nearest it, so the team did not
                // shift across, it collapsed into the ball's channel (§1.4).
                int shift = U.Dm(_ball.Y) - Pitch.CenterY;
                int max = _cfg.BlockLateralShiftMaxDm;
                if (shift < -max) shift = -max;
                else if (shift > max) shift = max;
                _blockY[side] = U.Units(Pitch.CenterY + shift);
            }

            /// <summary>
            /// Where a man stands when the game is asking nothing else of him: his place IN THE
            /// BLOCK. Not a point on the pitch he owns — a point in a shape, which is why all this
            /// does is take the block's line and its width and add his own offset within them.
            /// </summary>
            private void HomeSpot(int side, int slot, out int x, out int y)
            {
                int k = side * _n + slot;
                if (_keeper[k])
                {
                    KeeperSpot(side, out x, out y);
                    return;
                }

                int dir = MovementGeometry.Direction(side == 0);
                int px = _blockLineU[side] + _lineRank[k] * _blockSpacingU[side] + dir * U.Units(_offsetXDm[k]);

                // His distance from the middle is a SHARE of the block's, so the shape squeezes and
                // stretches as one piece instead of eleven men each widening on their own.
                int spreadDm = (_baseY[k] - 500) * Pitch.WidthDm / 1000;
                int py = _blockY[side] + dir * U.Units(spreadDm) * _blockWidthPercent[side] / 100;

                x = U.ClampX(px);
                y = _ctx.Inside(py);
            }
        }
    }
}
