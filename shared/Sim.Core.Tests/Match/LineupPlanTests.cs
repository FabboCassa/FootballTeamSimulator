using System.Linq;
using NUnit.Framework;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    [TestFixture]
    public class LineupPlanTests
    {
        private static Club AnyClub()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(31337));
            return league.Clubs[0];
        }

        [Test]
        public void From_BestEleven_RoundTripsThroughMaterialize()
        {
            Club club = AnyClub();
            Lineup best = LineupSelector.BestEleven(club);

            LineupPlan plan = LineupPlan.From(best);
            Lineup resolved = plan.Materialize(club);

            Assert.That(resolved.ClubId, Is.EqualTo(club.Id));
            Assert.That(resolved.Slots.Select(s => (s.Role, s.Player.Id)),
                Is.EqualTo(best.Slots.Select(s => (s.Role, s.Player.Id))));
        }

        [Test]
        public void Materialize_MissingPlayer_Throws()
        {
            Club club = AnyClub();
            LineupPlan plan = LineupPlan.From(LineupSelector.BestEleven(club));
            plan.Slots[5].PlayerId = 123456789;

            Assert.That(() => plan.Materialize(club), Throws.InvalidOperationException);
        }

        [Test]
        public void TryMaterialize_DuplicatePlayer_ReturnsFalse()
        {
            Club club = AnyClub();
            LineupPlan plan = LineupPlan.From(LineupSelector.BestEleven(club));
            plan.Slots[5].PlayerId = plan.Slots[6].PlayerId;

            bool ok = plan.TryMaterialize(club, out Lineup? lineup);

            Assert.That(ok, Is.False);
            Assert.That(lineup, Is.Null);
        }
    }
}
