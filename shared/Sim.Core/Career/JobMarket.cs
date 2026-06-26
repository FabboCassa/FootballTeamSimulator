using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Career
{
    /// <summary>A job offer the user receives: which club, its division and the reputation it expects.</summary>
    public readonly struct JobOffer
    {
        public JobOffer(int clubId, string clubName, int division, int requiredReputation)
        {
            ClubId = clubId;
            ClubName = clubName;
            Division = division;
            RequiredReputation = requiredReputation;
        }

        public int ClubId { get; }
        public string ClubName { get; }
        public int Division { get; }
        /// <summary>The club's stature on the reputation scale — bigger = a better job.</summary>
        public int RequiredReputation { get; }
    }

    /// <summary>One AI vacancy filled: the club and the coach installed (fresh when the pool ran dry).</summary>
    public readonly struct HireRecord
    {
        public HireRecord(int clubId, int coachId, int coachReputation, bool fresh)
        {
            ClubId = clubId;
            CoachId = coachId;
            CoachReputation = coachReputation;
            Fresh = fresh;
        }

        public int ClubId { get; }
        public int CoachId { get; }
        public int CoachReputation { get; }
        /// <summary>True if a brand-new coach was minted (no available coach met the club).</summary>
        public bool Fresh { get; }
    }

    /// <summary>
    /// The hiring carousel (task 5.6): generates the offers the user receives and reassigns the
    /// coaches freed by AI sackings to the vacant benches. PURE and deterministic — integer math,
    /// NO RNG: vacancies are filled best-job-to-best-available-coach and offers ranked by a total
    /// order (reputation desc, club id asc), so the world replays identically. Opt-in: nothing in
    /// the match engine references it → golden masters unaffected.
    /// </summary>
    public sealed class JobMarket
    {
        private const ulong FreshCoachIdBase = 800_000UL;

        private readonly CareerBalance _cfg;
        private readonly ReputationModel _rep;

        public JobMarket(BalanceConfig? config = null)
        {
            BalanceConfig cfg = config ?? new BalanceConfig();
            _cfg = cfg.Career;
            _rep = new ReputationModel(cfg);
        }

        /// <summary>
        /// The clubs that would approach the user, given his reputation: clubs MORE prestigious than
        /// his current one that he now qualifies for (required reputation within
        /// <see cref="CareerBalance.OfferReputationMargin"/> of his), best first, capped at
        /// <see cref="CareerBalance.MaxUserOffers"/>. Pass <paramref name="currentClubId"/> = 0 (or any
        /// id not in the world) for an unemployed coach — then any club he qualifies for can offer.
        /// </summary>
        public List<JobOffer> GenerateUserOffers(Coach user, int currentClubId, IReadOnlyList<League> leagues)
        {
            int currentStature = 0;
            foreach (League league in leagues)
            {
                Club? cur = league.FindClub(currentClubId);
                if (cur != null) { currentStature = _rep.RequiredReputation(cur, league.Division); break; }
            }

            var candidates = new List<JobOffer>();
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    if (club.Id == currentClubId) continue;

                    int required = _rep.RequiredReputation(club, league.Division);

                    // Aspirational: a better job than the one he holds, and within reach of his reputation.
                    if (required <= currentStature) continue;
                    if (user.Reputation + _cfg.OfferReputationMargin < required) continue;

                    candidates.Add(new JobOffer(club.Id, club.Name, league.Division, required));
                }
            }

            candidates.Sort((a, b) =>
            {
                if (a.RequiredReputation != b.RequiredReputation)
                    return b.RequiredReputation.CompareTo(a.RequiredReputation);
                return a.ClubId.CompareTo(b.ClubId);
            });

            if (candidates.Count > _cfg.MaxUserOffers)
                candidates.RemoveRange(_cfg.MaxUserOffers, candidates.Count - _cfg.MaxUserOffers);
            return candidates;
        }

        /// <summary>
        /// Reassigns the freed coaches to the vacant benches: the most reputable available coach takes
        /// the most prestigious vacant job, and so on — but a coach is NEVER rehired by the club that
        /// just sacked him (he moves on, or fresh blood comes in). A vacancy with no eligible coach
        /// left gets a fresh one seeded to the club's stature. Each <paramref name="vacancies"/> entry
        /// is a (club, division) pair; each <paramref name="availablePool"/> entry is a freed coach and
        /// the id of the club he came from. Installs the coach (resetting confidence to neutral) and
        /// returns the hires for the report.
        /// </summary>
        public List<HireRecord> FillVacancies(
            IReadOnlyList<(Club club, int division)> vacancies,
            IReadOnlyList<(Coach coach, int formerClubId)> availablePool,
            int year)
        {
            // Best job first.
            var jobs = new List<(Club club, int division, int required)>();
            foreach ((Club club, int division) in vacancies)
                jobs.Add((club, division, _rep.RequiredReputation(club, division)));
            jobs.Sort((a, b) =>
            {
                if (a.required != b.required) return b.required.CompareTo(a.required);
                return a.club.Id.CompareTo(b.club.Id);
            });

            // Best available coach first.
            var pool = new List<(Coach coach, int formerClubId)>(availablePool);
            pool.Sort((a, b) =>
            {
                if (a.coach.Reputation != b.coach.Reputation) return b.coach.Reputation.CompareTo(a.coach.Reputation);
                return a.coach.Id.CompareTo(b.coach.Id);
            });
            var used = new bool[pool.Count];

            var hires = new List<HireRecord>(jobs.Count);
            foreach ((Club club, int division, int _) in jobs)
            {
                Coach? coach = null;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (used[i]) continue;
                    if (pool[i].formerClubId == club.Id) continue; // never rehired by the club that sacked him
                    used[i] = true;
                    coach = pool[i].coach;
                    break;
                }

                bool fresh = coach == null;
                if (coach == null) coach = MintCoach(club, division, year);

                coach.IsHuman = false;
                coach.BoardConfidence = _cfg.NeutralConfidence;
                club.Coach = coach;
                hires.Add(new HireRecord(club.Id, coach.Id, coach.Reputation, fresh));
            }

            return hires;
        }

        /// <summary>Creates a brand-new AI coach for a club, seeded to its stature.</summary>
        public Coach MintCoach(Club club, int division, int year)
        {
            int id = (int)(FreshCoachIdBase + (ulong)(year * 1000 + (club.Id % 1000)));
            return new Coach
            {
                Id = id,
                IsHuman = false,
                Reputation = _rep.SeedReputation(club, division),
                BoardConfidence = _cfg.NeutralConfidence
            };
        }
    }
}
