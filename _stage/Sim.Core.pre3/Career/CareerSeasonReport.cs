using System.Collections.Generic;

namespace Sim.Core.Career
{
    /// <summary>
    /// What the coach-career season-end evaluation produced (task 5.6), for the host to display and
    /// persist: the user's standing vs his board objective, his updated confidence/reputation, whether
    /// the seat is warned or he was sacked, the offers on the table, and the AI carousel (who was
    /// sacked and who was hired). Plain data — no logic.
    /// </summary>
    public sealed class CareerSeasonReport
    {
        public int Year { get; set; }

        // --- The user ---
        public int UserClubId { get; set; }
        public int UserFinishPosition { get; set; }
        public int UserExpectedPosition { get; set; }
        public SeasonOutcome UserOutcome { get; set; }

        /// <summary>Board confidence AFTER this season's evaluation.</summary>
        public int UserConfidence { get; set; }
        /// <summary>Reputation AFTER this season's evaluation.</summary>
        public int UserReputation { get; set; }

        /// <summary>The seat is hot (confidence at/below the warning line) — a warning, not a sacking.</summary>
        public bool UserWarned { get; set; }
        /// <summary>The board has dismissed the user (confidence below the sack line). The host presents it.</summary>
        public bool UserSacked { get; set; }

        /// <summary>Clubs offering the user a job (best stature first).</summary>
        public List<JobOffer> UserOffers { get; set; } = new List<JobOffer>();

        // --- The AI carousel ---
        public List<int> SackedAiClubIds { get; set; } = new List<int>();
        public List<HireRecord> Hires { get; set; } = new List<HireRecord>();
    }
}
