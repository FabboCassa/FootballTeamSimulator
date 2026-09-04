using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;

namespace Sim.Core.Career
{
    /// <summary>
    /// The board's expectations and the confidence (sacking) meter (task 5.6). PURE and
    /// deterministic — integer math, NO RNG. Two jobs:
    ///   • set a season objective (an expected finishing position) from squad strength, last
    ///     season's finish and the coach's reputation;
    ///   • move <see cref="Coach.BoardConfidence"/> toward that expectation as results come in,
    ///     with a capped per-evaluation change so a warning season always precedes a sacking.
    /// Opt-in: the match engine and SeasonProgressor never call it → golden masters unaffected.
    /// </summary>
    public sealed class BoardModel
    {
        private readonly CareerBalance _cfg;

        public BoardModel(BalanceConfig? config = null)
        {
            _cfg = (config ?? new BalanceConfig()).Career;
        }

        /// <summary>
        /// The club's 1-based rank within its division by squad strength (best XI average overall),
        /// strongest = 1. Ties broken by club id so the rank is total and deterministic.
        /// </summary>
        public static int StrengthRank(Club club, League league)
        {
            int myStrength = BudgetModel.ClubStrength(club);
            int rank = 1;
            foreach (Club other in league.Clubs)
            {
                if (other.Id == club.Id) continue;
                int s = BudgetModel.ClubStrength(other);
                if (s > myStrength || (s == myStrength && other.Id < club.Id))
                    rank++;
            }
            return rank;
        }

        /// <summary>
        /// The position the board expects this season: a blend of the club's squad-strength rank and
        /// last season's finish (when one exists), nudged by the coach's reputation (the board asks
        /// more of a famous coach). Clamped to [1, clubCount].
        /// </summary>
        public int ExpectedPosition(Club club, League league, int prevFinishPosition)
        {
            int clubCount = league.Clubs.Count;
            if (clubCount < 1) clubCount = 1;

            int strengthRank = StrengthRank(club, league);

            int blended;
            if (prevFinishPosition >= 1)
            {
                int sw = _cfg.ObjectiveStrengthRankWeightPercent;
                int pw = _cfg.ObjectivePrevFinishWeightPercent;
                int denom = sw + pw;
                if (denom <= 0) { sw = 1; denom = 1; pw = 0; }
                blended = (strengthRank * sw + prevFinishPosition * pw + denom / 2) / denom;
            }
            else
            {
                blended = strengthRank;
            }

            // A famous coach (reputation above neutral) is expected to finish higher (lower number).
            int repSwing = (club.Coach.Reputation - _cfg.NeutralConfidence) * _cfg.ObjectiveReputationPositionSwingPercent / 100;
            int expected = blended - repSwing;

            if (expected < 1) expected = 1;
            if (expected > clubCount) expected = clubCount;
            return expected;
        }

        /// <summary>Sets <see cref="Coach.ObjectiveExpectedPosition"/> for the upcoming season.</summary>
        public void AssignObjective(Club club, League league, int prevFinishPosition)
        {
            club.Coach.ObjectiveExpectedPosition = ExpectedPosition(club, league, prevFinishPosition);
        }

        // ----------------------------------------------------------------- confidence

        /// <summary>
        /// Season-end confidence change: every position better than the objective lifts confidence,
        /// every position worse drains it; meeting the objective adds a small reassurance bonus.
        /// Capped at ±<see cref="CareerBalance.MaxConfidenceDeltaPerEvaluation"/> so confidence can
        /// never fall from the warning line straight past the sack line in one season.
        /// </summary>
        public int SeasonEndConfidenceDelta(int actualPosition, int expectedPosition)
        {
            int gap = expectedPosition - actualPosition; // >0 = better than expected
            int delta = gap * _cfg.ConfidencePerPositionVsObjective;

            if (BoardObjective.Classify(actualPosition, expectedPosition, _cfg) == SeasonOutcome.Met)
                delta += _cfg.ConfidenceMeetBonus;

            return Cap(delta, _cfg.MaxConfidenceDeltaPerEvaluation);
        }

        /// <summary>
        /// A gentler mid-season running check from the club's CURRENT table position vs its objective
        /// (also capped). Lets the seat heat up before the season is over so a warning can land early.
        /// </summary>
        public int RunningConfidenceDelta(int currentPosition, int expectedPosition)
        {
            int gap = expectedPosition - currentPosition;
            int delta = gap * _cfg.RunningConfidencePerPositionVsObjective;
            return Cap(delta, _cfg.MaxConfidenceDeltaPerEvaluation);
        }

        /// <summary>Applies a capped confidence delta to the coach (clamped to [0,100] by Coach).</summary>
        public void ApplyConfidenceDelta(Coach coach, int delta)
        {
            coach.BoardConfidence += Cap(delta, _cfg.MaxConfidenceDeltaPerEvaluation);
        }

        // ----------------------------------------------------------------- thresholds

        /// <summary>True if the board would sack the coach at this confidence (below the sack line).</summary>
        public bool ShouldSack(int confidence) => confidence < _cfg.ConfidenceSackThreshold;

        /// <summary>True if the seat is hot — at or below the warning line (a public warning, not yet a sacking).</summary>
        public bool IsWarned(int confidence) => confidence <= _cfg.ConfidenceWarningThreshold;

        private static int Cap(int value, int max)
        {
            if (value > max) return max;
            if (value < -max) return -max;
            return value;
        }
    }
}
