namespace Fts.Application.Integrity;

/// <summary>How a fee compares to what the player is actually worth.</summary>
public enum TransferVerdict
{
    /// <summary>Within the normal negotiating range — nothing to see.</summary>
    Fair = 0,
    /// <summary>Allowed, but far enough from market value to be worth logging.</summary>
    Suspicious = 1,
    /// <summary>Refused: this is a gift (or a laundering payment), not a transfer.</summary>
    Blocked = 2,
}

/// <summary>The price bands, in percent of the player's market value. Infrastructure maps its
/// configuration onto this so the model itself stays free of config plumbing.</summary>
/// <param name="MinPercent">Below this share of market value a fee is refused (a gifted player).</param>
/// <param name="MaxPercent">Above this share a fee is refused (money shovelled to a friend).</param>
/// <param name="SuspiciousLowPercent">Below this (but above <paramref name="MinPercent"/>) → flagged.</param>
/// <param name="SuspiciousHighPercent">Above this (but below <paramref name="MaxPercent"/>) → flagged.</param>
/// <param name="MinValueChecked">Players cheaper than this are ignored: at squad-filler prices the
/// percentages swing wildly and there is nothing worth farming.</param>
public readonly record struct TransferBands(
    int MinPercent,
    int MaxPercent,
    int SuspiciousLowPercent,
    int SuspiciousHighPercent,
    long MinValueChecked);

/// <summary>The verdict plus the numbers that produced it (so the flag/error message can be specific).</summary>
public readonly record struct TransferAssessment(TransferVerdict Verdict, int FeePercentOfValue, string Reason)
{
    public bool IsBlocked => Verdict == TransferVerdict.Blocked;
    public bool IsSuspicious => Verdict == TransferVerdict.Suspicious;
}

/// <summary>
/// Collusion guard for coach-to-coach transfers (Phase 9.5). PURE and deterministic — integer/long maths,
/// no RNG, no I/O — so it can be unit-tested on its own and reused by any market surface.
///
/// The whole idea in one line: <b>a transfer must look like a transfer</b>. Two friends cannot move a star
/// for pocket change (boosting one squad for free) and cannot overpay wildly (moving a budget across
/// accounts). Fees far outside the band are refused outright; fees merely odd are allowed but flagged, so
/// an honest bargain is never blocked while a pattern of them stays visible.
///
/// Deliberately NOT symmetric with a real market: the bands are wide (a genuine haggle lands inside), and a
/// player with no meaningful valuation is skipped entirely rather than guessed at.
/// </summary>
public static class TransferIntegrity
{
    public static TransferAssessment Assess(long fee, long marketValue, TransferBands bands)
    {
        // No usable valuation, or a cheap squad filler: nothing to police.
        if (marketValue <= 0 || marketValue < bands.MinValueChecked)
            return new TransferAssessment(TransferVerdict.Fair, 100, "unpriced");

        if (fee <= 0)
            return new TransferAssessment(TransferVerdict.Blocked, 0, "free_transfer");

        int percent = PercentOfValue(fee, marketValue);

        if (percent < bands.MinPercent)
            return new TransferAssessment(TransferVerdict.Blocked, percent, "fee_far_below_value");
        if (percent > bands.MaxPercent)
            return new TransferAssessment(TransferVerdict.Blocked, percent, "fee_far_above_value");
        if (percent < bands.SuspiciousLowPercent)
            return new TransferAssessment(TransferVerdict.Suspicious, percent, "fee_below_value");
        if (percent > bands.SuspiciousHighPercent)
            return new TransferAssessment(TransferVerdict.Suspicious, percent, "fee_above_value");

        return new TransferAssessment(TransferVerdict.Fair, percent, "fair");
    }

    /// <summary>Fee as a percentage of market value, saturating instead of overflowing on absurd input.</summary>
    public static int PercentOfValue(long fee, long marketValue)
    {
        if (marketValue <= 0) return 0;
        long percent = fee / marketValue * 100 + fee % marketValue * 100 / marketValue;
        return percent > int.MaxValue ? int.MaxValue : (int)percent;
    }
}
