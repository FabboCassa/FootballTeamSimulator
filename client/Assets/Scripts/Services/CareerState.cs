using System.Collections.Generic;
using Newtonsoft.Json;
using Sim.Core.Development;
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
        public int SaveVersion { get; set; } = 7;

        /// <summary>Seed used to generate the world (kept for debugging/replays).</summary>
        public ulong Seed { get; set; }

        public int UserClubId { get; set; }

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
