using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>One fast free-ball turn: its middle frame, its cause and the man nearest the ball there.</summary>
    public readonly struct FreeBallTurn
    {
        public int Frame { get; }
        public BallEventKind Cause { get; }
        public bool NearestHome { get; }
        public int NearestSlot { get; }
        public int NearestDm { get; }

        public FreeBallTurn(int frame, BallEventKind cause, bool nearestHome, int nearestSlot, int nearestDm)
        {
            Frame = frame;
            Cause = cause;
            NearestHome = nearestHome;
            NearestSlot = nearestSlot;
            NearestDm = nearestDm;
        }
    }

    /// <summary>
    /// The fast free-ball turns of one match (free-ball-real-movement spec R2), with their causes.
    /// See <see cref="FreeBallTurnAnalyzer"/> for the definitions.
    /// </summary>
    public sealed class FreeBallTurnMetrics
    {
        private readonly List<FreeBallTurn> _turns = new List<FreeBallTurn>();

        /// <summary>Stream frames per match minute, to read a turn's frame as a match clock.</summary>
        public int FramesPerMinute { get; set; }

        /// <summary>Pairs of 0.2 s steps that qualify: in play, fast, low and nobody within 2 m.</summary>
        public int Steps { get; set; }

        public IReadOnlyList<FreeBallTurn> Turns => _turns;

        public double TurnPercent => Steps <= 0 ? 0 : 100.0 * _turns.Count / Steps;

        public int CountOf(BallEventKind cause)
        {
            int n = 0;
            foreach (FreeBallTurn t in _turns)
                if (t.Cause == cause) n++;
            return n;
        }

        internal void Add(FreeBallTurn turn) => _turns.Add(turn);
    }
}
