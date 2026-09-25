using System.Linq;
using NUnit.Framework;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;

namespace Sim.Core.Tests.Match
{
    /// <summary>The lineups and tallies behind the R9/R10/R11 effect measurements.</summary>
    [TestFixture]
    public class EffectLineupsTests
    {
        private static readonly PositionRole[] Shape =
        {
            PositionRole.Goalkeeper, PositionRole.CentreBack, PositionRole.CentreBack,
            PositionRole.FullBack, PositionRole.FullBack, PositionRole.DefensiveMidfielder,
            PositionRole.CentralMidfielder, PositionRole.CentralMidfielder,
            PositionRole.Winger, PositionRole.Winger, PositionRole.Striker
        };

        /// <summary>Everybody in his natural role; stamina 70 except the keeper (10) and the listed slots.</summary>
        private static Lineup Eleven(params (int Slot, int Stamina)[] stamina)
        {
            var lineup = new Lineup { ClubId = 7 };
            for (int i = 0; i < Shape.Length; i++)
            {
                int st = i == 0 ? 10 : 70;
                foreach ((int slot, int value) in stamina)
                    if (slot == i) st = value;
                lineup.Slots.Add(new LineupSlot
                {
                    Role = Shape[i],
                    Player = new Player { Id = 100 + i, Role = Shape[i], Attributes = new PlayerAttributes { Stamina = st } }
                });
            }

            return lineup;
        }

        [Test]
        public void MostTired_AreTheLowestStaminaOutfielders_TiesToTheLowerSlot()
        {
            Lineup lineup = Eleven((4, 40), (9, 30), (2, 55), (6, 55));

            Assert.That(EffectLineups.MostTired(lineup, 3), Is.EqualTo(new[] { 9, 4, 2 }),
                "the keeper (stamina 10) is never among them");
        }

        [Test]
        public void WithFreshSubs_ReplacesOnlyThoseSlots_WithEqualRatedNewMen_AndLeavesTheOriginal()
        {
            Lineup lineup = Eleven();

            Lineup subbed = EffectLineups.WithFreshSubs(lineup, new[] { 3, 8 }, idOffset: 1000);

            Assert.That(subbed, Is.Not.SameAs(lineup));
            Assert.That(subbed.ClubId, Is.EqualTo(lineup.ClubId));
            Assert.That(subbed.Slots[3].Player.Id, Is.EqualTo(1103));
            Assert.That(subbed.Slots[8].Player.Id, Is.EqualTo(1108));
            Assert.That(subbed.Slots[3].Player.Attributes.Stamina, Is.EqualTo(lineup.Slots[3].Player.Attributes.Stamina));
            Assert.That(subbed.Slots[3].Role, Is.EqualTo(lineup.Slots[3].Role));
            Assert.That(subbed.Slots[5].Player, Is.SameAs(lineup.Slots[5].Player));
            Assert.That(lineup.Slots[3].Player.Id, Is.EqualTo(103), "the original eleven is untouched");
            Assert.DoesNotThrow(subbed.Validate);
        }

        [Test]
        public void OutOfRole_KeepsTheShapeAndKeeper_AndMovesEveryOutfielderOffHisRole()
        {
            Lineup lineup = Eleven();

            Lineup shuffled = EffectLineups.OutOfRole(lineup);

            Assert.That(shuffled.Slots.Select(s => s.Role), Is.EqualTo(Shape));
            Assert.That(shuffled.Slots[0].Player, Is.SameAs(lineup.Slots[0].Player));
            Assert.That(shuffled.Slots.Select(s => s.Player.Id).OrderBy(id => id),
                Is.EqualTo(lineup.Slots.Select(s => s.Player.Id).OrderBy(id => id)));
            Assert.That(EffectLineups.OutOfRoleCount(lineup), Is.EqualTo(0));
            Assert.That(EffectLineups.OutOfRoleCount(shuffled), Is.EqualTo(10));
            Assert.That(lineup.Slots[1].Player.Id, Is.EqualTo(101), "the original eleven is untouched");
            Assert.DoesNotThrow(shuffled.Validate);
        }

        [Test]
        public void PairedEffect_ComparesTheMeans()
        {
            var e = new PairedEffect();
            e.Add(treatment: 2, baseline: 1);
            e.Add(treatment: 0, baseline: 0);
            e.Add(treatment: 1, baseline: -1);

            Assert.That(e.Count, Is.EqualTo(3));
            Assert.That(e.TreatmentMean, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(e.BaselineMean, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(e.Delta, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(new PairedEffect().Delta, Is.EqualTo(0.0));
        }

        [Test]
        public void PairedEffect_ChangePercent_IsRelativeToTheBaseline()
        {
            var e = new PairedEffect();
            e.Add(treatment: 6, baseline: 4);

            Assert.That(e.ChangePercent, Is.EqualTo(50.0).Within(1e-12));
            Assert.That(new PairedEffect().ChangePercent, Is.EqualTo(0.0));
        }

        [Test]
        public void StateTally_AveragesEachScoreStateOnItsOwn()
        {
            var t = new StateTally();
            t.Add(ScoreState.Leading, 1);
            t.Add(ScoreState.Leading, -1);
            t.Add(ScoreState.Leading, 3);
            t.Add(ScoreState.Trailing, -2);

            Assert.That(t.Count(ScoreState.Leading), Is.EqualTo(3));
            Assert.That(t.Mean(ScoreState.Leading), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(t.Mean(ScoreState.Trailing), Is.EqualTo(-2.0).Within(1e-12));
            Assert.That(t.Count(ScoreState.Level), Is.EqualTo(0));
            Assert.That(t.Mean(ScoreState.Level), Is.EqualTo(0.0));
        }
    }
}
