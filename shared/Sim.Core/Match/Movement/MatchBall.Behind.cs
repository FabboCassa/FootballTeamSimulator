namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// A dead ball over a goal line (R5 of believable-match): it runs on into the net, or towards
    /// the boards, instead of being lifted onto the next spot, and it lies where it stopped. The
    /// net, the boards or the side netting kill it dead; one that crossed the line at a crawl
    /// rolls on to <see cref="_restDepth"/>, so the picture shows it beyond the line.
    /// </summary>
    internal sealed partial class MatchBall
    {
        private bool _behind;
        private int _lineX, _beyond, _restDepth, _backDepth, _minY, _maxY, _creep;

        /// <summary>
        /// The ball has just crossed the goal line at <paramref name="lineX"/> and is dead: it runs
        /// on behind it, between <paramref name="minY"/> and <paramref name="maxY"/>, from where it
        /// got to this tick. Depths and the creep (units per tick) are measured beyond the line.
        /// </summary>
        public void RunBehind(int lineX, int restDepth, int backDepth, int minY, int maxY, int creep)
        {
            _behind = true;
            _lineX = lineX;
            _beyond = lineX == 0 ? -1 : 1;
            _restDepth = restDepth;
            _backDepth = backDepth;
            _minY = minY;
            _maxY = maxY;
            _creep = creep;
            Dead = true;
            OwnerSide = -1;
            OwnerSlot = -1;
            Stop();
        }

        private void StepBehind()
        {
            if (!_behind) return;

            PrevX = X;
            PrevY = Y;
            if (Vx == 0 && Vy == 0 && (X - _lineX) * _beyond < _restDepth) X += _beyond * _creep;
            X += Vx;
            Y += Vy;
            Vx = Vx * _frictionPermille / 1000;
            Vy = Vy * _frictionPermille / 1000;
            if (U.Length(Vx, Vy) < _restSpeed)
            {
                Vx = 0;
                Vy = 0;
            }

            Stop();
        }

        /// <summary>
        /// The net, the boards and the side netting: a ball reaching one stops where its step met
        /// it, not slid along it. And it is a whole decimetre over the line from the tick it
        /// crossed, or the picture's decimetres show it on the line.
        /// </summary>
        private void Stop()
        {
            int depth = (X - _lineX) * _beyond, from = (PrevX - _lineX) * _beyond;
            bool deep = depth > _backDepth;
            if (deep)
            {
                if (from < _backDepth) Y = PrevY + (int)((long)(Y - PrevY) * (_backDepth - from) / (depth - from));
                X = _lineX + _beyond * _backDepth;
            }

            bool side = Y < _minY || Y > _maxY;
            if (side)
            {
                int edge = Y < _minY ? _minY : _maxY;
                if (PrevY >= _minY && PrevY <= _maxY && Y != PrevY)
                    X = PrevX + (int)((long)(X - PrevX) * (edge - PrevY) / (Y - PrevY));
                Y = edge;
            }

            if ((X - _lineX) * _beyond < U.Scale) X = _lineX + _beyond * U.Scale;
            if (!deep && !side) return;

            Vx = 0;
            Vy = 0;
        }
    }
}
