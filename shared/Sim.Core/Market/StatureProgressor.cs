using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Market
{
    /// <summary>
    /// Evolves a club's persistent <see cref="Club.Stature"/> at season end (task: season-end
    /// stature evolution, R5) — the stature counterpart of <see cref="Career.ReputationModel"/>.
    /// A season's outcome is judged the same way the board judges a coach: finish vs the
    /// expected league position, plus promotion/relegation and an outright title. The move is
    /// capped per season (<see cref="StatureBalance.MaxDeltaPerSeason"/>) so stature only ever
    /// drifts, never jumps.
    ///
    /// PURE and deterministic — integer math, NO RNG, no shared RNG stream consumed. Opt-in by
    /// being called: nothing in the match engine, <c>SeasonProgressor</c> or generation calls it,
    /// so the golden master and every existing determinism test are unaffected. The host (client
    /// SeasonService/CareerService) calls it once per finished season, per club, alongside the
    /// existing board/reputation evaluation — that wiring is a separate task.
    /// </summary>
    public sealed class StatureProgressor
    {
        private readonly StatureBalance _cfg;

        public StatureProgressor(BalanceConfig? config = null)
        {
            _cfg = (config ?? new BalanceConfig()).Stature;
        }

        /// <summary>
        /// The stature change a finished season earns: per position vs the expected finish, plus a
        /// title bonus for winning the division outright (position 1), plus/minus a flat
        /// promotion/relegation adjustment, capped at ±<see cref="StatureBalance.MaxDeltaPerSeason"/>.
        /// </summary>
        public int SeasonEndDelta(int actualPosition, int expectedPosition, bool promoted, bool relegated)
        {
            int gap = expectedPosition - actualPosition; // >0 = better than expected
            int delta = gap * _cfg.DeltaPerPositionVsExpectation;

            if (actualPosition == 1) delta += _cfg.TitleBonus;
            if (promoted) delta += _cfg.PromotionBonus;
            if (relegated) delta -= _cfg.RelegationPenalty;

            int max = _cfg.MaxDeltaPerSeason;
            if (delta > max) delta = max;
            if (delta < -max) delta = -max;
            return delta;
        }

        /// <summary>
        /// Applies one season-end stature change to <paramref name="club"/>, clamped to the
        /// [0, 100] stature range. Call once per club, once per finished season — never mid-season.
        /// </summary>
        public void ApplySeasonEnd(Club club, int actualPosition, int expectedPosition, bool promoted, bool relegated)
        {
            int stature = club.Stature + SeasonEndDelta(actualPosition, expectedPosition, promoted, relegated);
            if (stature < 0) stature = 0;
            if (stature > 100) stature = 100;
            club.Stature = stature;
        }
    }
}
