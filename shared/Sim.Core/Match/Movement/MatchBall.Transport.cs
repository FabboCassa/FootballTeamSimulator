using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// A restart's ball is never placed (R5 of believable-match). The referee's spot is where the
    /// model reads the ball from the whistle (the taker walks to it and both shapes set up around
    /// it), but the picture shows the ball running on from where play stopped until it is stopped
    /// (by a player, or by the boards beyond a line), then rolled back to the spot at no more than
    /// the transport speed; the restart is not taken before it arrives. A dead ball is nobody's to
    /// play, so all of this moves only the picture and the wait (<see cref="TransportTicks"/>).
    /// </summary>
    internal sealed partial class MatchBall
    {
        /// <summary>Where the stream draws the ball: X, Y, except while a dead ball is on its way to its spot.</summary>
        public int ShownX, ShownY;

        private int _transportStep = 1, _runOnTicks, _boardsU;

        // The last step shown in play; the run-on still to show: its speed at the whistle, and the ticks of it left.
        private int _lastStepX, _lastStepY, _runVx, _runVy, _runLeft;

        private void InitTransport(MatchBalance cfg)
        {
            int step = U.PerTick(cfg.RestartTransportDmPerSecond, cfg);
            _transportStep = step < 1 ? 1 : step;
            _runOnTicks = cfg.TicksOfMs(cfg.DeadBallRunOnMs);
            _boardsU = U.Units(cfg.RunOffDepthDm);
        }

        /// <summary>
        /// The ball goes down on the restart spot, and the picture takes it there. A loose ball first
        /// finishes this tick's flight (to the line it went out over), one at a man's feet stays
        /// where it was shown; then it runs on a metre or so, knocked on or stopped, the way it was
        /// going — or, standing still, the way its last toucher was playing — before it is rolled
        /// to the spot. A dead one carries on from where it is shown. From the whistle the picture
        /// never moves the ball faster than a transport, and the run-on takes it off the spot before
        /// it comes back, so no restart's ball reaches its spot at the pace of play.
        /// </summary>
        public void SendTo(int x, int y)
        {
            if (!Dead)
            {
                int vx = X - ShownX, vy = Y - ShownY;
                if (Free)
                {
                    ShowWhereItIs();
                    ShownX = MovementGeometry.Clamp(ShownX, -_boardsU, U.LengthU + _boardsU);
                    ShownY = U.ClampY(ShownY);
                }

                if (vx == 0 && vy == 0)
                {
                    vx = _lastStepX;
                    vy = _lastStepY;
                }

                if (vx == 0 && vy == 0) vx = MovementGeometry.Direction(LastTouchSide != 1);
                U.Scaled(vx, vy, _transportStep, out _runVx, out _runVy);
                _runLeft = _runOnTicks;
            }

            int shownX = ShownX, shownY = ShownY;
            Place(x, y);
            ShownX = shownX;
            ShownY = shownY;
        }

        /// <summary>The ball is shown where it is: the opening kickoff, before any frame is written.</summary>
        public void ShowWhereItIs()
        {
            ShownX = X;
            ShownY = Y;
            _runLeft = 0;
        }

        /// <summary>Ticks the shown ball still needs to stop and reach the spot.</summary>
        public int TransportTicks
        {
            get
            {
                int x = ShownX, y = ShownY;
                for (int left = _runLeft; left > 0; left--) RunOn(ref x, ref y, left);
                return _runLeft + (U.Distance(x, y, X, Y) + _transportStep - 1) / _transportStep;
            }
        }

        /// <summary>
        /// One tick of the picture, after everything else in the tick: a dead ball on its way runs
        /// on, then rolls a step toward its spot; any other ball is shown where it is.
        /// </summary>
        public void StepShown()
        {
            if (!Dead || _behind)
            {
                _lastStepX = X - ShownX;
                _lastStepY = Y - ShownY;
                ShowWhereItIs();
                return;
            }

            if (_runLeft > 0)
            {
                RunOn(ref ShownX, ref ShownY, _runLeft);
                _runLeft--;
                return;
            }

            int dx = X - ShownX, dy = Y - ShownY;
            int gap = U.Length(dx, dy);
            if (gap <= _transportStep)
            {
                ShownX = X;
                ShownY = Y;
                return;
            }

            ShownX += (int)((long)dx * _transportStep / gap);
            ShownY += (int)((long)dy * _transportStep / gap);
        }

        /// <summary>
        /// One tick of the run-on with <paramref name="left"/> ticks of it to go: the pace falls off
        /// evenly to nothing. The boards a run-off beyond a goal line stop it, and a touchline: the
        /// picture has no ground beyond it, so a throw-in's ball rolls along it.
        /// </summary>
        private void RunOn(ref int x, ref int y, int left)
        {
            x += (int)((long)_runVx * left / (_runOnTicks + 1));
            y += (int)((long)_runVy * left / (_runOnTicks + 1));
            x = MovementGeometry.Clamp(x, -_boardsU, U.LengthU + _boardsU);
            y = U.ClampY(y);
        }
    }
}
