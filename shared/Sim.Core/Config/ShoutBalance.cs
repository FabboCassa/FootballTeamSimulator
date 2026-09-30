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

        /// <summary>Percent applied to the match fatigue a side accumulates while the shout lasts.</summary>
        public int V11PressHighFatiguePercent { get; set; } = 150;

        // --- "Calm, keep the ball": tempo down, risk down ---
        public int KeepBallHoldPercent { get; set; } = 140;
        public int KeepBallShotAppetitePercent { get; set; } = 98;

        /// <summary>
        /// V11 reads "risk down" off these: the weight on a ball that can be lost, and on the
        /// threat a move gains, in percent. V11 reads the hold at its tempo spread
        /// (MatchBalance.V11TempoSpreadPercent), so on V11 the risk is most of the shout.
        /// </summary>
        public int KeepBallRiskPercent { get; set; } = 250;
        public int KeepBallGainPercent { get; set; } = 40;

        /// <summary>V11: percent on the odds a challenger takes it off a man told to keep it (he shields it).</summary>
        public int KeepBallDuelLossPercent { get; set; } = 75;

        // --- "All forward!": mentality up, defensive exposure up ---
        public int AllForwardLinePushDm { get; set; } = 90;
        public int AllForwardSupporters { get; set; } = 1;
        public int AllForwardFrontLineGapPercent { get; set; } = 85;
        public int AllForwardShotAppetitePercent { get; set; } = 104;

        // --- "Encourage": composure up, weaker when repeated ---

        /// <summary>Percent of the pressure a man on the ball feels (lower = calmer).</summary>
        public int EncouragePressureFeltPercent { get; set; } = 80;

        /// <summary>V11: percent on the odds a challenger takes the ball off an encouraged man.</summary>
        public int EncourageDuelLossPercent { get; set; } = 70;

        /// <summary>
        /// V11: the weight an encouraged man puts on a ball that can be lost, in percent. Below 100
        /// he is bolder with it: the belief that wins him duels also has him try the ball he would
        /// not, which costs a side that has something to protect.
        /// </summary>
        public int EncourageRiskPercent { get; set; } = 70;

        /// <summary>What each repeat keeps of the previous encouragement's gain, in percent.</summary>
        public int EncourageRepeatPercent { get; set; } = 50;

        // --- "Concentrate": fewer defensive errors, attack slightly more cautious ---

        /// <summary>
        /// Percent of the pressure felt by a man on the ball in his own half. Not lower: the engine
        /// has no score-state behaviour, so a shout with no cost reads net-positive in every state
        /// (R11), and at 75 Concentrate did on most retunes of issue #65.
        /// </summary>
        public int ConcentrateOwnHalfPressureFeltPercent { get; set; } = 80;
        public int ConcentrateShotAppetitePercent { get; set; } = 99;

        /// <summary>V11: the weight on a ball that can be lost, in percent — the more careful side.</summary>
        public int ConcentrateRiskPercent { get; set; } = 250;

        /// <summary>V11: percent on the odds a challenger takes the ball off a man in his own half.</summary>
        public int ConcentrateOwnHalfDuelLossPercent { get; set; } = 70;
    }
}
