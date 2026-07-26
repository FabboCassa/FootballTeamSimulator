using Fts.Application.Ranked;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// The ladder's rating maths (Phase 9.3) — pure, static, no I/O, no state, so it can be unit-tested on its
/// own and reasoned about without a database.
///
/// Standard Elo: the expected score of A against B is 1 / (1 + 10^((Rb − Ra) / 400)), and a match moves the
/// rating by K × (actual − expected) with actual = 1 / 0.5 / 0 for win / draw / loss. Two ladder-specific
/// additions on top:
/// <list type="bullet">
/// <item>a <b>finishing bonus</b> at season end that scales linearly from +swing (1st) to −swing (last), so
/// where you finish matters and not only who you beat;</item>
/// <item>a <b>promotion/relegation adjustment</b>, because moving tier is the ladder's real progression.</item>
/// </list>
///
/// NOTE this is server-side ranking maths, NOT Sim.Core: it never feeds the match engine, so it is free to
/// use floating point (<see cref="Math.Pow"/>) — the golden master and the client/server byte-identity
/// guarantee are untouched. Results are rounded away from zero so a narrow loss always costs at least a
/// point (an update that rounds to 0 would let a coach farm draws forever at no risk).
/// </summary>
public static class EloModel
{
    /// <summary>The classic Elo scale constant: a 400-point gap means the favourite is expected to win ~91%.</summary>
    private const double Scale = 400.0;

    /// <summary>Expected score (0..1) for a coach rated <paramref name="rating"/> against
    /// <paramref name="opponentRating"/>.</summary>
    public static double Expected(int rating, int opponentRating) =>
        1.0 / (1.0 + Math.Pow(10.0, (opponentRating - rating) / Scale));

    /// <summary>The actual score of an outcome: win 1, draw ½, loss 0.</summary>
    public static double Score(RankedMatchOutcome outcome) => outcome switch
    {
        RankedMatchOutcome.Win => 1.0,
        RankedMatchOutcome.Draw => 0.5,
        _ => 0.0,
    };

    /// <summary>How much one match moves a coach's rating (can be negative). Rounded away from zero so a
    /// result is never worth literally nothing.</summary>
    public static int MatchDelta(int rating, int opponentRating, RankedMatchOutcome outcome, int kFactor)
    {
        double raw = kFactor * (Score(outcome) - Expected(rating, opponentRating));
        return RoundAwayFromZero(raw);
    }

    /// <summary>The finishing bonus/malus for ending a season in <paramref name="position"/> of
    /// <paramref name="groupSize"/>: +swing for 1st, −swing for last, linear in between (0 for the middle
    /// of an odd-sized group). A one-club group scores 0 — there is nobody to finish above.</summary>
    public static int SeasonEndDelta(int position, int groupSize, int swing)
    {
        if (groupSize <= 1 || position <= 0) return 0;
        int clamped = Math.Clamp(position, 1, groupSize);
        // (groupSize + 1 - 2·position) / (groupSize - 1) runs from +1 (1st) to -1 (last).
        double factor = (groupSize + 1 - 2.0 * clamped) / (groupSize - 1);
        return RoundAwayFromZero(swing * factor);
    }

    /// <summary>The rating adjustment for changing tier: + for a promotion, − for a relegation, 0 for staying.</summary>
    public static int TierMoveDelta(RankedTierMove move, int promotionBonus) => move switch
    {
        RankedTierMove.Promotion => promotionBonus,
        RankedTierMove.Relegation => -promotionBonus,
        _ => 0,
    };

    /// <summary>Applies a delta to a rating, never letting it fall below <paramref name="minRating"/>.</summary>
    public static int Apply(int rating, int delta, int minRating) => Math.Max(minRating, rating + delta);

    /// <summary>A match's outcome from the goals scored and conceded.</summary>
    public static RankedMatchOutcome OutcomeOf(int goalsFor, int goalsAgainst) =>
        goalsFor > goalsAgainst ? RankedMatchOutcome.Win
        : goalsFor < goalsAgainst ? RankedMatchOutcome.Loss
        : RankedMatchOutcome.Draw;

    private static int RoundAwayFromZero(double value)
    {
        int rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded != 0) return rounded;
        // Keep the sign of a sub-half movement so nothing is ever worth exactly nothing.
        if (value > 0) return 1;
        if (value < 0) return -1;
        return 0;
    }
}
