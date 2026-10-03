using System;
using System.Reflection;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R5 of the real-match spec: an outfield man sent at a spot beyond a line runs to the line and
    /// stands on it, instead of pressing against it with a stride outside every tick. Drives the
    /// simulator's own steering (private, so through reflection) on a simulator a real match has
    /// set up, with every other man moved out of his way.
    /// </summary>
    [TestFixture]
    public class TouchlineSteerTests
    {
        private const int UnitsPerDm = 16;
        private const int OneMetreDm = 10;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly MatchBalance Cfg = new BalanceConfig().Match;
        private static Lineup _home = null!;
        private static Lineup _away = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _home = LineupSelector.BestEleven(league.Clubs[9]);
            _away = LineupSelector.BestEleven(league.Clubs[10]);
        }

        // Start 2 m inside a line, target 3 m beyond it, five seconds of running.
        [TestCase(Pitch.CenterX, 20, Pitch.CenterX, -30, true, TestName = "Sprint_Target3mOverTheNearTouchline")]
        [TestCase(Pitch.CenterX, Pitch.WidthDm - 20, Pitch.CenterX, Pitch.WidthDm + 30, true, TestName = "Sprint_Target3mOverTheFarTouchline")]
        [TestCase(20, Pitch.CenterY + 150, -30, Pitch.CenterY + 150, true, TestName = "Sprint_Target3mOverTheOwnGoalLine")]
        [TestCase(Pitch.LengthDm - 20, Pitch.CenterY - 150, Pitch.LengthDm + 30, Pitch.CenterY - 150, true, TestName = "Sprint_Target3mOverTheFarGoalLine")]
        [TestCase(Pitch.CenterX, 20, Pitch.CenterX, -30, false, TestName = "Jog_Target3mOverTheNearTouchline")]
        [TestCase(Pitch.LengthDm - 20, Pitch.CenterY - 150, Pitch.LengthDm + 30, Pitch.CenterY - 150, false, TestName = "Jog_Target3mOverTheFarGoalLine")]
        public void ATargetOverALine_HeRunsToTheLineAndStandsOnIt(int fromXDm, int fromYDm, int toXDm, int toYDm, bool sprint)
        {
            var steer = new SteerProbe(PlayedSimulator(), fromXDm * UnitsPerDm, fromYDm * UnitsPerDm);

            for (int tick = 0; tick < 5 * Cfg.TicksPerSecond; tick++)
            {
                steer.Step(toXDm * UnitsPerDm, toYDm * UnitsPerDm, sprint);
                Assert.That(steer.X, Is.InRange(0, Pitch.LengthDm * UnitsPerDm), $"tick {tick}: x off the pitch");
                Assert.That(steer.Y, Is.InRange(0, Pitch.WidthDm * UnitsPerDm), $"tick {tick}: y off the pitch");
            }

            int lineXDm = Math.Clamp(toXDm, 0, Pitch.LengthDm);
            int lineYDm = Math.Clamp(toYDm, 0, Pitch.WidthDm);
            Assert.That(Math.Abs(steer.X - lineXDm * UnitsPerDm), Is.LessThanOrEqualTo(OneMetreDm * UnitsPerDm), "within 1 m of the line (x)");
            Assert.That(Math.Abs(steer.Y - lineYDm * UnitsPerDm), Is.LessThanOrEqualTo(OneMetreDm * UnitsPerDm), "within 1 m of the line (y)");

            // Standing on it, not running into it: his last stride ends on the pitch.
            Assert.That(steer.StepToX, Is.InRange(0, Pitch.LengthDm * UnitsPerDm), "his stride reaches over the goal line");
            Assert.That(steer.StepToY, Is.InRange(0, Pitch.WidthDm * UnitsPerDm), "his stride reaches over the touchline");
            if (sprint)
                Assert.That((steer.Vx, steer.Vy), Is.EqualTo((0, 0)), "five seconds on the line and still running");
        }

        private static MatchSimulator PlayedSimulator()
        {
            var sim = new MatchSimulator(Cfg);
            var report = new MatchReport { HomeClubId = _home.ClubId, AwayClubId = _away.ClubId };
            sim.Generate(_home, _away, report, new Pcg32(7), null);
            return sim;
        }

        /// <summary>One outfield man of the home side, alone on the pitch, steered tick by tick.</summary>
        private sealed class SteerProbe
        {
            private readonly MatchSimulator _sim;
            private readonly MethodInfo _steer;
            private readonly int[] _px, _py, _vx, _vy, _stepToX, _stepToY;
            private readonly int _k;

            public SteerProbe(MatchSimulator sim, int x, int y)
            {
                _sim = sim;
                _steer = typeof(MatchSimulator).GetMethod("Steer", Private)!;
                _px = Field<int[]>("_px");
                _py = Field<int[]>("_py");
                _vx = Field<int[]>("_vx");
                _vy = Field<int[]>("_vy");
                _stepToX = Field<int[]>("_stepToX");
                _stepToY = Field<int[]>("_stepToY");
                bool[] keeper = Field<bool[]>("_keeper");
                bool[] sentOff = Field<bool[]>("_sentOff");

                // The ball is in play, so nobody is a set-piece taker.
                object ball = Field<object>("_ball");
                ball.GetType().GetField("Dead")!.SetValue(ball, false);

                while (keeper[_k] || sentOff[_k]) _k++;

                // Everybody else in a heap on the centre spot, far outside any separation radius.
                for (int i = 0; i < _px.Length; i++)
                {
                    _px[i] = Pitch.CenterX * UnitsPerDm;
                    _py[i] = Pitch.CenterY * UnitsPerDm;
                }

                _px[_k] = x;
                _py[_k] = y;
                _vx[_k] = 0;
                _vy[_k] = 0;
            }

            public int X => _px[_k];
            public int Y => _py[_k];
            public int Vx => _vx[_k];
            public int Vy => _vy[_k];
            public int StepToX => _stepToX[_k];
            public int StepToY => _stepToY[_k];

            public void Step(int tx, int ty, bool sprint) => _steer.Invoke(_sim, new object[] { _k, tx, ty, sprint });

            private T Field<T>(string name) =>
                (T)typeof(MatchSimulator).GetField(name, Private)!.GetValue(_sim)!;
        }
    }
}
