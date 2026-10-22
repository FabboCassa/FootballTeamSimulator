namespace Sim.Core.Config
{
    /// <summary>
    /// Touchline shouts (watchable-match spec R11). Each shout moves a handful of the movement
    /// tactics' levers for <see cref="DurationMinutes"/>; percentages read 100 and offsets read 0
    /// as "no change". The magnitudes are starting points for the tactic tournament (#37) and the
    /// tuning pass (#38), not measured values yet.
    /// </summary>
    public sealed class ShoutBalance
    {
        /// <summary>How long a shout is heard, in match minutes.</summary>
        public int DurationMinutes { get; set; } = 10;

        /// <summary>
        /// How long after a shout the same bench is heard again, in match minutes. One voice per
        /// bench: any shout, not just the same one, waits out the cooldown.
        /// </summary>
        public int CooldownMinutes { get; set; } = 15;

        // --- "Press high!": pressing up, fatigue up ---
        public int PressHighReachPercent { get; set; } = 125;
        public int PressHighTriggerDepthDm { get; set; } = 120;
        public int PressHighSecondPressPercent { get; set; } = 140;
        public int PressHighStandOffPercent { get; set; } = 85;

        /// <summary>
        /// Percent on how close the pressers get before they challenge (see V11PressingChallengePercent).
        /// 160 since issue #87: with the challenge radius cut to 1.6 m, the shout's other knobs alone
        /// moved its high recoveries by 0% (R11 wants at least +15%).
        /// </summary>
        public int PressHighChallengePercent { get; set; } = 160;

        /// <summary>
        /// Percent applied to the match fatigue a side accumulates while the shout lasts. 220 since
        /// issue #87 (was 150): with the pressers going in from closer the shout won the ball back
        /// twice as often for nothing and read net-positive in every score state.
        /// </summary>
        public int V11PressHighFatiguePercent { get; set; } = 220;

        // --- "Calm, keep the ball": tempo down, risk down ---
        public int KeepBallHoldPercent { get; set; } = 140;
        // 85 since issue #87 (was 98), with the risk and shield figures below: a side told to keep
        // it shoots less, so the shout costs the side that needs a goal (R11: never net-positive
        // in every score state).
        public int KeepBallShotAppetitePercent { get; set; } = 85;

        /// <summary>
        /// V11 reads "risk down" off these: the weight on a ball that can be lost, and on the
        /// threat a move gains, in percent. V11 reads the hold at its tempo spread
        /// (MatchBalance.V11TempoSpreadPercent), so on V11 the risk is most of the shout.
        /// 150 and 70 since issue #87 (were 250 and 40), with the shield at 95 (was 75): with the
        /// ball changing hands a third as often, a side told to keep it lost nothing by it and the
        /// shout read net-positive in every score state.
        /// </summary>
        public int KeepBallRiskPercent { get; set; } = 150;
        public int KeepBallGainPercent { get; set; } = 70;

        /// <summary>V11: percent on the odds a challenger takes it off a man told to keep it (he shields it).</summary>
        public int KeepBallDuelLossPercent { get; set; } = 95;

        // --- "All forward!": mentality up, defensive exposure up ---
        public int AllForwardLinePushDm { get; set; } = 90;
        public int AllForwardSupporters { get; set; } = 1;
        public int AllForwardFrontLineGapPercent { get; set; } = 85;
        // 130 since issue #87 (was 104): with fewer bodies cleared out of the box the shout's push
        // alone read fewer shots (R11 wants more; +0.7% at 120).
        public int AllForwardShotAppetitePercent { get; set; } = 130;

        // --- "Encourage": composure up, weaker when repeated ---

        /// <summary>Percent of the pressure a man on the ball feels (lower = calmer).</summary>
        public int EncouragePressureFeltPercent { get; set; } = 80;

        /// <summary>V11: percent on the odds a challenger takes the ball off an encouraged man.</summary>
        public int EncourageDuelLossPercent { get; set; } = 70;

        /// <summary>
        /// V11: the weight an encouraged man puts on a ball that can be lost, in percent. Below 100
        /// he is bolder with it: the belief that wins him duels also has him try the ball he would
        /// not, which costs a side that has something to protect. 55 since issue #87 (was 70): with
        /// fewer duels his calm cost nothing and the shout read net-positive in every score state.
        /// </summary>
        public int EncourageRiskPercent { get; set; } = 55;

        /// <summary>What each repeat keeps of the previous encouragement's gain, in percent.</summary>
        public int EncourageRepeatPercent { get; set; } = 50;

        // --- "Concentrate": fewer defensive errors, attack slightly more cautious ---

        /// <summary>
        /// Percent of the pressure felt by a man on the ball in his own half. Not lower: the engine
        /// has no score-state behaviour, so a shout with no cost reads net-positive in every state
        /// (R11), and at 75 Concentrate did on most retunes of issue #65. 95 since issue #87, with
        /// the risk at 130 (was 250) and the own-half shield at 90 (was 70): with fewer duels the
        /// old figures halved the own-half losses for nothing, net-positive in every score state.
        /// </summary>
        public int ConcentrateOwnHalfPressureFeltPercent { get; set; } = 95;
        // 85 since issue #87 (was 99): the attack more cautious, for the same reason as KeepBall's.
        public int ConcentrateShotAppetitePercent { get; set; } = 85;

        /// <summary>V11: the weight on a ball that can be lost, in percent — the more careful side.</summary>
        public int ConcentrateRiskPercent { get; set; } = 130;

        /// <summary>V11: percent on the odds a challenger takes the ball off a man in his own half.</summary>
        public int ConcentrateOwnHalfDuelLossPercent { get; set; } = 90;
    }
}
