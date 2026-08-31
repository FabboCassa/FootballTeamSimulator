using System.Collections.Generic;

namespace Sim.Core.Match.Movement
{
    /// <summary>What the script tells the integrator to do with the ball at a given tick.</summary>
    internal enum ScriptOp
    {
        /// <summary>Ball to the centre spot, given to a player of the kicking-off side.</summary>
        Kickoff = 0,
        /// <summary>The carrier plays it to a team-mate (Pass / LongBall / Cross).</summary>
        Pass = 1,
        /// <summary>The carrier beats a man and drives on; the ball stays at his feet.</summary>
        Dribble = 2,
        /// <summary>Arms the scripted shooter's run toward his shooting position.</summary>
        AimShot = 3,
        /// <summary>The scripted shooter strikes toward the goal.</summary>
        Shot = 4,
        /// <summary>How the shot ended (Goal / Save / Miss), fired when it arrives.</summary>
        Outcome = 5,
        /// <summary>Possession changes hands (Tackle / Interception).</summary>
        Turnover = 6,
        /// <summary>Ball out of play; the spot is derived from where the ball is.</summary>
        DeadBall = 7
    }

    /// <summary>
    /// One instruction of the choreography. The planner decides WHAT happens and WHEN
    /// (that is where every random draw is spent); the integrator decides WHO, from the
    /// live positions, which keeps it a pure function of the state it has already built.
    /// </summary>
    internal struct ScriptStep
    {
        public int Tick;
        public int Seq;                 // emission order, for a stable sort within a tick
        public ScriptOp Op;
        public BallActionKind Kind;     // the label the commentary shows
        public bool Home;               // the side acting (for a Turnover: the side WINNING the ball)
        public int Slot;                // forced actor, or -1 to use the current carrier
        public int Target;              // forced receiver, or -1 to resolve from the bias
        public int Bias;                // -1 backwards · 0 square · 1 forward · 2 long
        public int Flight;              // ticks the ball spends travelling
        public int X;                   // target point (shots, and the goal mouth)
        public int Y;
    }

    /// <summary>The whole match choreography, in tick order.</summary>
    internal sealed class MatchScript
    {
        public List<ScriptStep> Steps { get; } = new List<ScriptStep>();

        public const int BiasBack = -1;
        public const int BiasSquare = 0;
        public const int BiasForward = 1;
        public const int BiasLong = 2;

        public void Add(
            int tick, ScriptOp op, BallActionKind kind, bool home,
            int slot = -1, int target = -1, int bias = BiasForward, int flight = 0, int x = 0, int y = 0)
        {
            Steps.Add(new ScriptStep
            {
                Tick = tick < 0 ? 0 : tick,
                Seq = Steps.Count,
                Op = op,
                Kind = kind,
                Home = home,
                Slot = slot,
                Target = target,
                Bias = bias,
                Flight = flight,
                X = x,
                Y = y
            });
        }

        /// <summary>Orders the steps by tick, keeping emission order inside a tick.</summary>
        public void Sort() => Steps.Sort(static (a, b) => a.Tick != b.Tick ? a.Tick - b.Tick : a.Seq - b.Seq);
    }
}
