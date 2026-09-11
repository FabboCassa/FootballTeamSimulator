using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Positioning
{
    /// <summary>
    /// Task 6.10 acceptance: free player positioning + zone-based roles.
    ///   • a fullback dragged high on the flank is played as a winger (role by zone);
    ///   • pushing the whole line up scores AND concedes measurably more (the tilt);
    ///   • a wider setup changes width-driven output;
    ///   • no "park everyone forward" exploit (win rate stays capped);
    ///   • opt-in + clean-preset identity → golden masters/replays are unaffected.
    ///
    /// All deterministic (integer geometry); the harness numbers are printed for the
    /// user to judge the magnitudes, the guards are comparative so they hold regardless
    /// of exact calibration.
    /// </summary>
    [TestFixture]
    public class PositioningTests
    {
        private static League _league = null!;
        private static Club _club = null!; // equal-squad tests use one club on both sides
        private static readonly BalanceConfig _cfg = new BalanceConfig();
        private static MatchBalance M => _cfg.Match;
        private static PositioningBalance P => _cfg.Positioning;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260706));
            _club = _league.Clubs[9];
        }

        // ---------------------------------------------------------------- helpers

        private static PositionRole[] Roles(Lineup l)
        {
            var roles = new PositionRole[l.Slots.Count];
            for (int i = 0; i < l.Slots.Count; i++) roles[i] = l.Slots[i].Role;
            return roles;
        }

        /// <summary>Attaches each slot's canonical anchor position (a clean preset → zero tilt).</summary>
        private static Lineup WithAnchors(Lineup l)
        {
            PositionRole[] roles = Roles(l);
            for (int i = 0; i < l.Slots.Count; i++)
            {
                int ax = FormationGeometry.AnchorX(l.Slots[i].Role, M);
                int ay = FormationGeometry.AnchorY(roles, i, M);
                l.Slots[i].Position = new SlotPosition(ax, ay);
            }
            return l;
        }

        /// <summary>Shifts every outfield player's line height by <paramref name="delta"/> permille (keeps roles).</summary>
        private static Lineup PushHeight(Lineup l, int delta)
        {
            foreach (LineupSlot s in l.Slots)
            {
                if (s.Role == PositionRole.Goalkeeper || s.Position == null) continue;
                SlotPosition p = s.Position.Value;
                s.Position = new SlotPosition(p.XPermille + delta, p.YPermille);
            }
            return l;
        }

        /// <summary>Widens (positive) or narrows (negative) every outfield player relative to centre.</summary>
        private static Lineup PushSpread(Lineup l, int delta)
        {
            foreach (LineupSlot s in l.Slots)
            {
                if (s.Role == PositionRole.Goalkeeper || s.Position == null) continue;
                SlotPosition p = s.Position.Value;
                int dir = p.YPermille >= 500 ? 1 : -1;
                s.Position = new SlotPosition(p.XPermille, p.YPermille + dir * delta);
            }
            return l;
        }

        // ---------------------------------------------------------------- zone → role

        [Test]
        public void PresetAnchors_RoundTripToTheirRoles()
        {
            // Every formation preset sits its players on the role anchors, so ZoneRole must
            // map each anchor back to its own role — this is why "reset on formation change"
            // restores the exact preset roles (a clean preset is a fixed point of the map).
            foreach (Formation f in Formations.All)
            {
                PositionRole[] roles = Formations.Roles(f);
                for (int i = 0; i < roles.Length; i++)
                {
                    int ax = FormationGeometry.AnchorX(roles[i], M);
                    int ay = FormationGeometry.AnchorY(roles, i, M);
                    PositionRole resolved = ZoneRole.Resolve(roles[i], new SlotPosition(ax, ay), P);
                    Assert.That(resolved, Is.EqualTo(roles[i]),
                        $"{f} slot {i} ({roles[i]}) anchor ({ax},{ay}) must resolve back to itself");
                }
            }
        }

        [Test]
        public void FullBack_DraggedHighAndWide_BecomesWinger()
        {
            // High + wide → winger; high + central → striker; a keeper never changes.
            Assert.That(ZoneRole.Resolve(PositionRole.FullBack, new SlotPosition(720, 920), P),
                Is.EqualTo(PositionRole.Winger), "a fullback pushed high on the flank plays as a winger");
            Assert.That(ZoneRole.Resolve(PositionRole.FullBack, new SlotPosition(720, 500), P),
                Is.EqualTo(PositionRole.Striker), "pushed high but central he is a striker");
            Assert.That(ZoneRole.Resolve(PositionRole.CentreBack, new SlotPosition(150, 900), P),
                Is.EqualTo(PositionRole.FullBack), "a deep centre-back pulled wide is a fullback");
            Assert.That(ZoneRole.Resolve(PositionRole.Goalkeeper, new SlotPosition(720, 920), P),
                Is.EqualTo(PositionRole.Goalkeeper), "a goalkeeper is always a goalkeeper");
        }

        [Test]
        public void ZoneRole_IsMonotonicInLineHeight_Central()
        {
            // Walking a central player from his own goal to the opponent's passes through
            // the bands in order: CB → DM → CM → AM → ST (never backwards).
            var seen = new List<PositionRole>();
            PositionRole last = (PositionRole)(-1);
            for (int x = 0; x <= 1000; x += 10)
            {
                PositionRole r = ZoneRole.Resolve(PositionRole.CentralMidfielder, new SlotPosition(x, 500), P);
                if (r != last) { seen.Add(r); last = r; }
            }
            Assert.That(seen, Is.EqualTo(new List<PositionRole>
            {
                PositionRole.CentreBack, PositionRole.DefensiveMidfielder,
                PositionRole.CentralMidfielder, PositionRole.AttackingMidfielder, PositionRole.Striker
            }), "central line-height bands must progress CB→DM→CM→AM→ST in order");
        }

        // ---------------------------------------------------------------- opt-in / identity (golden-master safety)

        [Test]
        public void FlagOff_IgnoresCustomPositions_ByteIdentical()
        {
            Lineup pushed = PushHeight(WithAnchors(LineupSelector.BestEleven(_club)), 300);
            Lineup plain = LineupSelector.BestEleven(_club);

            // Compared WITHOUT the movement stream, because since task 13.1 the stream lays
            // players out on their custom positions — a shape the user arranges on the Tactics
            // pitch is the shape that lines up on the match pitch, which is the point of it.
            // That is presentation; what this test guards is the SIM, i.e. the score and the
            // event timeline, and those must not move while the flag is off.
            var off = new MatchEngine(_cfg, generatePositions: false); // applyPositioning defaults false
            string withPos = JsonSerializer.Serialize(off.Simulate(pushed, plain, new Pcg32(777)));
            string noPos = JsonSerializer.Serialize(off.Simulate(
                LineupSelector.BestEleven(_club), plain, new Pcg32(777)));

            Assert.That(withPos, Is.EqualTo(noPos),
                "with the positioning flag off, custom positions must not change the sim (golden-master safety)");
        }

        [Test]
        public void CustomPositions_ShowUpInTheMovementStream_EvenWithTheFlagOff()
        {
            // The other half of the line above, and the reason it had to be redrawn (13.1):
            // the shape is DRAWN wherever the coach put his players, whether or not the engine
            // is opted into the rating tilt. Same seed, same result — different geometry.
            Lineup pushed = PushHeight(WithAnchors(LineupSelector.BestEleven(_club)), 300);
            Lineup plain = LineupSelector.BestEleven(_club);

            var off = new MatchEngine(_cfg);
            MatchReport shaped = off.Simulate(pushed, plain, new Pcg32(777));
            MatchReport preset = off.Simulate(LineupSelector.BestEleven(_club), plain, new Pcg32(777));

            // ENGINE PHASE 6. This used to end "same seed, same result — different geometry", and
            // that half of it is gone with the causality: a side drawn thirty metres further up
            // the pitch is a side PLAYING thirty metres further up the pitch, and where a man
            // stands is now one of the things that decides the match. What the test still pins —
            // and what task 6.10 was actually about — is that dragging a player off his anchor
            // MOVES HIM, with or without the rating tilt the flag turns on.
            Assert.That(
                JsonSerializer.Serialize(shaped.Positions), Is.Not.EqualTo(JsonSerializer.Serialize(preset.Positions)),
                "a side pushed 300 permille up the pitch must be DRAWN further up the pitch");
        }

        [Test]
        public void CleanPreset_And_NullPositions_AreIdentity_WhenFlagOn()
        {
            Lineup plain = LineupSelector.BestEleven(_club); // null positions
            var off = new MatchEngine(_cfg);
            var on = new MatchEngine(_cfg, applyPositioning: true);

            string baseline = JsonSerializer.Serialize(off.Simulate(plain, plain, new Pcg32(4242)));

            // Null positions with the flag on: nothing to tilt → identity.
            string nullOn = JsonSerializer.Serialize(on.Simulate(
                LineupSelector.BestEleven(_club), LineupSelector.BestEleven(_club), new Pcg32(4242)));
            Assert.That(nullOn, Is.EqualTo(baseline), "flag on + no custom positions must be identity");

            // Clean preset (every player on his role anchor) with the flag on: zero tilt → identity.
            string presetOn = JsonSerializer.Serialize(on.Simulate(
                WithAnchors(LineupSelector.BestEleven(_club)),
                WithAnchors(LineupSelector.BestEleven(_club)), new Pcg32(4242)));
            Assert.That(presetOn, Is.EqualTo(baseline),
                "flag on + a clean formation preset must be identity (the tilt is a delta from the anchors)");
        }

        [Test]
        public void SameLineup_SamePositions_IsDeterministic()
        {
            Lineup a = PushHeight(WithAnchors(LineupSelector.BestEleven(_club)), 180);
            Lineup b = WithAnchors(LineupSelector.BestEleven(_club));
            var on = new MatchEngine(_cfg, applyPositioning: true);
            string r1 = JsonSerializer.Serialize(on.Simulate(a, b, new Pcg32(31337)));

            Lineup a2 = PushHeight(WithAnchors(LineupSelector.BestEleven(_club)), 180);
            Lineup b2 = WithAnchors(LineupSelector.BestEleven(_club));
            string r2 = JsonSerializer.Serialize(on.Simulate(a2, b2, new Pcg32(31337)));
            Assert.That(r2, Is.EqualTo(r1));
        }

        // ---------------------------------------------------------------- the ✅ effect

        [Test]
        public void PushingTheLineUp_ScoresAndConcedesMore()
        {
            // Equal squads. Side A pushes its whole outfield line forward; B stays on its
            // preset. Identical behaviour otherwise, alternating venue so home advantage
            // cancels. A must both SCORE more and CONCEDE more than the same A at preset.
            const int seeds = 400;
            const int push = 200; // permille (a clear, sub-cap tilt: ~+4% attack / −4% defense)

            long gfBase = 0, gaBase = 0, gfPush = 0, gaPush = 0;
            var off = new MatchEngine(_cfg, generatePositions: false);  // statistics only: the picture costs 96ms a match and nothing here looks at it
            var on = new MatchEngine(_cfg, applyPositioning: true, generatePositions: false);

            for (ulong i = 0; i < seeds; i++)
            {
                bool aHome = i % 2 == 0;
                ulong seed = 60000 + i;

                Lineup aBase = LineupSelector.BestEleven(_club);
                Lineup bBase = LineupSelector.BestEleven(_club);
                Accumulate(off, aBase, bBase, aHome, seed, ref gfBase, ref gaBase);

                Lineup aPush = PushHeight(WithAnchors(LineupSelector.BestEleven(_club)), push);
                Lineup bPush = LineupSelector.BestEleven(_club);
                Accumulate(on, aPush, bPush, aHome, seed, ref gfPush, ref gaPush);
            }

            TestContext.Out.WriteLine(
                $"[positioning-line] pushed +{push}‰: GF {gfBase}→{gfPush}  GA {gaBase}→{gaPush} over {seeds} matches");
            Assert.That(gfPush, Is.GreaterThan(gfBase), "pushing the line up must score more");
            Assert.That(gaPush, Is.GreaterThan(gaBase), "pushing the line up must concede more (open game)");
        }

        [Test]
        public void WiderSetup_CreatesMoreThanNarrow()
        {
            // A wider shape trades central control for chance creation (like the Width
            // instruction): a spread side should out-score the same side kept narrow.
            const int seeds = 400;
            const int spread = 200;

            long gfWide = 0, gfNarrow = 0;
            var on = new MatchEngine(_cfg, applyPositioning: true, generatePositions: false);  // statistics only: the picture costs 96ms a match and nothing here looks at it

            for (ulong i = 0; i < seeds; i++)
            {
                bool aHome = i % 2 == 0;
                ulong seed = 70000 + i;

                Lineup aWide = PushSpread(WithAnchors(LineupSelector.BestEleven(_club)), spread);
                long gf = 0, ga = 0;
                Accumulate(on, aWide, LineupSelector.BestEleven(_club), aHome, seed, ref gf, ref ga);
                gfWide += gf;

                Lineup aNarrow = PushSpread(WithAnchors(LineupSelector.BestEleven(_club)), -spread);
                gf = 0; ga = 0;
                Accumulate(on, aNarrow, LineupSelector.BestEleven(_club), aHome, seed, ref gf, ref ga);
                gfNarrow += gf;
            }

            TestContext.Out.WriteLine(
                $"[positioning-width] GF wide {gfWide} vs narrow {gfNarrow} over {seeds} matches");
            Assert.That(gfWide, Is.GreaterThan(gfNarrow),
                "a wider shape must create more than the same side kept narrow");
        }

        [Test]
        public void ParkEveryoneForward_IsNotADominantExploit()
        {
            // The anti-exploit ✅: shove the whole outfield line to the front (max tilt) and
            // it must NOT run away with games — attacking gains are paid for by defensive
            // exposure, and the tilt is capped. Win rate over the field must stay ≤ ~55%.
            const int seeds = 1000;
            const int push = 500; // saturates the tilt cap

            int aWins = 0, bWins = 0, draws = 0;
            var on = new MatchEngine(_cfg, applyPositioning: true, generatePositions: false);  // statistics only: the picture costs 96ms a match and nothing here looks at it

            for (ulong i = 0; i < seeds; i++)
            {
                bool aHome = i % 2 == 0;
                ulong seed = 80000 + i;
                Lineup a = PushHeight(WithAnchors(LineupSelector.BestEleven(_club)), push);
                Lineup b = LineupSelector.BestEleven(_club);

                int ag, bg;
                if (aHome)
                {
                    MatchReport r = on.Simulate(a, b, new Pcg32(seed));
                    ag = r.HomeGoals; bg = r.AwayGoals;
                }
                else
                {
                    MatchReport r = on.Simulate(b, a, new Pcg32(seed));
                    ag = r.AwayGoals; bg = r.HomeGoals;
                }
                if (ag > bg) aWins++; else if (ag < bg) bWins++; else draws++;
            }

            int decisive = aWins + bWins;
            double winRate = (double)aWins / seeds;
            double share = decisive > 0 ? 100.0 * aWins / decisive : 50.0;
            TestContext.Out.WriteLine(
                $"[positioning-exploit] all-forward win rate {winRate * 100:F1}% (share of decided {share:F1}%), draws {draws / 10.0}% over {seeds}");
            Assert.That(winRate, Is.LessThan(0.58),
                "parking everyone forward must not be a dominant exploit (attack gains are paid for defensively)");
        }

        private static void Accumulate(
            MatchEngine engine, Lineup a, Lineup b, bool aHome, ulong seed, ref long gf, ref long ga)
        {
            if (aHome)
            {
                MatchReport r = engine.Simulate(a, b, new Pcg32(seed));
                gf += r.HomeGoals; ga += r.AwayGoals;
            }
            else
            {
                MatchReport r = engine.Simulate(b, a, new Pcg32(seed));
                gf += r.AwayGoals; ga += r.HomeGoals;
            }
        }
    }
}
