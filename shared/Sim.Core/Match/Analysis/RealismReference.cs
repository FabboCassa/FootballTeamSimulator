using System.Collections.Generic;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The R2 neutral realism bands of docs/specs/real-match-and-playing-styles.md, sourced in
    /// docs/research/football-reference.md ("the doc"). Each band names the doc section that backs
    /// it; doc §5 lists every band, and doc §6 logs, with its arithmetic, every band that differs
    /// from the spec's R2 starting value (only <see cref="Shots"/>).
    ///
    /// Percentages are 0-100. "Per match" counts both teams; "per team" is the per-match count ÷ 2.
    /// A ratio is pooled (total numerator ÷ total denominator over every match and both teams)
    /// unless stated "mean over sequences".
    ///
    /// Definitions the harness must use (doc §1.5, same text). "Convention" marks a choice no
    /// source gives.
    /// - Attempted pass (convention): every deliberate kick, header or throw that is not a shot,
    ///   clearances, crosses and every restart delivery (kick-off, goal kick, throw-in, free kick,
    ///   corner) included; saves, blocks and deflections are not passes. Completed: the next touch
    ///   is by a teammate with no stoppage before it.
    /// - Sequence (Opta; boundaries by convention): the run of touches by one team. It starts with
    ///   its first touch after the ball came from the opponent, after a shot, or at a restart; it
    ///   ends at any opponent touch (deflections and keeper saves included), a shot, or a stoppage
    ///   (ball out, foul, offside, goal, end of a half).
    /// - Open-play sequence (convention): first touch in play (not a restart), or a throw-in or
    ///   goal kick. Kick-off, corner, free kick, penalty or any other restart starts a set-piece
    ///   sequence, left out of every sequence metric and of PPDA.
    /// - Goals: scoreboard goals, penalties and own goals included. Shots: every attempt, blocked
    ///   and penalties included; own goals are not shots. On target: goal or keeper save ÷ all
    ///   shots. Blocked: stopped by an outfield defender ÷ all shots. Inside the box: struck inside
    ///   the opponent's penalty area, on the line counts as inside (convention). Save rate: saves ÷
    ///   (saves + goals from shots), penalties included, own goals excluded; the box split uses
    ///   the same location rule. Fouls: whistled fouls, penalty fouls included, offsides not.
    /// - Passes per open-play sequence: attempted passes inside open-play sequences ÷ open-play
    ///   sequences, zero-pass sequences included (the starting throw-in or goal kick and the
    ///   final incomplete pass count). 10+ sequences: open-play sequences with at least 10
    ///   attempted passes, ÷ (2 × matches).
    /// - Direct speed: mean over open-play sequences (each weighted 1) of progress ÷ duration.
    ///   Convention: progress is the signed distance toward the opponent goal from the ball at the
    ///   first touch to the ball at the last touch (may be negative); duration is the time between
    ///   them; sequences under 1 s are left out.
    /// - PPDA (convention, one pass set), per pressing team: numerator = the opponent's attempted
    ///   passes started in the zone of <see cref="PpdaZoneFraction"/> that belong to an opponent
    ///   open-play sequence (its starting throw-in or goal kick included; set-piece sequences not
    ///   counted); denominator = the pressing team's tackles (won or lost), interceptions and fouls
    ///   in the zone while the opponent owns an open-play sequence, the ending action included.
    ///   Aggregated as Σ passes ÷ Σ actions over every match and both sides (doc §2.4 is built the
    ///   same way), not as a mean of per-match ratios.
    /// - Crosses (convention): attempted passes started in the last third, outside the width of
    ///   the penalty area, aimed inside the opponent's penalty area, completed or not; corner kicks
    ///   and throw-ins excluded, qualifying free kicks included.
    /// - Ball in play: minutes with the ball in play, added time included, mean over matches.
    /// - Distance per outfield player (convention): a team's outfield distance in a match,
    ///   substitutes included, keepers excluded, ÷ 10; mean over team-matches.
    /// </summary>
    public static class RealismReference
    {
        /// <summary>
        /// PPDA zone on the Understat scale (doc §1.3): the fraction of the pitch length, nearest
        /// the passing team's own goal, in which opponent passes and the pressing team's defensive
        /// actions are counted. Opta's zone (outside the pressing team's defensive third) reads
        /// higher for the same team, so the band and the R19 PPDA anchors hold only on this scale.
        /// </summary>
        public const double PpdaZoneFraction = 0.60;

        // doc §5 / §2.1 League core events
        public static readonly RealismBand Goals = new RealismBand("goals/match", 2.5, 3.2);
        // doc §5 / §2.1 League core events; max narrowed, holding both the 2024-25 and 2025-26
        // top-five envelopes, doc §6
        public static readonly RealismBand Shots = new RealismBand("shots/match", 23, 27.9);
        // doc §5 / §2.1 League core events, §2.2 Shot mix and keeper
        public static readonly RealismBand OnTargetPercent =
            new RealismBand("on target (excl. blocked) % of shots", 30, 38);
        // doc §5 / §2.2 Shot mix and keeper
        public static readonly RealismBand BlockedPercent = new RealismBand("blocked % of shots", 20, 30);
        // doc §5 / §2.2 Shot mix and keeper
        public static readonly RealismBand InsideBoxPercent = new RealismBand("inside the box % of shots", 60, 72);
        // doc §5 / §2.2 Shot mix and keeper
        public static readonly RealismBand HeadedShots = new RealismBand("headed shots/match", 3, 5);
        // doc §5 / §2.1 League core events, §2.2 Shot mix and keeper; kept at R2, doc §6
        public static readonly RealismBand SaveRatePercent = new RealismBand("keeper save rate %", 65, 75);
        // doc §5 / §2.2 Shot mix and keeper
        public static readonly RealismBand SaveRateInsideBoxPercent =
            new RealismBand("save rate inside the box %", 55, 68);
        // doc §5 / §2.2 Shot mix and keeper
        public static readonly RealismBand SaveRateOutsideBoxPercent =
            new RealismBand("save rate outside the box %", 78, 90);
        // doc §5 / §2.3 Passing
        public static readonly RealismBand PassesPerTeam = new RealismBand("passes attempted/team", 380, 520);
        // doc §5 / §2.3 Passing
        public static readonly RealismBand PassAccuracyPercent = new RealismBand("pass accuracy %", 78, 86);
        // doc §5 / §2.4 Sequences and pressing; kept at R2 (no league mean), doc §6
        public static readonly RealismBand PassesPerSequence =
            new RealismBand("passes per open-play sequence", 3.0, 5.0);
        // doc §5 / §2.4 Sequences and pressing; kept at R2 (no league mean), doc §6
        public static readonly RealismBand TenPlusSequencesPerTeam =
            new RealismBand("10+ pass open-play sequences/team", 6, 14);
        // doc §5 / §2.4 Sequences and pressing; kept at R2 (no league mean), doc §6 and §8
        public static readonly RealismBand DirectSpeed = new RealismBand("direct speed (open play) m/s", 1.3, 1.8);
        // doc §5 / §1.3 PPDA scales, §2.4 Sequences and pressing (Understat zone); kept at R2, doc §6
        public static readonly RealismBand Ppda = new RealismBand("PPDA", 9, 14);
        // doc §5 / §1.4 Other conventions, §2.3 Passing; kept at R2 (scale not established), doc §6
        public static readonly RealismBand CrossesPerTeam = new RealismBand("crosses/team", 12, 20);
        // doc §5 / §2.1 League core events
        public static readonly RealismBand Corners = new RealismBand("corners/match", 9, 11);
        // doc §5 / §2.1 League core events
        public static readonly RealismBand Fouls = new RealismBand("fouls/match", 20, 27);
        // doc §5 / §2.5 Ball in play and distance; kept at R2, doc §6
        public static readonly RealismBand BallInPlayMinutes = new RealismBand("ball in play min", 53, 59);
        // doc §5 / §2.5 Ball in play and distance
        public static readonly RealismBand DistancePerOutfieldPlayerKm =
            new RealismBand("distance per outfield player km", 9.5, 11.5);

        /// <summary>Every band above, in R2 table order.</summary>
        public static readonly IReadOnlyList<RealismBand> All = new[]
        {
            Goals, Shots, OnTargetPercent, BlockedPercent, InsideBoxPercent, HeadedShots,
            SaveRatePercent, SaveRateInsideBoxPercent, SaveRateOutsideBoxPercent,
            PassesPerTeam, PassAccuracyPercent, PassesPerSequence, TenPlusSequencesPerTeam,
            DirectSpeed, Ppda, CrossesPerTeam, Corners, Fouls, BallInPlayMinutes,
            DistancePerOutfieldPlayerKm,
        };
    }
}
