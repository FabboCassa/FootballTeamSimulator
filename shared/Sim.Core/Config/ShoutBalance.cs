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
        public int PressHighFatiguePercent { get; set; } = 140;

        // --- "Calm, keep the ball": tempo down, risk down ---
        public int KeepBallHoldPercent { get; set; } = 140;
        public int KeepBallForwardBias { get; set; } = -4;
        public int KeepBallShotAppetitePercent { get; set; } = 98;

        // --- "All forward!": mentality up, defensive exposure up ---
        public int AllForwardLinePushDm { get; set; } = 90;
        public int AllForwardSupporters { get; set; } = 1;
        public int AllForwardFrontLineGapPercent { get; set; } = 85;
        public int AllForwardShotAppetitePercent { get; set; } = 104;

        // --- "Encourage": composure up, weaker when repeated ---

        /// <summary>Percent of the pressure a man on the ball feels (lower = calmer).</summary>
        public int EncouragePressureFeltPercent { get; set; } = 80;

        /// <summary>What each repeat keeps of the previous encouragement's gain, in percent.</summary>
        public int EncourageRepeatPercent { get; set; } = 50;

        // --- "Concentrate": fewer defensive errors, attack slightly more cautious ---

        /// <summary>Percent of the pressure felt by a man on the ball in his own half.</summary>
        public int ConcentrateOwnHalfPressureFeltPercent { get; set; } = 75;
        public int ConcentrateShotAppetitePercent { get; set; } = 99;
        public int ConcentrateForwardBias { get; set; } = -2;
    }
}
