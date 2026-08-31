namespace Fts.Infrastructure.Ranked;

/// <summary>
/// Shape of a public-ranked world (Phase 9.1), bound by hand from the "Ranked" configuration section
/// (like <c>JwtOptions</c>/<c>FcmOptions</c> — this is a plain class library, no config-binder dependency).
///
/// The defaults are the agreed pyramid: <b>3 tiers, 8 clubs per group</b> — tier 1 one group, tier 2 two
/// groups, tier 3 four groups = 56 seats per world, of which 48 are placeable (tier 1 is reached by
/// promotion only). Small groups keep a season short (14 matchdays), which suits the ≤10-minute daily
/// loop the ladder is designed around.
///
/// Tests shrink these so spillover to a second world can be exercised without hundreds of accounts.
/// </summary>
public sealed class RankedOptions
{
    public const string SectionName = "Ranked";

    /// <summary>Clubs per group — also the number of clubs generated for the group's world.</summary>
    public int GroupSize { get; set; } = 8;

    /// <summary>Coaches per placement season (kept equal to <see cref="GroupSize"/> by default so a
    /// placement cohort maps 1:1 onto a group's worth of seats).</summary>
    public int PlacementGroupSize { get; set; } = 8;

    /// <summary>Groups in the top tier.</summary>
    public int Tier1Groups { get; set; } = 1;

    /// <summary>Groups in the second tier.</summary>
    public int Tier2Groups { get; set; } = 2;

    /// <summary>Groups in the third tier.</summary>
    public int Tier3Groups { get; set; } = 4;

    /// <summary>How many placement finishers earn the higher placeable tier; everyone below drops to the
    /// lowest tier. Tier 1 is never handed out by placement — it is earned by promotion.</summary>
    public int PlacementTopPositionsToUpperTier { get; set; } = 2;

    /// <summary>Ladder rating a freshly placed coach starts from.</summary>
    public int StartingRating { get; set; } = 1000;

    /// <summary>Rating spread per placement position (1st in a group of 8 starts this × 7 above last).</summary>
    public int RatingPerPlacementPosition { get; set; } = 15;

    // --- Real-time calendar (Phase 9.2) --------------------------------------------------------

    /// <summary>Wall-clock gap between consecutive matchdays (default 1 day = 86400s → a 14-matchday season
    /// runs in ~2 weeks). Tests compress this to 0 so a whole season resolves in a few ticks.</summary>
    public int MatchdayIntervalSeconds { get; set; } = 86_400;

    /// <summary>How long a market window stays open (default 1 day). Window 0 opens at season start (the
    /// lead-in before matchday 1); window 1 opens around the season midpoint. The market CONTENT
    /// (free-agent auctions + direct coach offers) is wired in 9.2b — 9.2a fires the windows on the
    /// calendar and announces them.</summary>
    public int MarketWindowDurationSeconds { get; set; } = 86_400;

    /// <summary>Safety valve for a large live ladder (Phase 9.6): the most matchdays a single calendar tick
    /// may resolve. 0 (the default) means no cap — the behaviour the calendar has always had. Raising a cap
    /// bounds how long one run can take; the groups it skips resolve on the next run a minute later, which
    /// costs nothing when kickoffs are a day apart. Leave it at 0 unless a tick starts crowding its own
    /// schedule.</summary>
    public int MaxMatchdaysPerTick { get; set; } = 0;

    // --- Ranked market economy (Phase 9.2b) ----------------------------------------------------

    /// <summary>Transfer budget every ranked club is seeded with when its season starts (default 25M) —
    /// the money a coach spends on direct offers to other coaches during a market window.</summary>
    public long StartingTransferBudget { get; set; } = 25_000_000;

    /// <summary>A coach cannot sell below this squad size — a floor so a direct offer can never gut a
    /// team down to an unplayable size.</summary>
    public int MinSquadSizeForSale { get; set; } = 16;

    /// <summary>
    /// Opening price of EVERY auction lot in the ladder (default 1M): the same for a phenom and for a
    /// squad filler, so with equal budgets anyone can bid on anyone and the auction — not the valuation
    /// model — decides what a player is worth. The balance harness is what argued for it: priced at half
    /// of market value, a ranked world's best player asks more than six times the whole kitty, so the top
    /// of the market was decoration. Set to 0 to fall back on the value-based opening price the private
    /// leagues use.
    /// </summary>
    public long AuctionFlatStartPrice { get; set; } = 1_000_000;

    // --- Auction timers & seller lots (Phase 12.2) ---------------------------------------------

    /// <summary>
    /// How long an auto-opened FREE-AGENT lot runs (default 24h). Until 12.2 this was implicit — every lot
    /// of a window ended when the window did — and pulling it out here is what makes the two kinds of lot
    /// share one mechanism: a duration, clamped to the window's close. With the default window also a day
    /// long the behaviour is byte-for-byte what 9.2b shipped.
    /// </summary>
    public int AuctionLotSeconds { get; set; } = 86_400;

    /// <summary>Shortest timer a coach may put on his OWN lot (default 1h). Anything below is refused —
    /// a lot nobody can realistically see is not an auction.</summary>
    public int SellerLotMinSeconds { get; set; } = 3_600;

    /// <summary>Longest timer a coach may put on his own lot (default 24h). Anything above is refused; the
    /// chosen duration is then clamped to the market window's close, so no lot outlives the market.</summary>
    public int SellerLotMaxSeconds { get; set; } = 86_400;

    /// <summary>
    /// A bid landing within this many seconds of a lot's end pushes the end to now + this (default 30s) —
    /// the private leagues' anti-snipe rule, brought to the ladder now that lots have their own timers.
    /// It extends THAT lot only. Tests raise it so the extension is observable without waiting out a timer.
    /// </summary>
    public int AuctionAntiSnipeSeconds { get; set; } = 30;

    /// <summary>
    /// Whether the group's AI clubs (vacant seats) bid on the lots coaches put up. On by default: a ladder
    /// group is mostly AI early on, and a sell flow that only works when another human happens to be online
    /// is not a sell flow. They never bid on free-agent lots — that board belongs to the coaches.
    /// </summary>
    public bool AiBidsOnSellerLots { get; set; } = true;

    /// <summary>The most an AI club will pay for a listed player, as a percentage of his market value
    /// (default 120%). Inside the 9.5 integrity band by construction, so a bot can never make a deal the
    /// guards would refuse between two humans.</summary>
    public int AiSellerLotMaxPercentOfValue { get; set; } = 120;

    // --- Live ranked matches & the visible kick-off time (task 12.3) ---------------------------

    /// <summary>
    /// The IANA time zone a freshly opened ranked world belongs to (default Europe/Rome). A world kicks off
    /// at <see cref="KickoffHourLocal"/> of THIS zone and every client renders that instant in the device's
    /// own local time — so an Italian world's 21:00 reads 20:00 in London, and a US world opened later runs
    /// on US evenings without anyone being asked to turn up at 05:00. Stored as a zone rather than a fixed
    /// offset because that is the only way a season keeps kicking off at 21:00 across a DST change.
    /// </summary>
    public string WorldTimeZone { get; set; } = "Europe/Rome";

    /// <summary>The local hour (0-23) a matchday kicks off at in its world's zone. The anchoring only
    /// engages on a whole-day matchday cadence — see <c>RankedCalendar.KickoffClock</c> — so a compressed
    /// test calendar keeps the pre-12.3 season-start-relative kickoffs.</summary>
    public int KickoffHourLocal { get; set; } = 21;

    /// <summary>How long before kickoff a coach may open his live session (default 15 min): the lobby where
    /// he waits out the countdown, and also when the "your match is about to start" push fires.</summary>
    public int LiveOpensBeforeSeconds { get; set; } = 900;

    /// <summary>
    /// How long after kickoff the calendar will WAIT for a live match before resolving the matchday headless
    /// (default 10 min). A live 90' runs at <see cref="LiveSecondsPerMatchMinute"/>, i.e. three real minutes,
    /// so this is generous — it exists so a stalled client can never hold a whole group's matchday hostage.
    /// The wait only happens when a session for that round actually exists and is unfinished: a matchday
    /// nobody turned up for resolves at its kickoff exactly as it did before 12.3.
    /// </summary>
    public int LiveGraceSeconds { get; set; } = 600;

    /// <summary>Real seconds per match minute during a live match (default 2 ⇒ a 90' takes three minutes).
    /// The SERVER owns this number now: the client renders on it AND the server uses it to check that a
    /// pause-point change is not being made in the match's future (see
    /// <see cref="LiveChangeMinuteTolerance"/>).</summary>
    public int LiveSecondsPerMatchMinute { get; set; } = 2;

    /// <summary>
    /// How many match minutes of slack the server allows a pause-point change beyond the minute the wall
    /// clock says has been played (default 5). Without a check the whole 90' is in the pushed report, so a
    /// doctored client could read the ending and then "substitute" at minute 10 with hindsight — a private
    /// league is a lobby of friends, the ladder is ranked. The tolerance absorbs latency and clock skew;
    /// 0 disables the check.
    /// </summary>
    public int LiveChangeMinuteTolerance { get; set; } = 5;

    /// <summary>Master switch for attending a ranked match (task 12.3). Off ⇒ every matchday resolves
    /// headless on the tick exactly as it did in 9.2, and the live endpoints refuse.</summary>
    public bool LiveMatchesEnabled { get; set; } = true;

    // --- Coach ranking & seasonal reset (Phase 9.3) --------------------------------------------

    /// <summary>Elo K-factor: the most a single matchday can move a rating. 24 keeps a 14-matchday season
    /// worth at most ~±170 before the finishing bonus — responsive without being swingy.</summary>
    public int EloKFactor { get; set; } = 24;

    /// <summary>Rating a vacant (AI) seat plays at in the TOP tier. Lower tiers subtract
    /// <see cref="AiRatingPerTierStep"/> per tier, so beating an AI club is worth more the higher you are.
    /// Without this a division with a single human coach would never move its rating in-season.</summary>
    public int AiRatingTopTier { get; set; } = 1150;

    /// <summary>How much lower an AI seat plays per tier below the top.</summary>
    public int AiRatingPerTierStep { get; set; } = 100;

    /// <summary>Season-end swing: the winner of a group gains this, the last-placed loses it, everyone
    /// in between scales linearly. Applied on top of the per-matchday Elo.</summary>
    public int SeasonEndPositionSwing { get; set; } = 40;

    /// <summary>Extra rating for a promotion (and the same amount subtracted for a relegation).</summary>
    public int PromotionRatingBonus { get; set; } = 60;

    /// <summary>How many coaches go up from a group at the end of its season (tier 1 has nowhere to go).</summary>
    public int PromotionSlots { get; set; } = 1;

    /// <summary>How many coaches go down (the lowest tier has nowhere to go).</summary>
    public int RelegationSlots { get; set; } = 1;

    /// <summary>Rating floor — a coach can never be rated below this however bad the run.</summary>
    public int MinRating { get; set; } = 100;

    /// <summary>The break between two seasons (default 1 week). A finished group keeps its final table
    /// readable for this long — coaches can look at the season they just played and at their palmarès — and
    /// only then are promotions/relegations applied and the squads reset. Tests set it to 0.</summary>
    public int SeasonBreakSeconds { get; set; } = 604_800;

    /// <summary>Reset the group's squads at season end (equalise, neutral condition, fresh budgets) so the
    /// next season starts fair and the new market window re-auctions the free agents. Turning this off keeps
    /// squads across seasons — kept as a switch, the ladder default is on.</summary>
    public bool ResetSquadsBetweenSeasons { get; set; } = true;

    /// <summary>Squad size every club is trimmed back to at the seasonal reset: whatever a club accumulated
    /// through the market beyond this is RELEASED back to free agency, which both keeps squads a fixed size
    /// across seasons and refills the pool the next season's windows auction. 0 disables the trim.</summary>
    public int SeasonResetSquadSize { get; set; } = 22;

    /// <summary>Default page size of the global leaderboard.</summary>
    public int LeaderboardTopCount { get; set; } = 50;

    /// <summary>Rating an AI-held seat plays at in the given tier (clamped at <see cref="MinRating"/>).</summary>
    public int AiRatingForTier(int tier)
    {
        int steps = Math.Max(0, tier - 1);
        return Math.Max(MinRating, AiRatingTopTier - steps * AiRatingPerTierStep);
    }

    /// <summary>Group counts per tier, index 0 = tier 1. Only non-empty tiers are kept.</summary>
    public IReadOnlyList<int> GroupsPerTier()
    {
        var tiers = new List<int>();
        if (Tier1Groups > 0) tiers.Add(Tier1Groups);
        if (Tier2Groups > 0) tiers.Add(Tier2Groups);
        if (Tier3Groups > 0) tiers.Add(Tier3Groups);
        if (tiers.Count == 0) tiers.Add(1);
        return tiers;
    }

    /// <summary>Number of tiers in the pyramid.</summary>
    public int TierCount() => GroupsPerTier().Count;

    /// <summary>The best tier a placement season can hand out: the second tier when the pyramid has one
    /// (tier 1 stays promotion-only), otherwise the only tier there is.</summary>
    public int UpperPlacementTier() => TierCount() > 1 ? 2 : 1;

    /// <summary>The tier everyone who did not make <see cref="UpperPlacementTier"/> is sorted into.</summary>
    public int LowerPlacementTier() => TierCount();
}
