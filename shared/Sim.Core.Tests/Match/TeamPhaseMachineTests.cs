using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Match.Movement;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R1 team phases (watchable-match spec, task 5), on scripted positions: no match is played,
    /// the machine is fed a ball position, who has it and whether it is dead, tick by tick.
    /// Home (side 0) attacks toward x = Pitch.LengthDm, away toward x = 0.
    /// </summary>
    [TestFixture]
    public class TeamPhaseMachineTests
    {
        private const int Home = 0;
        private const int Away = 1;
        private const int Nobody = -1;

        private MatchBalance _cfg = null!;
        private TeamPhaseMachine _machine = null!;
        private int _tick;

        private int OwnThirdDm => Pitch.LengthDm * _cfg.PhaseBuildUpEndPermille / 1000 / 2;
        private int MiddleDm => Pitch.CenterX;
        private int FinalThirdDm => Pitch.LengthDm - OwnThirdDm;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _machine = new TeamPhaseMachine(_cfg);
            _tick = 0;
        }

        private void Live(int ballXDm, int owner) => _machine.Update(++_tick, ballXDm, owner, false, Nobody);

        private void Dead(int ballXDm, int restartSide) => _machine.Update(++_tick, ballXDm, Nobody, true, restartSide);

        private void Phases(TeamPhase home, TeamPhase away, string because)
        {
            Assert.That(_machine.PhaseOf(Home), Is.EqualTo(home), "home: " + because);
            Assert.That(_machine.PhaseOf(Away), Is.EqualTo(away), "away: " + because);
        }

        /// <summary>Home has had the ball long enough for any transition to be over.</summary>
        private void SettledHomePossession(int ballXDm)
        {
            Dead(MiddleDm, Home);
            for (int i = 0; i <= _cfg.PhaseTransitionTicks; i++) Live(ballXDm, Home);
        }

        [Test]
        public void Thresholds_AreTunableInMatchBalance_WithSaneDefaults()
        {
            Assert.That(_cfg.PhaseBuildUpEndPermille, Is.InRange(1, 499));
            Assert.That(_cfg.PhaseFinalThirdStartPermille, Is.InRange(501, 999));
            Assert.That(_cfg.PhaseTransitionTicks, Is.EqualTo(_cfg.TicksOfMs(_cfg.PhaseTransitionMs)));
        }

        [Test]
        public void DeadBall_PutsBothSidesInSetPiece()
        {
            SettledHomePossession(MiddleDm);
            Dead(FinalThirdDm, Away);

            Phases(TeamPhase.SetPiece, TeamPhase.SetPiece, "a dead ball is a set piece for both sides");
        }

        [Test]
        public void BeforeAnythingHappens_TheKickoffIsASetPiece()
        {
            Phases(TeamPhase.SetPiece, TeamPhase.SetPiece, "a match starts on a dead ball");
        }

        [Test]
        public void BallInTheAttackersOwnThird_IsBuildUp()
        {
            SettledHomePossession(OwnThirdDm);

            Phases(TeamPhase.BuildUp, TeamPhase.BuildUp, "home builds up and away defends the build-up");
        }

        [Test]
        public void BallInMidfield_IsProgression()
        {
            SettledHomePossession(MiddleDm);

            Phases(TeamPhase.Progression, TeamPhase.Progression, "the ball is being moved through the middle");
        }

        [Test]
        public void BallInTheFinalThird_IsFinalThird()
        {
            SettledHomePossession(MiddleDm);
            Live(FinalThirdDm, Home);

            Phases(TeamPhase.FinalThird, TeamPhase.FinalThird, "home is in the final third");
        }

        [Test]
        public void TheThirds_AreReadInTheAttackersDirection()
        {
            Dead(MiddleDm, Away);
            for (int i = 0; i <= _cfg.PhaseTransitionTicks; i++) Live(OwnThirdDm, Away);

            Phases(TeamPhase.FinalThird, TeamPhase.FinalThird,
                "near x = 0 is away's final third, not its build-up");

            Live(FinalThirdDm, Away);
            Phases(TeamPhase.BuildUp, TeamPhase.BuildUp, "near x = length is away's own third");
        }

        [Test]
        public void Turnover_PutsTheWinnerInAttackTransition_AndTheLoserInDefenceTransition()
        {
            SettledHomePossession(FinalThirdDm);
            Live(FinalThirdDm, Away);

            Phases(TeamPhase.DefenceTransition, TeamPhase.AttackTransition, "away won the ball back");
        }

        [Test]
        public void ALooseBall_IsNotATurnover()
        {
            SettledHomePossession(MiddleDm);
            Live(MiddleDm, Nobody);

            Phases(TeamPhase.Progression, TeamPhase.Progression, "a pass in flight is still home's ball");
        }

        [Test]
        public void Transition_LastsItsWindow_ThenTheThirdsTakeOver()
        {
            SettledHomePossession(MiddleDm);
            Live(MiddleDm, Away);

            for (int i = 1; i < _cfg.PhaseTransitionTicks; i++)
            {
                Live(MiddleDm, Nobody);
                Phases(TeamPhase.DefenceTransition, TeamPhase.AttackTransition, $"inside the window, tick {i}");
            }

            Live(MiddleDm, Away);
            Phases(TeamPhase.Progression, TeamPhase.Progression, "the window is over");
        }

        [Test]
        public void ADeadBall_EndsTheTransition_AndTheRestartIsNotATurnover()
        {
            SettledHomePossession(MiddleDm);
            Live(MiddleDm, Away);
            Dead(MiddleDm, Home);
            Phases(TeamPhase.SetPiece, TeamPhase.SetPiece, "the ball went out");

            Live(MiddleDm, Home);
            Phases(TeamPhase.Progression, TeamPhase.Progression,
                "taking the restart the ball was awarded is not winning it back");
        }

        [Test]
        public void ATurnoverInsideTheWindow_FlipsTheTransition()
        {
            SettledHomePossession(MiddleDm);
            Live(MiddleDm, Away);
            Live(MiddleDm, Home);

            Phases(TeamPhase.AttackTransition, TeamPhase.DefenceTransition, "home won it straight back");
        }

        [Test]
        public void EachSide_IsInExactlyOneDefinedPhase_EveryTick()
        {
            int[] owners = { Home, Nobody, Away, Away, Nobody, Home };
            for (int t = 0; t < 600; t++)
            {
                if (t % 97 == 0) Dead(t % Pitch.LengthDm, t % 2);
                else Live((t * 37) % (Pitch.LengthDm + 1), owners[t % owners.Length]);

                for (int side = 0; side < 2; side++)
                    Assert.That(Enum.IsDefined(typeof(TeamPhase), _machine.PhaseOf(side)), Is.True);
            }

            Assert.That(Enum.GetValues(typeof(TeamPhase)).Length, Is.EqualTo(TeamPhaseMachine.PhaseCount));
        }
    }
}
