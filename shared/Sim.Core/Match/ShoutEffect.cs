using Sim.Core.Config;

namespace Sim.Core.Match
{
    /// <summary>
    /// What a touchline shout does to one side's movement tactics while it is heard. Offsets are
    /// added, percentages multiply; <see cref="None"/> is the identity, and every site that spends
    /// it does so as <c>x + 0</c> or <c>x * 100 / 100</c>, which is exact in integers — so a match
    /// with no shout is the match it always was.
    /// </summary>
    public readonly struct ShoutEffect
    {
        public readonly int LinePushDm;
        public readonly int FrontLineGapPercent;
        public readonly int Supporters;
        public readonly int PressReachPercent;
        public readonly int PressTriggerDepthDm;
        public readonly int SecondPressPercent;
        public readonly int PressStandOffPercent;
        public readonly int HoldPercent;
        public readonly int ForwardBias;
        public readonly int ShotAppetitePercent;
        public readonly int FatiguePercent;
        public readonly int PressureFeltPercent;
        public readonly int OwnHalfPressureFeltPercent;

        /// <summary>V11 only: percent on the weight the man on the ball puts on losing it.</summary>
        public readonly int RiskPercent;

        /// <summary>V11 only: percent on the weight he puts on the threat a move gains.</summary>
        public readonly int GainPercent;

        /// <summary>
        /// V11 only: percent on the odds a challenger takes the ball off one of this side's men,
        /// anywhere and in his own half.
        /// </summary>
        public readonly int DuelLossPercent;
        public readonly int OwnHalfDuelLossPercent;

        public static ShoutEffect None => new ShoutEffect(0, 100, 0, 100, 0, 100, 100, 100, 0, 100, 100, 100, 100);

        private ShoutEffect(
            int linePushDm, int frontLineGapPercent, int supporters, int pressReachPercent,
            int pressTriggerDepthDm, int secondPressPercent, int pressStandOffPercent, int holdPercent,
            int forwardBias, int shotAppetitePercent, int fatiguePercent, int pressureFeltPercent,
            int ownHalfPressureFeltPercent, int riskPercent = 100, int gainPercent = 100,
            int duelLossPercent = 100, int ownHalfDuelLossPercent = 100)
        {
            LinePushDm = linePushDm;
            FrontLineGapPercent = frontLineGapPercent;
            Supporters = supporters;
            PressReachPercent = pressReachPercent;
            PressTriggerDepthDm = pressTriggerDepthDm;
            SecondPressPercent = secondPressPercent;
            PressStandOffPercent = pressStandOffPercent;
            HoldPercent = holdPercent;
            ForwardBias = forwardBias;
            ShotAppetitePercent = shotAppetitePercent;
            FatiguePercent = fatiguePercent;
            PressureFeltPercent = pressureFeltPercent;
            OwnHalfPressureFeltPercent = ownHalfPressureFeltPercent;
            RiskPercent = riskPercent;
            GainPercent = gainPercent;
            DuelLossPercent = duelLossPercent;
            OwnHalfDuelLossPercent = ownHalfDuelLossPercent;
        }

        /// <summary>
        /// The effect of <paramref name="shout"/>, with <paramref name="repeats"/> earlier
        /// encouragements already heard (only Encourage reads it).
        /// </summary>
        public static ShoutEffect Of(TouchlineShout shout, int repeats, MatchBalance cfg)
        {
            ShoutBalance s = cfg.Shouts ?? new ShoutBalance();
            switch (shout)
            {
                case TouchlineShout.PressHigh:
                    return new ShoutEffect(0, 100, 0, s.PressHighReachPercent, s.PressHighTriggerDepthDm,
                        s.PressHighSecondPressPercent, s.PressHighStandOffPercent, 100, 0, 100,
                        cfg.Brain == MatchBrainVersion.V11 ? s.V11PressHighFatiguePercent : s.PressHighFatiguePercent,
                        100, 100);
                case TouchlineShout.KeepBall:
                    return new ShoutEffect(0, 100, 0, 100, 0, 100, 100, s.KeepBallHoldPercent,
                        s.KeepBallForwardBias, s.KeepBallShotAppetitePercent, 100, 100, 100,
                        s.KeepBallRiskPercent, s.KeepBallGainPercent, duelLossPercent: s.KeepBallDuelLossPercent);
                case TouchlineShout.AllForward:
                    return new ShoutEffect(s.AllForwardLinePushDm, s.AllForwardFrontLineGapPercent,
                        s.AllForwardSupporters, 100, 0, 100, 100, 100, 0, s.AllForwardShotAppetitePercent,
                        100, 100, 100);
                case TouchlineShout.Encourage:
                    return new ShoutEffect(0, 100, 0, 100, 0, 100, 100, 100, 0, 100, 100,
                        EncourageFelt(s, repeats), 100, s.EncourageRiskPercent,
                        duelLossPercent: EncourageDuelLoss(s, repeats));
                case TouchlineShout.Concentrate:
                    return new ShoutEffect(0, 100, 0, 100, 0, 100, 100, 100, s.ConcentrateForwardBias,
                        s.ConcentrateShotAppetitePercent, 100, 100, s.ConcentrateOwnHalfPressureFeltPercent,
                        s.ConcentrateRiskPercent, ownHalfDuelLossPercent: s.ConcentrateOwnHalfDuelLossPercent);
                default:
                    return None;
            }
        }

        /// <summary>The duel odds against an encouraged side, weakening with each repeat like the calm.</summary>
        private static int EncourageDuelLoss(ShoutBalance s, int repeats)
        {
            int gain = 100 - s.EncourageDuelLossPercent;
            for (int i = 0; i < repeats; i++) gain = gain * s.EncourageRepeatPercent / 100;
            return 100 - gain;
        }

        /// <summary>Each repeat keeps only EncourageRepeatPercent of the previous gain.</summary>
        private static int EncourageFelt(ShoutBalance s, int repeats)
        {
            int gain = 100 - s.EncouragePressureFeltPercent;
            for (int i = 0; i < repeats; i++) gain = gain * s.EncourageRepeatPercent / 100;
            return 100 - gain;
        }
    }
}
