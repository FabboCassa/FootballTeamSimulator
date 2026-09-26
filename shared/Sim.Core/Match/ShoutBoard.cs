using System.Collections.Generic;
using Sim.Core.Config;

namespace Sim.Core.Match
{
    /// <summary>
    /// One bench's voice as the live shout panel shows it (watchable-match spec R11): the shout
    /// still running and for how long, and how long until the bench is heard again. It is read
    /// off the shouts the engine wrote into the report, so it counts plan-rule shouts too and
    /// can never offer a shout the engine would refuse.
    /// </summary>
    public readonly struct ShoutBoard
    {
        /// <summary>The five shouts, in the order the panel lays out its buttons.</summary>
        public static readonly IReadOnlyList<TouchlineShout> Choices = new[]
        {
            TouchlineShout.PressHigh, TouchlineShout.KeepBall, TouchlineShout.AllForward,
            TouchlineShout.Encourage, TouchlineShout.Concentrate
        };

        public TouchlineShout Active { get; }
        public int EffectMinutesLeft { get; }
        public int CooldownMinutesLeft { get; }

        /// <summary>Whether a shout called at the board's minute would be heard.</summary>
        public bool CanShout => CooldownMinutesLeft == 0;

        private ShoutBoard(TouchlineShout active, int effectMinutesLeft, int cooldownMinutesLeft)
        {
            Active = active;
            EffectMinutesLeft = effectMinutesLeft;
            CooldownMinutesLeft = cooldownMinutesLeft;
        }

        /// <summary>
        /// The board of <paramref name="clubId"/>'s bench at <paramref name="minute"/> — the minute
        /// a shout pressed now would be injected at — from the shouts heard before that minute.
        /// </summary>
        public static ShoutBoard Read(MatchReport? report, int clubId, int minute, ShoutBalance? balance = null)
        {
            ShoutBalance timing = balance ?? new ShoutBalance();
            var voice = new TouchlineShouts(timing.DurationMinutes, timing.CooldownMinutes);
            if (report != null)
            {
                foreach (MatchEvent e in report.Events)
                {
                    if (e.Type == MatchEventType.Shout && e.ClubId == clubId && e.Minute < minute)
                        voice.Call(true, e.Shout, e.Minute);
                }
            }

            return new ShoutBoard(voice.Active(true, minute), voice.MinutesLeft(true, minute), voice.CooldownLeft(true, minute));
        }
    }
}
