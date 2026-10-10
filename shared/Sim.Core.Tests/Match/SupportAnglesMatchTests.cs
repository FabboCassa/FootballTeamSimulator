using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Real-match spec R9 in a whole V11 match: in the build-up and the progression the men
    /// nearest the ball go and offer the short option, and a pressed man gives the ball to a free
    /// team-mate. Each is counted by the simulator, and each is off when its knob is. The angle
    /// and the release themselves are scripted in <see cref="SupportAnglesR9Tests"/>, the passing
    /// chains they buy are the realism harness's bands.
    /// </summary>
    [TestFixture]
    public class SupportAnglesMatchTests
    {
        private const ulong Seed = 3303;

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        private static MatchSimulator Play(BalanceConfig cfg)
        {
            var side = new TacticContext(new Tactic(Formation.F433, TacticInstructions.Neutral), cfg.Tactics.FamiliarityMax);
            var sim = new MatchSimulator(cfg.Match);
            sim.Generate(LineupSelector.BestEleven(_league.Clubs[9]), LineupSelector.BestEleven(_league.Clubs[10]),
                new MatchReport(), new Pcg32(Seed), new MatchTactics(side, side));
            return sim;
        }

        [Test]
        public void InAMatch_BothSidesOfferTheShortOption_AndNobodyDoesWithTheOffersOff()
        {
            MatchSimulator on = Play(new BalanceConfig());
            var still = new BalanceConfig();
            still.Match.V11SupportOffers = 0;
            MatchSimulator off = Play(still);

            TestContext.Out.WriteLine($"[R9 offers] player-ticks offering: home {on.OfferTicks(0)}, away {on.OfferTicks(1)}");
            Assert.That(on.OfferTicks(0), Is.GreaterThan(0), "the home side's men come short");
            Assert.That(on.OfferTicks(1), Is.GreaterThan(0), "and the away side's");
            Assert.That(off.OfferTicks(0) + off.OfferTicks(1), Is.Zero);
        }

        [Test]
        public void InAMatch_PressedMenReleaseTheBall_AndNobodyDoesWithTheReleaseOff()
        {
            MatchSimulator on = Play(new BalanceConfig());
            var never = new BalanceConfig();
            never.Match.V11PressedReleasePermille = 1001;   // no man is ever pressed that hard
            MatchSimulator off = Play(never);

            TestContext.Out.WriteLine($"[R9 release] pressed releases: home {on.PressedReleases(0)}, away {on.PressedReleases(1)}");
            Assert.That(on.PressedReleases(0) + on.PressedReleases(1), Is.GreaterThan(0),
                "a pressed man with a free team-mate gives it to him");
            Assert.That(off.PressedReleases(0) + off.PressedReleases(1), Is.Zero);
        }
    }
}
