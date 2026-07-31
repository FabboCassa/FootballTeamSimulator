using Fts.Application.Integrity;

namespace Fts.Infrastructure.Integrity;

/// <summary>
/// Abuse &amp; integrity knobs (Phase 9.5), bound by hand from the "Integrity" configuration section (same
/// convention as <c>RankedOptions</c>/<c>JwtOptions</c> — this is a plain class library, no config-binder
/// dependency). Every guard in the phase reads its numbers from here, so tightening the ladder after a
/// live season is a config push, not a code change.
///
/// Defaults are deliberately GENEROUS. The aim is to make farming pointless, not to referee honest play:
/// a hard block only fires on fees that could not come out of a real negotiation, and the multi-account
/// heuristics separate rather than punish.
/// </summary>
public sealed class IntegrityOptions
{
    public const string SectionName = "Integrity";

    // --- transfer price bands (collusion) -------------------------------------------------------

    /// <summary>Refuse a transfer whose fee is below this percentage of the player's market value — the
    /// "gifted star" case. 40% still leaves room for a genuinely desperate seller.</summary>
    public int MinFeePercentOfValue { get; set; } = 40;

    /// <summary>Refuse a transfer whose fee is above this percentage of market value — the "shovel the
    /// budget to a friend" case. 250% is well above any sane premium for a wanted player.</summary>
    public int MaxFeePercentOfValue { get; set; } = 250;

    /// <summary>Allowed, but flagged for review, below this percentage.</summary>
    public int SuspiciousLowPercentOfValue { get; set; } = 65;

    /// <summary>Allowed, but flagged for review, above this percentage.</summary>
    public int SuspiciousHighPercentOfValue { get; set; } = 160;

    /// <summary>Players valued below this are not policed at all: at filler prices the percentages swing
    /// wildly and there is nothing worth farming.</summary>
    public long MinPlayerValueChecked { get; set; } = 250_000;

    /// <summary>How many completed transfers between the SAME two coaches inside one group before the pair
    /// is flagged. Two is a market; four is an arrangement.</summary>
    public int RepeatedTradesPerPairThreshold { get; set; } = 3;

    // --- multi-account heuristics ---------------------------------------------------------------

    /// <summary>Master switch for the link heuristics (signal capture + the enrolment guard).</summary>
    public bool EnableMultiAccountHeuristics { get; set; } = true;

    /// <summary>Points for two accounts seen from the same hashed address.</summary>
    public int SharedAddressPoints { get; set; } = 50;

    /// <summary>Points for two accounts seen from the same device id (much stronger than an address).</summary>
    public int SharedDevicePoints { get; set; } = 80;

    /// <summary>Corroboration points when the two accounts were registered within
    /// <see cref="CreatedTogetherMinutes"/> of each other AND already share an address or device.</summary>
    public int CreatedTogetherPoints { get; set; } = 20;

    public int CreatedTogetherMinutes { get; set; } = 60;

    /// <summary>Score at or above which two accounts are kept out of the same ranked group.</summary>
    public int LinkScoreThreshold { get; set; } = 60;

    /// <summary>Salt mixed into the address/device hashes. Override per environment; rotating it makes the
    /// stored fingerprints uncorrelatable with anything recorded before.</summary>
    public string SignalSalt { get; set; } = "fts-integrity-dev-salt";

    /// <summary>Do not rewrite an account's signal row more often than this (it is touched on every ranked
    /// request; without a cooldown that is a pointless write per call).</summary>
    public int SignalRefreshMinutes { get; set; } = 30;

    // --- reports ---------------------------------------------------------------------------------

    /// <summary>How many reports one account may file per day (a report button is itself a harassment
    /// vector; this caps it without needing moderation).</summary>
    public int MaxReportsPerDay { get; set; } = 10;

    /// <summary>Free-text detail is truncated to this many characters before it is stored.</summary>
    public int MaxReportDetailsLength { get; set; } = 280;

    // --- input deadlines -------------------------------------------------------------------------

    /// <summary>Refuse lineup submissions once the next matchday's kickoff has arrived. The ladder is
    /// polled by a minutely job, so without this a coach could keep submitting for a matchday that is
    /// already due and effectively pick their team after seeing the day's other results.</summary>
    public bool EnforceLineupDeadline { get; set; } = true;

    /// <summary>Extra lock-out ahead of kickoff, in seconds (0 = the deadline is kickoff itself).</summary>
    public int LineupLockSeconds { get; set; } = 0;

    // --- rate limits -----------------------------------------------------------------------------

    /// <summary>Master switch for the per-account rate limits on the ranked write endpoints.</summary>
    public bool EnableRateLimiting { get; set; } = true;

    /// <summary>Window length for both rate-limit buckets.</summary>
    public int RateWindowSeconds { get; set; } = 10;

    /// <summary>Ranked writes (lineup, training, confirm, offers, enrol…) allowed per window per account.</summary>
    public int WritesPerWindow { get; set; } = 30;

    /// <summary>Auction bids allowed per window per account — the bucket that stops bid spam.</summary>
    public int BidsPerWindow { get; set; } = 10;

    /// <summary>Reports allowed per window per account (the daily cap is separate).</summary>
    public int ReportsPerWindow { get; set; } = 3;

    /// <summary>The transfer price bands as the pure model wants them.</summary>
    public TransferBands Bands() => new(
        MinPercent: MinFeePercentOfValue,
        MaxPercent: MaxFeePercentOfValue,
        SuspiciousLowPercent: SuspiciousLowPercentOfValue,
        SuspiciousHighPercent: SuspiciousHighPercentOfValue,
        MinValueChecked: MinPlayerValueChecked);

    /// <summary>The link weights as the pure model wants them.</summary>
    public LinkWeights Weights() => new(
        SharedAddressPoints: SharedAddressPoints,
        SharedDevicePoints: SharedDevicePoints,
        CreatedTogetherPoints: CreatedTogetherPoints,
        CreatedTogetherMinutes: CreatedTogetherMinutes,
        Threshold: LinkScoreThreshold);
}
