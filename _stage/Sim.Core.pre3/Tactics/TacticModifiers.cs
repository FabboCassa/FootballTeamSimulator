using Sim.Core.Config;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// Turns a side's tactic (its own instructions, the opponent's instructions,
    /// and its familiarity) into attack/midfield/defense multipliers (task 3.2).
    ///
    /// Determinism: only +,-,*,/ on doubles and integer ops - no transcendental
    /// functions - so it is bit-identical across .NET / Mono / IL2CPP, like the
    /// rest of the engine.
    ///
    /// Fairness (see TacticsBalance): self-effects are trade-offs and counter terms
    /// are zero-sum over a uniform field, so a fully neutral tactic yields exactly
    /// 1.0/1.0/1.0 and no tactic gains on average against the field.
    /// </summary>
    public static class TacticModifiers
    {
        public readonly struct Multipliers
        {
            public readonly double Attack;
            public readonly double Midfield;
            public readonly double Defense;

            public Multipliers(double attack, double midfield, double defense)
            {
                Attack = attack;
                Midfield = midfield;
                Defense = defense;
            }

            public static Multipliers Identity => new Multipliers(1.0, 1.0, 1.0);
        }

        public static Multipliers Compute(
            TacticInstructions me, TacticInstructions opp, int familiarity, TacticsBalance cfg)
        {
            int attack = 0, midfield = 0, defense = 0; // accumulated percent deltas

            // --- Self-effects (trade-offs) ---
            int m = Sign(me.Mentality);   // Def -1 / Bal 0 / Att +1
            int p = Sign(me.Pressing);    // Low -1 / Med 0 / High +1
            int t = Sign(me.Tempo);       // Slow -1 / Normal 0 / Fast +1
            int w = Sign(me.Width);       // Narrow -1 / Normal 0 / Wide +1

            // Possession (midfield) is moved ONLY by width, so the press/tempo
            // counters below are not drowned out by a possession swing (a midfield
            // gap is squared by PossessionSharpness and would otherwise dominate).
            attack += m * cfg.MentalitySwingPercent; defense -= m * cfg.MentalitySwingPercent;
            attack += p * cfg.PressingSwingPercent; defense -= p * cfg.PressingSwingPercent;
            attack += t * cfg.TempoSwingPercent; defense -= t * cfg.TempoSwingPercent;
            attack += w * cfg.WidthSwingPercent; midfield -= w * cfg.WidthSwingPercent;

            // --- Counter-matrix (zero-sum over a uniform field) ---
            int pOpp = Sign(opp.Pressing), tOpp = Sign(opp.Tempo);
            // Fast/direct tempo plays through a high press (and is absorbed by a low block):
            attack += t * pOpp * cfg.CounterPressVsTempoPercent;
            // ...leaving the high press exposed at the back against fast tempo:
            defense -= p * tOpp * cfg.CounterPressVsTempoPercent;
            // A high (attacking) line is punished defensively by fast play, solid vs slow:
            defense -= m * tOpp * cfg.CounterMentalityVsTempoPercent;
            // Cyclic width edge (Wide>Narrow>Normal>Wide), on attack:
            attack += RpsWidth((int)me.Width, (int)opp.Width) * cfg.CounterWidthPercent;

            // --- Familiarity: an unfamiliar tactic is less effective overall ---
            double familiarityMult = FamiliarityModel.EffectivenessMultiplier(familiarity, cfg);

            double attackMult = Clamp(100 + attack, cfg) * familiarityMult;
            double midfieldMult = Clamp(100 + midfield, cfg);
            double defenseMult = Clamp(100 + defense, cfg) * familiarityMult;

            return new Multipliers(attackMult, midfieldMult, defenseMult);
        }

        /// <summary>Maps a 3-value axis to -1 / 0 / +1 around its neutral middle.</summary>
        private static int Sign(Mentality v) => (int)v - 1;
        private static int Sign(Pressing v) => (int)v - 1;
        private static int Sign(Tempo v) => (int)v - 1;
        private static int Sign(Width v) => (int)v - 1;

        /// <summary>Cyclic rock-paper-scissors: +1 if a beats b, -1 if a loses, 0 if equal. Rows sum to 0.</summary>
        private static int RpsWidth(int a, int b)
        {
            int d = (((a - b) % 3) + 3) % 3;
            return d == 2 ? 1 : (d == 1 ? -1 : 0);
        }

        /// <summary>Percent value -> multiplier in [Min,Max] (percent guardrails).</summary>
        private static double Clamp(int percent, TacticsBalance cfg)
        {
            int lo = cfg.MinMultiplierPercent, hi = cfg.MaxMultiplierPercent;
            int c = percent < lo ? lo : (percent > hi ? hi : percent);
            return c / 100.0;
        }
    }
}
