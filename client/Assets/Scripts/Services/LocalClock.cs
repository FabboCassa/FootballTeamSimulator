using System.Collections.Generic;
using System.Text;
using Fts.Services.Messaging;
using Fts.Services.Persistence;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Difficulty;
using Sim.Core.Domain;
using Sim.Core.Market;
using Sim.Core.Match;
using Sim.Core.Tactics;
using UnityEngine;

namespace Fts.Services
{
    /// <summary>
    /// Single-player clock: advances the career one day per call, simulating
    /// due fixtures headless (Sim.Core SeasonProgressor) with the user's saved
    /// lineup when set, autosaving, and publishing DayAdvancedMessage.
    /// Lives in the Game scope.
    /// </summary>
    public sealed class LocalClock : IGameClock
    {
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly IMessageBroker _broker;
        private readonly UserMatchLog _matchLog;
        private readonly UserMatchContextHolder _matchContext;
        private readonly LocalMarketService _market;
        private readonly ScoutingService _scouting;
        private readonly InboxService _inbox;
        // Condition is live (task 4.2): matches are simulated condition-aware and the
        // whole world's form/morale/fitness evolves each day via EvolveCondition below.
        // applyMatchFatigue also fades each side within the match (by avg stamina) with
        // a half-time recovery, so the fresher/better-conditioned side gains late.
        // applyPositioning (task 6.10): the user's custom on-pitch shape tilts his side's
        // ratings. AI clubs field clean best-XI presets (no custom positions) → identity, so
        // their matches stay byte-identical; only the user's shape is affected.
        private readonly SeasonProgressor _progressor = new SeasonProgressor(applyCondition: true, applyMatchFatigue: true, applyPositioning: true);
        private readonly TacticsBalance _tacticsConfig = new BalanceConfig().Tactics;
        // Development & ageing is live (task 4.4, superseding the 4.3 training-only tick):
        // every week the WHOLE world develops AND ages — the user club follows its chosen
        // plan with real per-player minutes (youth who play grow faster), every other club
        // develops the balanced AI default at neutral minutes. The match engine never reads
        // development, so this is opt-in and golden masters stay byte-identical.
        private readonly DevelopmentProgressor _development = new DevelopmentProgressor(new BalanceConfig().Development);
        private readonly DevelopmentBalance _developmentConfig = new BalanceConfig().Development;
        private readonly int _trainingPeriodDays = new BalanceConfig().Season.DaysBetweenRounds;
        // Market values are re-priced once per training week (task 5.1): the cached figure
        // tracks development/ageing and contract decay without flickering with daily form.
        // Pure/deterministic, no RNG, never read by the engine — golden masters stay safe.
        private readonly ValuationProgressor _valuation = new ValuationProgressor(new BalanceConfig());
        // Whole-world finances are live (task 5.5): gate receipts credit the home club each
        // matchday, sponsors and wages settle on the weekly tick (the board floors the balance so
        // bankruptcy is impossible), and the training facility tier feeds the development model.
        // Pure/integer, no RNG, never read by the engine — golden masters stay safe.
        private readonly BalanceConfig _config = new BalanceConfig();
        private readonly FinanceProgressor _finance = new FinanceProgressor(new BalanceConfig());

        /// <summary>
        /// Stride for folding the season year into the per-week development RNG. The
        /// training-week counter is season-local (resets each season), so without this
        /// season 2's week 1 would reuse season 1's week-1 rolls; multiplying the year by a
        /// stride safely larger than the weeks in a season decorrelates them while staying
        /// fully deterministic per (seed, year, week).
        /// </summary>
        private const int SeasonWeekStride = 100;

        public LocalClock(
            CareerState career,
            ISaveRepository saveRepository,
            IMessageBroker broker,
            UserMatchLog matchLog,
            UserMatchContextHolder matchContext,
            LocalMarketService market,
            ScoutingService scouting,
            InboxService inbox)
        {
            _career = career;
            _saveRepository = saveRepository;
            _broker = broker;
            _matchLog = matchLog;
            _matchContext = matchContext;
            _market = market;
            _scouting = scouting;
            _inbox = inbox;
        }

        public int CurrentDay => _career.Season.CurrentDay;

        public void AdvanceDay()
        {
            // Run any transfer window now due (task 5.2b) BEFORE the day's matches, so the
            // start-of-season and mid-season AI markets are reflected in the squads that play
            // (and in the user's upcoming opponents). The user's club is excluded; whole-world
            // AI↔AI otherwise. Guarded by TransferWindowsRun so it fires at most twice a season.
            _market.RunDueWindows();

            // Top up incoming AI offers for the user's listed players (task 5.3). No-op unless a
            // window is open and the user has listed someone; deterministic, saves only if changed.
            _market.GenerateListingOffers();

            // Develop the world for any training week the upcoming day completes, BEFORE
            // the day's matches: attributes then stay stable through the match sim and the
            // watched-match re-sim (task 3.4), so the re-sim still reproduces the committed
            // result. Training weeks align with the matchday calendar (every period days).
            EvolveTrainingUpTo(_career.Season.CurrentDay + 1);

            Dictionary<int, LineupPlan> plans = null;
            if (_career.UserLineup != null)
                plans = new Dictionary<int, LineupPlan> { [_career.UserClubId] = _career.UserLineup };

            // The user's chosen tactic flows into their own fixtures (only the
            // user club is in the dict; AI clubs play neutral = engine identity,
            // so their matches stay byte-identical). null tactic = no entry.
            int famAtKickoff = _career.UserTactic != null ? GetFamiliarity(_career.UserTactic) : 0;
            Dictionary<int, TacticContext> tactics = null;
            if (_career.UserTactic != null)
            {
                tactics = new Dictionary<int, TacticContext>
                {
                    [_career.UserClubId] = new TacticContext(_career.UserTactic.ToTactic(), famAtKickoff)
                };
            }

            // The user's conditional pre-match plan (3.5): resolved against the
            // current squad and the in-match formation, then executed automatically
            // in the user's fixtures — including ones advanced past without watching.
            List<MatchRule> userRules = ResolveUserRules();
            Dictionary<int, IReadOnlyList<MatchRule>> rules = userRules != null
                ? new Dictionary<int, IReadOnlyList<MatchRule>> { [_career.UserClubId] = userRules }
                : null;

            // Difficulty (task 5.7b): the user's AI opponents field a competence-scaled XI (the user's
            // own club is excluded). null/Hard = best XI; Easy/Normal field genuinely weaker sides.
            DifficultyContext difficulty = BuildDifficultyContext();

            List<MatchOutcome> outcomes = _progressor.AdvanceDay(_career.Leagues, _career.Season, _career.Seed, plans, tactics, rules, difficulty);

            bool userMatchPlayed = false;
            foreach (MatchOutcome outcome in outcomes)
            {
                if (outcome.Fixture.Involves(_career.UserClubId))
                {
                    _matchLog.LastMatch = outcome;
                    // Captures the kickoff condition snapshot BEFORE EvolveCondition runs below.
                    _matchContext.Current = BuildUserMatchContext(outcome.Fixture, famAtKickoff, userRules, difficulty);
                    // Credit the starting XI's minutes toward the next development week (4.4).
                    RecordUserMatchStarts();
                    // Inbox notification of the user's result (task 6.2).
                    PostUserMatchInbox(outcome.Fixture);
                    userMatchPlayed = true;
                }
            }

            // Credit gate receipts to the home club of every fixture played today (task 5.5).
            // Mutates only Finances.Balance, never attributes/condition — the user-match re-sim
            // is unaffected. Sponsors/wages settle weekly inside EvolveTrainingUpTo above.
            _finance.AccrueMatchday(_career.Leagues, outcomes);

            // Evolve the whole world's condition for the day just played: starters
            // drain, benched players take the morale/form step, idle clubs recover.
            // Runs after the snapshot capture so the user-match re-sim stays consistent.
            _progressor.EvolveCondition(_career.Leagues, _career.Season, outcomes, _career.Seed, plans, difficulty);

            // Familiarity accrues once per user match actually played with a tactic.
            if (userMatchPlayed && _career.UserTactic != null)
                AccumulateFamiliarity(_career.UserTactic);

            LogUserMatch(outcomes, plans != null);
            _saveRepository.Save(_career);
            _broker.Publish(new DayAdvancedMessage(_career.Season.CurrentDay, outcomes.Count, userMatchPlayed));
        }

        /// <summary>
        /// Evolves the whole world's development for every training week whose end day
        /// is at or before <paramref name="upcomingDay"/> and that hasn't run yet. The
        /// user club uses its saved plan (else the balanced default); all other clubs
        /// train the balanced default — so the user shapes only his own squad while the
        /// world still improves/declines around him. Deterministic per (seed, week);
        /// the catch-up loop runs at most one week on a normal day-by-day advance.
        /// </summary>
        private void EvolveTrainingUpTo(int upcomingDay)
        {
            if (_trainingPeriodDays <= 0)
                return;

            int targetWeek = upcomingDay / _trainingPeriodDays;
            if (_career.LastTrainingWeek >= targetWeek)
                return;

            Dictionary<int, TrainingPlan> plans = null;
            if (_career.UserTraining != null)
                plans = new Dictionary<int, TrainingPlan> { [_career.UserClubId] = _career.UserTraining };

            // Per-player minutes accumulated since the last tick → development context for
            // the user club (form doubles as the performance signal; facilities stay neutral
            // until task 5.5). AI clubs are absent ⇒ neutral context (age-only growth). Built
            // once and reused for the whole catch-up (normally a single week).
            Dictionary<int, DevelopmentContext> contexts = BuildUserDevelopmentContexts();

            // Honour the Rest support action (4.5): a rested player skips this tick's
            // training. The whole-world development RNG is per-club and shared across the
            // squad, so we can't simply omit a player without shifting his team-mates'
            // rolls. Instead we let development run normally (stream untouched, everyone
            // else identical) and restore the rested players' attributes afterwards — so
            // their week is effectively skipped while staying fully deterministic.
            Dictionary<int, int[]> restedSkills = SnapshotRestedSkills();

            while (_career.LastTrainingWeek < targetWeek)
            {
                int weekInSeason = _career.LastTrainingWeek + 1;
                // Decorrelate the per-week RNG across seasons (the counter resets each season).
                int rngWeek = _career.Season.Year * SeasonWeekStride + weekInSeason;

                _development.EvolveWeek(_career.Leagues, plans, contexts, _career.Seed, rngWeek);

                // Scout the whole world this week on the same cadence (task 5.4b): the user club
                // follows its assignments, every other club the default policy. Pure/no-RNG, never
                // read by the engine, so attributes the re-sim relies on are unaffected.
                _scouting.EvolveWeek();

                // Settle one week of the whole world's finances on the same cadence (task 5.5):
                // sponsor income in, the wage bill out (scaled by each club's current standing),
                // then the board floors the balance so no club goes bankrupt. Wages read each
                // player's cached MarketValue (kept fresh by the weekly re-price below). Only
                // Finances change — attributes/condition are untouched, so the re-sim is safe.
                _finance.AccrueWeek(_career.Leagues, _career.Season);

                // The Tactical team focus drills the user's current tactic (the "affects
                // tactic familiarity" half of 4.3); every other focus gains nothing here.
                TeamTrainingFocus focus = _career.UserTraining?.TeamFocus ?? TeamTrainingFocus.Balanced;
                int familiarityGain = TrainingModel.TacticalFamiliarityGain(focus, _developmentConfig);
                if (familiarityGain > 0)
                    AccrueTacticalFamiliarity(familiarityGain);

                _career.LastTrainingWeek = weekInSeason;
            }

            // Skipped: undo development for the rested players (their training cost).
            RestoreRestedSkills(restedSkills);

            // Re-price the whole world now that attributes/ages have moved this week (5.1).
            _valuation.Reprice(_career.Leagues);

            // Window consumed: start fresh for the next tick.
            _career.StartsSinceTraining.Clear();
            _career.UserMatchesSinceTraining = 0;
            _career.RestedSinceTraining.Clear();
        }

        /// <summary>
        /// Snapshots the current skills of every player flagged Rested since the last tick
        /// (task 4.5), so <see cref="RestoreRestedSkills"/> can put them back after the
        /// world develops — making a rested player's training week a no-op without
        /// perturbing the shared per-club development RNG of his team-mates.
        /// </summary>
        private Dictionary<int, int[]> SnapshotRestedSkills()
        {
            var snapshot = new Dictionary<int, int[]>();
            if (_career.RestedSinceTraining == null)
                return snapshot;

            foreach (int playerId in _career.RestedSinceTraining)
            {
                Player p = _career.FindPlayer(playerId);
                if (p == null || snapshot.ContainsKey(playerId))
                    continue;

                var skills = new int[PlayerAttributes.SkillCount];
                for (int i = 0; i < PlayerAttributes.SkillCount; i++)
                    skills[i] = p.Attributes[i];
                snapshot[playerId] = skills;
            }

            return snapshot;
        }

        /// <summary>Restores the snapshotted skills, cancelling this tick's development for the rested players.</summary>
        private void RestoreRestedSkills(Dictionary<int, int[]> snapshot)
        {
            foreach (var kv in snapshot)
            {
                Player p = _career.FindPlayer(kv.Key);
                if (p == null)
                    continue;

                for (int i = 0; i < PlayerAttributes.SkillCount; i++)
                    p.Attributes[i] = kv.Value[i];
            }
        }

        /// <summary>
        /// Builds a per-player <see cref="DevelopmentContext"/> for the user club from the
        /// minutes accumulated since the last training tick: playing share = starts / matches
        /// (full when no match fell in the window, so a bye week never penalises anyone).
        /// Form supplies the performance signal (neutral 50 = no effect); facilities are
        /// neutral until task 5.5. Only user-club players get an entry — every other club
        /// falls back to the neutral context (age-only growth) inside the progressor.
        /// </summary>
        private Dictionary<int, DevelopmentContext> BuildUserDevelopmentContexts()
        {
            Club userClub = _career.FindClub(_career.UserClubId);
            if (userClub == null)
                return null;

            // Training-ground facility level (task 5.5): tier 1 = the neutral baseline (world
            // develops as 4.4), each upgrade lifts the development facility level → faster growth.
            int facilityLevel = FacilityEffects.TrainingFacilityLevel(userClub.Facilities.Training, _config);

            int matches = _career.UserMatchesSinceTraining;
            var contexts = new Dictionary<int, DevelopmentContext>();
            foreach (Player p in userClub.Squad.Players)
            {
                int starts = _career.StartsSinceTraining.TryGetValue(p.Id, out int s) ? s : 0;
                int share = matches > 0 ? starts * 100 / matches : 100;
                contexts[p.Id] = new DevelopmentContext(share, facilityLevel, p.Condition.Form);
            }

            return contexts;
        }

        /// <summary>
        /// Credits the user club's starting eleven (resolved exactly as the sim resolved it)
        /// toward the next development week's minutes. Starters get a full match, the bench
        /// none (partial sub minutes aren't modelled in v1, mirroring the condition model).
        /// </summary>
        private void RecordUserMatchStarts()
        {
            Club userClub = _career.FindClub(_career.UserClubId);
            if (userClub == null)
                return;

            Lineup xi = ResolveUserLineup(userClub);
            _career.UserMatchesSinceTraining++;
            foreach (LineupSlot slot in xi.Slots)
            {
                int id = slot.Player.Id;
                _career.StartsSinceTraining[id] = (_career.StartsSinceTraining.TryGetValue(id, out int c) ? c : 0) + 1;
            }
        }

        /// <summary>Adds a week of Tactical-focus drilling to the user's current tactic familiarity (capped at max).</summary>
        private void AccrueTacticalFamiliarity(int gain)
        {
            string key = (_career.UserTactic ?? TacticPlan.Neutral()).Key();
            int current = _career.TacticFamiliarity.TryGetValue(key, out int v) ? v : 0;
            int max = _tacticsConfig.FamiliarityMax;
            int next = current + gain;
            _career.TacticFamiliarity[key] = next > max ? max : next;
        }

        /// <summary>
        /// Rebuilds the user match's kickoff plan exactly as SeasonProgressor
        /// resolved it (same lineup fallback, same tactics, same per-fixture seed),
        /// so the watch screen can re-simulate from any minute and the unchanged
        /// re-sim reproduces the committed result (task 3.4).
        /// </summary>
        private UserMatchContext BuildUserMatchContext(Fixture fixture, int famAtKickoff, List<MatchRule> userRules, DifficultyContext difficulty)
        {
            bool userIsHome = fixture.HomeClubId == _career.UserClubId;
            Club userClub = _career.FindClub(_career.UserClubId);
            Club opponent = _career.FindClub(userIsHome ? fixture.AwayClubId : fixture.HomeClubId);

            Lineup userLineup = ResolveUserLineup(userClub);
            // Resolve the opponent's XI EXACTLY as the season sim did (difficulty-degraded under
            // Easy/Normal, best XI under Hard) so the watched-match re-sim reproduces the committed
            // result (task 3.4 + 5.7b). The user club is excluded by the context's HumanClubId.
            Lineup oppLineup = SeasonProgressor.ResolveAiLineup(opponent, _career.Seed, fixture.Id, difficulty);

            MatchTactics matchTactics = null;
            if (_career.UserTactic != null)
            {
                var userCtx = new TacticContext(_career.UserTactic.ToTactic(), famAtKickoff);
                TacticContext oppCtx = TacticContext.Neutral(_tacticsConfig.FamiliarityMax);
                matchTactics = userIsHome ? new MatchTactics(userCtx, oppCtx) : new MatchTactics(oppCtx, userCtx);
            }

            Lineup home = userIsHome ? userLineup : oppLineup;
            Lineup away = userIsHome ? oppLineup : userLineup;
            var plan = new MatchPlan(new MatchInput(home, away, matchTactics));

            // Rules belong to the user's side only; the watch re-sim then reproduces
            // the committed result and the user can still intervene on top (3.4).
            IReadOnlyList<MatchRule> homeRules = userIsHome ? userRules : null;
            IReadOnlyList<MatchRule> awayRules = userIsHome ? null : userRules;

            // Freeze the kickoff condition of both squads (4.2): EvolveCondition will
            // drain/rest these same players right after, so the re-sim must restore
            // these values to reproduce the committed result.
            var kickoff = new Dictionary<int, PlayerCondition>();
            SnapshotCondition(userClub, kickoff);
            SnapshotCondition(opponent, kickoff);

            return new UserMatchContext(fixture, _career.Seed, plan, userIsHome, homeRules, awayRules, kickoff);
        }

        /// <summary>Stores a frozen copy of every squad player's current condition into <paramref name="into"/>.</summary>
        private static void SnapshotCondition(Club club, Dictionary<int, PlayerCondition> into)
        {
            if (club == null)
                return;

            foreach (Player p in club.Squad.Players)
            {
                into[p.Id] = new PlayerCondition
                {
                    Form = p.Condition.Form,
                    Morale = p.Condition.Morale,
                    Fitness = p.Condition.Fitness
                };
            }
        }

        /// <summary>
        /// Resolves the user's pre-match plan against the current squad, or null
        /// when there is no plan / it resolves to no rules. Instruction-change
        /// familiarity is looked up per resulting tactic (in-match formation +
        /// instructions), mirroring how the kickoff tactic's familiarity is read.
        /// </summary>
        private List<MatchRule> ResolveUserRules()
        {
            if (_career.UserPlan == null || _career.UserPlan.Rules.Count == 0)
                return null;

            Club userClub = _career.FindClub(_career.UserClubId);
            Formation formation = _career.UserTactic?.Formation ?? Formation.F433;

            List<MatchRule> resolved = _career.UserPlan.Resolve(
                userClub,
                instr => FamiliarityForInstructions(formation, instr),
                _tacticsConfig.FamiliarityMax);

            return resolved.Count > 0 ? resolved : null;
        }

        private int FamiliarityForInstructions(Formation formation, TacticInstructions instructions)
        {
            string key = TacticPlan.FromTactic(new Tactic(formation, instructions)).Key();
            return _career.TacticFamiliarity.TryGetValue(key, out int v) ? v : 0;
        }

        /// <summary>Mirrors SeasonProgressor.ResolveLineup for the user club (plan if valid, else best XI).</summary>
        private Lineup ResolveUserLineup(Club club)
        {
            if (_career.UserLineup != null && _career.UserLineup.TryMaterialize(club, out Lineup lineup))
                return lineup;

            return LineupSelector.BestEleven(club);
        }

        /// <summary>
        /// The match difficulty slice (task 5.7b): the AI lineup competence for the chosen difficulty,
        /// stamped with the user's club id so his own XI is never degraded. Hard resolves to competence
        /// 100 (best XI = the pre-5.7 AI); Easy/Normal field weaker opponent sides.
        /// </summary>
        private DifficultyContext BuildDifficultyContext()
        {
            DifficultySettings settings = DifficultyModel.Resolve(_career.Difficulty, _config);
            return DifficultyModel.MatchContext(_career.UserClubId, settings);
        }

        private int GetFamiliarity(TacticPlan tactic) =>
            _career.TacticFamiliarity.TryGetValue(tactic.Key(), out int v) ? v : 0;

        private void AccumulateFamiliarity(TacticPlan tactic)
        {
            string key = tactic.Key();
            int current = GetFamiliarity(tactic);
            _career.TacticFamiliarity[key] = FamiliarityModel.Accumulate(current, _tacticsConfig);
        }

        /// <summary>
        /// Console log of the user club's match (result, scorers, XI source) —
        /// the verification window until the match-day screen arrives in 2.6.
        /// </summary>
        private void LogUserMatch(List<MatchOutcome> outcomes, bool customLineup)
        {
            foreach (MatchOutcome outcome in outcomes)
            {
                Fixture f = outcome.Fixture;
                if (!f.Involves(_career.UserClubId))
                    continue;

                string home = ClubName(f.HomeClubId);
                string away = ClubName(f.AwayClubId);

                var scorers = new StringBuilder();
                foreach (MatchEvent e in outcome.Report.Events)
                {
                    if (e.Type != MatchEventType.Goal)
                        continue;
                    if (scorers.Length > 0) scorers.Append(", ");
                    scorers.Append($"{e.Minute}' {PlayerName(e.ClubId, e.PlayerId)} ({ClubName(e.ClubId)})");
                }

                Debug.Log(
                    $"[Match] {home} {f.HomeGoals}-{f.AwayGoals} {away} | user XI: {(customLineup ? "custom" : "auto")} | " +
                    $"scorers: {(scorers.Length > 0 ? scorers.ToString() : "none")}");
            }
        }

        /// <summary>
        /// Posts the user's match result to the Inbox (task 6.2). The template is chosen from the
        /// user's perspective (win/draw/loss) while the args always read home-to-away so the
        /// scoreline is correct whichever side the user played.
        /// </summary>
        private void PostUserMatchInbox(Fixture f)
        {
            bool userHome = f.HomeClubId == _career.UserClubId;
            int userGoals = userHome ? f.HomeGoals : f.AwayGoals;
            int oppGoals = userHome ? f.AwayGoals : f.HomeGoals;

            string key = userGoals > oppGoals ? "inbox.match_win"
                : userGoals < oppGoals ? "inbox.match_loss"
                : "inbox.match_draw";

            _inbox.Post(InboxCategory.Match, key,
                ClubName(f.HomeClubId), f.HomeGoals.ToString(), f.AwayGoals.ToString(), ClubName(f.AwayClubId));
        }

        private string ClubName(int clubId) => _career.FindClub(clubId)?.Name ?? $"Club {clubId}";

        private string PlayerName(int clubId, int playerId)
        {
            Club club = _career.FindClub(clubId);
            if (club != null)
            {
                foreach (Player p in club.Squad.Players)
                {
                    if (p.Id == playerId)
                        return p.FullName;
                }
            }

            return $"Player {playerId}";
        }
    }
}
