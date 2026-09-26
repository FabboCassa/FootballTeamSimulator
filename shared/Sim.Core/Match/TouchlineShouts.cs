namespace Sim.Core.Match
{
    /// <summary>What the coach shouts from the touchline (watchable-match spec R11).</summary>
    public enum TouchlineShout
    {
        None = 0,
        /// <summary>"Press high!": pressing up, fatigue up.</summary>
        PressHigh = 1,
        /// <summary>"Calm, keep the ball": tempo down, risk down.</summary>
        KeepBall = 2,
        /// <summary>"All forward!": mentality up, defensive exposure up.</summary>
        AllForward = 3,
        /// <summary>"Come on!": composure up, weaker each time it is repeated.</summary>
        Encourage = 4,
        /// <summary>"Concentrate!": fewer defensive errors, attack slightly more cautious.</summary>
        Concentrate = 5
    }

    /// <summary>A shout that was heard: who called it, what, and from which minute.</summary>
    public readonly struct ShoutCall
    {
        public readonly int Minute;
        public readonly bool Home;
        public readonly TouchlineShout Shout;

        public ShoutCall(int minute, bool home, TouchlineShout shout)
        {
            Minute = minute;
            Home = home;
            Shout = shout;
        }
    }

    /// <summary>
    /// Both benches' voices over a match. A shout is heard for a fixed number of minutes, and a
    /// bench that has shouted is not heard again until its cooldown has run out — a refused call
    /// changes nothing. Pure integer bookkeeping: no randomness, so it can never move a match that
    /// nobody shouts in.
    /// </summary>
    public sealed class TouchlineShouts
    {
        private const int Sides = 2;

        private readonly int _duration;
        private readonly int _cooldown;
        private readonly TouchlineShout[] _shout = new TouchlineShout[Sides];
        private readonly int[] _calledAt = new int[Sides];
        private readonly bool[] _called = new bool[Sides];
        private readonly int[] _encouragements = new int[Sides];
        private readonly int[] _repeats = new int[Sides];

        public TouchlineShouts(int durationMinutes, int cooldownMinutes)
        {
            _duration = durationMinutes < 0 ? 0 : durationMinutes;
            _cooldown = cooldownMinutes < 0 ? 0 : cooldownMinutes;
        }

        /// <summary>
        /// The bench shouts at <paramref name="minute"/>. Returns whether it was heard: no shout,
        /// an unknown one, or one inside the cooldown of the last heard shout is refused.
        /// </summary>
        public bool Call(bool home, TouchlineShout shout, int minute)
        {
            if (shout <= TouchlineShout.None || shout > TouchlineShout.Concentrate) return false;

            int s = home ? 0 : 1;
            if (_called[s] && minute < _calledAt[s] + _cooldown) return false;

            _called[s] = true;
            _calledAt[s] = minute;
            _shout[s] = shout;
            if (shout == TouchlineShout.Encourage)
            {
                _repeats[s] = _encouragements[s];
                _encouragements[s]++;
            }

            return true;
        }

        /// <summary>The shout the side is playing to at <paramref name="minute"/>, or None once it has expired.</summary>
        public TouchlineShout Active(bool home, int minute)
        {
            int s = home ? 0 : 1;
            if (!_called[s] || minute < _calledAt[s] || minute >= _calledAt[s] + _duration)
                return TouchlineShout.None;
            return _shout[s];
        }

        /// <summary>Minutes the side's active shout still has at <paramref name="minute"/>; 0 with none active.</summary>
        public int MinutesLeft(bool home, int minute)
        {
            if (Active(home, minute) == TouchlineShout.None) return 0;
            return _calledAt[home ? 0 : 1] + _duration - minute;
        }

        /// <summary>Minutes until the side is heard again; 0 means a <see cref="Call"/> at <paramref name="minute"/> is heard.</summary>
        public int CooldownLeft(bool home, int minute)
        {
            int s = home ? 0 : 1;
            if (!_called[s]) return 0;
            int left = _calledAt[s] + _cooldown - minute;
            return left > 0 ? left : 0;
        }

        /// <summary>How many encouragements the side had heard before its latest one.</summary>
        public int Repeats(bool home) => _repeats[home ? 0 : 1];
    }
}
