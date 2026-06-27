using System.Collections.Generic;
using Newtonsoft.Json;
using Sim.Core.Development;
using Sim.Core.Difficulty;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Tactics;

namespace Fts.Services
{
    /// <summary>
    /// The active career: the generated world (division pyramid) plus the
    /// user's club. Persisted as versioned gzip-JSON by ISaveRepository.
    /// </summary>
    public sealed class CareerState
    {
        /// <summary>
        /// Save format version. Bump on breaking shape changes; migrations
        /// live in LocalJsonSaveRepository. v3: Leagues list replaced the
        /// single League (second division added). v4: user tactic + per-tactic
        /// familiarity added (task 3.3); the user pre-match plan (task 3.5) also
        /// rode in additively here (null = none, no migration). v5: training added
        /// (task 4.3 — UserTraining + LastTrainingWeek; both additive, the migration
        /// only seeds LastTrainingWeek from the current day so no training is applied
        /// retroactively to an existing save). v6: development & ageing went live
        /// (task 4.4 — per-player minutes tracking: StartsSinceTraining +
        /// UserMatchesSinceTraining; both additive, old saves start a fresh window).
        /// </summary>
        /// v7 (task 4.5): player support actions — SupportCooldowns + RestedSinceTraining,
        /// both additive (old saves load with empty support state, nothing to regenerate).
        /// v8 (task 5.2b): AI transfer market went live — TransferNews + TransferWindowsRun,
        /// both additive. Club.TransferBudget rides Club serialization (no field here). The
        /// migration suppresses the market for the rest of the loaded season (sets
        /// TransferWindowsRun = done) so an old save doesn't get a jarring mid-season window;
        /// the market begins cleanly at the next season's start window.
        /// v9 (task 5.3): the user's own market UI — Shortlist + TransferList + IncomingOffers,
        /// all additive (old saves load with empty market state, nothing to regenerate). The
        /// user's executed deals are recorded into TransferNews too. TransferList/IncomingOffers
        /// are cleared at season rollover; Shortlist persists (it's a watch list).
        /// v10 (task 5.4b): scouting / knowledge layer — ScoutKnowledge (whole-world per-(club,player)
        /// knowledge) + ScoutAssignments (the user's watch list). Both additive; Club.Scouts rides
        /// Club serialization. Knowledge persists across seasons ("you don't forget what you scouted"),
        /// so neither is cleared at rollover. Old saves load with empty knowledge and start scouting fresh.
        /// v11 (task 5.5): club facilities & finances. Facilities (4 tiers) and Finances (cash balance +
        /// season income/expense) ride Club serialization — no field here — so an old save deserializes
        /// them as defaults (all-tier-1 / zero balance); the migration seeds the whole world's finances
        /// (starting balances, stadium tiers by strength, finance-based transfer budgets) and sets the
        /// user club's scouting tier from its existing scout department. Finances/facilities persist and
        /// evolve with the world; the transfer budget is re-seeded from finances each season.
        /// v12 (task 5.6): coach career — board objectives, confidence, reputation, sackings, job offers
        /// and the whole-world hiring carousel. Coach state (reputation/confidence/objective/last finish)
        /// rides Club.Coach serialization, so no field here for it; this version adds SeasonEvaluated (the
        /// idempotency guard for the season-end evaluation) and CareerHistory (the career-history screen).
        /// The migration seeds every coach (reputation/objective from squad strength), marks the user's
        /// coach human, and guarantees the history list — so an old save gets a coherent coaching world.
        /// v13 (task 5.7b): single-player difficulty — the chosen Difficulty (Easy/Normal/Hard) rides
        /// here. It's a fixed-for-the-save setting; an old save defaults to Normal (the field initializer
        /// supplies it when the key is absent), so the migration only bumps the version.
        /// v14 (task 6.2): Inbox notifications hub — a persisted list of InboxMessage with read/unread
        /// state, generated from events (transfers, market windows, match results, board). Additive
        /// (old saves load with an empty inbox, nothing to regenerate); guarantee the list is non-null.
        /// v15 (task 6.2): onboarding — OnboardingDone flag (the guided first-run tutorial shows once
        /// per career while false). Additive; the migration marks existing saves done so a veteran
        /// player isn't shown the tutorial, while a fresh career starts false and sees it.
        public int SaveVersion { get; set; } = 15;

        /// <summary>Seed used to generate the world (kept for debugging/replays).</summary>
        public ulong Seed { get; set; }

        public int UserClubId { get; set; }

        /// <summary>
        /// Single-player difficulty (task 5.7), chosen at career creation and fixed for the save.
        /// Drives AI lineup competence (the user's opponents field weaker XIs on Easy), the user's
        /// vs the AI's transfer budget, and the board's patience — never a cheating stat bonus.
        /// Defaults to Normal (also the value an old save without this field deserialises to).
        /// </summary>
        public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Normal;

        /// <summary>Creation timestamp, metadata only (never used by sim logic).</summary>
        public string CreatedUtc { get; set; } = string.Empty;

        /// <summary>All divisions, top first.</summary>
        public List<League> Leagues { get; set; } = new List<League>();

        public Season Season { get; set; } = new Season();

        /// <summary>The user's saved lineup; null = AI picks the best eleven.</summary>
        public LineupPlan UserLineup { get; set; }

        /// <summary>
        /// The user's chosen tactic; null = neutral 4-3-3, in which case no
        /// tactic is passed to the sim (so matches stay byte-identical to the
        /// pre-tactics behaviour until the user explicitly picks one).
        /// </summary>
        public TacticPlan UserTactic { get; set; }

        /// <summary>
        /// The user club's accumulated familiarity per tactic, keyed by
        /// <see cref="TacticPlan.Key"/>. Rises each match a tactic is used.
        /// </summary>
        public Dictionary<string, int> TacticFamiliarity { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// The user's conditional pre-match plan (task 3.5); null or empty = none,
        /// in which case no rules are passed to the sim and matches run exactly as
        /// before. When set, its rules execute automatically in the user's matches
        /// — including ones advanced past without watching (the AI-fallback path).
        /// </summary>
        public PrematchPlan UserPlan { get; set; }

        /// <summary>
        /// The user's weekly training plan (task 4.3): squad-wide team focus + optional
        /// per-player individual focuses. null = the balanced default (same as what AI
        /// clubs train). The user controls only his own club; the whole world develops.
        /// </summary>
        public TrainingPlan UserTraining { get; set; }

        /// <summary>
        /// The last training week already evolved (task 4.3), counted within the current
        /// season. Training fires once per calendar week (every SeasonBalance.DaysBetweenRounds
        /// days); this guards against double-applying a week and lets the clock catch up
        /// exactly one week at a time. Reset to 0 at season rollover so development keeps
        /// running every season (the per-week RNG is decorrelated by year in LocalClock).
        /// </summary>
        public int LastTrainingWeek { get; set; }

        /// <summary>
        /// Per-player count of user-club matches each player STARTED since the last training
        /// tick (task 4.4). Drives the weekly development "playing share" (youth need games):
        /// a regular starter develops at full speed, a benched player only at the minutes
        /// floor. Reset each training tick and at season rollover. Starters credited a full
        /// match, bench 0 (partial sub minutes aren't modelled in v1, like the condition model).
        /// </summary>
        public Dictionary<int, int> StartsSinceTraining { get; set; } = new Dictionary<int, int>();

        /// <summary>Number of user-club matches played since the last training tick (denominator for the playing share). Task 4.4.</summary>
        public int UserMatchesSinceTraining { get; set; }

        /// <summary>
        /// Support-action cooldowns (task 4.5): the season-local day each (player, action)
        /// pair was last used, keyed by "{playerId}:{(int)SupportAction}". Rehydrated into a
        /// Sim.Core SupportActionLog by the Support screen, which gates repeats until the
        /// action's cooldown elapses. Cleared at season rollover (cooldowns are short and the
        /// day counter resets each season), so a stored day never goes stale across seasons.
        /// </summary>
        public Dictionary<string, int> SupportCooldowns { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Player ids who were given a Rest support action since the last development tick
        /// (task 4.5). Honoured by LocalClock: a rested player's attributes are restored after
        /// the weekly development run, so Rest costs that week's training (its opportunity cost).
        /// Consumed and cleared each tick (and at season rollover), like the minutes window.
        /// </summary>
        public List<int> RestedSinceTraining { get; set; } = new List<int>();

        /// <summary>
        /// Completed AI transfers this season (task 5.2b), newest appended last, for the
        /// transfer-news feed the Inbox/market UI shows (task 5.3). Filled by
        /// <see cref="LocalMarketService"/> at each window and cleared at season rollover.
        /// The user's own deals (5.3) will be recorded here too once that UI lands.
        /// </summary>
        public List<Sim.Core.Market.TransferRecord> TransferNews { get; set; } = new List<Sim.Core.Market.TransferRecord>();

        /// <summary>
        /// How many transfer windows have already run this season (task 5.2b): 0 = none yet
        /// (the start-of-season window — which also seeds club budgets — is due), 1 = start done
        /// (the mid-season window is due once the calendar reaches halfway), 2 = both done.
        /// Reset to 0 at season rollover so each new season seeds budgets and trades afresh.
        /// </summary>
        public int TransferWindowsRun { get; set; }

        /// <summary>
        /// Player ids the user is watching on the market (task 5.3). A simple watch list shown
        /// flagged on the Buy tab and filterable; purely a convenience, never read by the sim.
        /// Persists across seasons (unlike listings/offers), so a target tracked all season stays.
        /// </summary>
        public List<int> Shortlist { get; set; } = new List<int>();

        /// <summary>
        /// User-club players currently up for sale with their asking price (task 5.3). Interested
        /// AI clubs bid against these during an open transfer window, producing <see cref="IncomingOffers"/>.
        /// Cleared at season rollover (a listing never carries across the break).
        /// </summary>
        public List<TransferListing> TransferList { get; set; } = new List<TransferListing>();

        /// <summary>
        /// Pending AI bids for the user's listed players (task 5.3), shown on the Market "Sell"
        /// tab for the user to accept or reject. Generated deterministically (no RNG) during open
        /// windows; cleared at season rollover with the listings.
        /// </summary>
        public List<IncomingOffer> IncomingOffers { get; set; } = new List<IncomingOffer>();

        /// <summary>
        /// Whole-world scouting knowledge (task 5.4b): each entry "{clubId}:{playerId}" → knowledge
        /// level [0, ScoutingBalance.MaxKnowledge]. Rehydrated into a Sim.Core KnowledgeStore by
        /// <see cref="ScoutingService"/>, advanced each scouting week by the ScoutingProgressor (the
        /// user club follows ScoutAssignments, AI clubs the default policy), then written back here.
        /// Persists across seasons — you don't forget what you scouted — so it's never cleared at
        /// rollover. Bounded: each club watches only a few players, so this stays small.
        /// </summary>
        public Dictionary<string, int> ScoutKnowledge { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// The user club's active scouting assignments (task 5.4b): player ids his scouts are
        /// watching. Drives weekly knowledge gain on those players (AI clubs use the default policy);
        /// the watch list itself persists across seasons. Capacity is bounded by the club's scouts
        /// in the Scouting screen.
        /// </summary>
        public List<int> ScoutAssignments { get; set; } = new List<int>();

        /// <summary>
        /// Whether the coach-career season-end evaluation (prize money, confidence/reputation moves,
        /// AI sackings &amp; hires) has already been applied for the CURRENT finished season (task 5.6).
        /// Set by <see cref="CareerService"/> when the season is evaluated and cleared once the rollover
        /// commits — so quitting on the season-end decision screen and reloading does NOT double-apply
        /// the deltas (the screen rebuilds its read-only report instead).
        /// </summary>
        public bool SeasonEvaluated { get; set; }

        /// <summary>
        /// The user's completed coaching seasons (task 5.6), oldest first, for the career-history
        /// screen. Appended at each season end before the rollover and any club move. Persists for
        /// the life of the career.
        /// </summary>
        public List<CareerHistoryEntry> CareerHistory { get; set; } = new List<CareerHistoryEntry>();

        /// <summary>
        /// The Inbox notifications hub (task 6.2): persisted messages with read/unread state, oldest
        /// first, posted from events (completed transfers, market windows, match results, board
        /// outcomes) by <see cref="InboxService"/>. Each message stores a localization key + args so
        /// it re-renders in the active language. Bounded (the service caps the list), so it stays small.
        /// Persists across seasons — it's the player's running record — so it isn't cleared at rollover.
        /// </summary>
        public List<InboxMessage> Inbox { get; set; } = new List<InboxMessage>();

        /// <summary>
        /// Whether the guided first-run tutorial (task 6.2) has been shown for this career. Starts
        /// false on a new career → the Hub runs the onboarding overlay once, then sets this true and
        /// saves. Existing saves are migrated to true so a returning player never sees the tutorial.
        /// </summary>
        public bool OnboardingDone { get; set; }

        /// <summary>Write-only adapter for v2 saves, which stored a single "League".</summary>
        [JsonProperty("League")]
        private League LegacyLeague
        {
            set
            {
                if (value != null)
                    Leagues = new List<League> { value };
            }
        }

        public Club GetUserClub() => FindClub(UserClubId);

        /// <summary>The division the user's club currently plays in.</summary>
        public League GetUserLeague()
        {
            foreach (League league in Leagues)
            {
                if (league.FindClub(UserClubId) != null)
                    return league;
            }

            return null;
        }

        public Club FindClub(int clubId)
        {
            foreach (League league in Leagues)
            {
                Club club = league.FindClub(clubId);
                if (club != null)
                    return club;
            }

            return null;
        }

        public Player FindPlayer(int playerId)
        {
            foreach (League league in Leagues)
            {
                Player player = league.FindPlayer(playerId);
                if (player != null)
                    return player;
            }

            return null;
        }
    }
}
