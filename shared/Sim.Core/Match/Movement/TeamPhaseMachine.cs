using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>The phase of play a side is in (watchable-match spec, R1).</summary>
    public enum TeamPhase
    {
        BuildUp = 0,
        Progression = 1,
        FinalThird = 2,
        AttackTransition = 3,
        DefenceTransition = 4,
        SetPiece = 5
    }

    /// <summary>
    /// Which phase each side is in, read once a tick off the ball alone: where it is, who has it,
    /// and whether it is dead. Pure and draw-free, so it can be scripted in a test and it cannot
    /// move a match's random sequence.
    ///
    /// - A dead ball is a set piece for both sides, and the side awarded the restart has the ball.
    /// - Winning the ball back live starts a transition: the winner attacks, the loser defends,
    ///   until the window runs out, the ball goes dead or it changes hands again.
    /// - Otherwise both sides are in the phase the attack is in, read in the attacker's direction:
    ///   the side in possession plays it and the other defends it (presses the build-up, blocks
    ///   the progression, protects the box). Possession tells the brain which of the two it is.
    ///
    /// A loose ball (a pass or a shot in flight) belongs to whoever had it last.
    /// </summary>
    public sealed class TeamPhaseMachine
    {
        public const int PhaseCount = 6;

        private const int SideCount = 2;

        private readonly int _buildUpEndDm;
        private readonly int _finalThirdStartDm;
        private readonly int _transitionTicks;
        private readonly TeamPhase[] _phase = new TeamPhase[SideCount];

        private int _possession;
        private int _transitionUntil;

        public TeamPhaseMachine(MatchBalance cfg)
        {
            _buildUpEndDm = Pitch.LengthDm * cfg.PhaseBuildUpEndPermille / 1000;
            _finalThirdStartDm = Pitch.LengthDm * cfg.PhaseFinalThirdStartPermille / 1000;
            _transitionTicks = cfg.PhaseTransitionTicks;
            Reset();
        }

        /// <summary>A new match: the kickoff is a dead ball and nobody has had the ball yet.</summary>
        public void Reset()
        {
            _possession = -1;
            _transitionUntil = -1;
            _phase[0] = TeamPhase.SetPiece;
            _phase[1] = TeamPhase.SetPiece;
        }

        public TeamPhase PhaseOf(int side) => _phase[side];

        /// <param name="ballXDm">Ball along the length; home attacks toward <see cref="Pitch.LengthDm"/>.</param>
        /// <param name="ownerSide">The side with the ball at its feet, or -1 if it is loose.</param>
        /// <param name="restartSide">While the ball is dead, the side that takes the restart.</param>
        public void Update(int tick, int ballXDm, int ownerSide, bool deadBall, int restartSide)
        {
            if (deadBall)
            {
                if (restartSide >= 0) _possession = restartSide;
                _transitionUntil = -1;
                _phase[0] = TeamPhase.SetPiece;
                _phase[1] = TeamPhase.SetPiece;
                return;
            }

            if (ownerSide >= 0 && ownerSide != _possession)
            {
                if (_possession >= 0) _transitionUntil = tick + _transitionTicks;
                _possession = ownerSide;
            }

            if (_possession < 0) return;

            if (tick < _transitionUntil)
            {
                _phase[_possession] = TeamPhase.AttackTransition;
                _phase[1 - _possession] = TeamPhase.DefenceTransition;
                return;
            }

            TeamPhase play = PhaseOfPlay(_possession == 0 ? ballXDm : Pitch.LengthDm - ballXDm);
            _phase[0] = play;
            _phase[1] = play;
        }

        /// <summary>The attack's phase from how far the ball is from the attacker's own goal line.</summary>
        private TeamPhase PhaseOfPlay(int depthDm)
        {
            if (depthDm < _buildUpEndDm) return TeamPhase.BuildUp;
            return depthDm >= _finalThirdStartDm ? TeamPhase.FinalThird : TeamPhase.Progression;
        }
    }
}
