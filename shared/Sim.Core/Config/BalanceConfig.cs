namespace Sim.Core.Config
{
    /// <summary>
    /// THE single home for every gameplay tunable (ARCHITECTURE.md 4.1 rule 4).
    /// Plain POCO: hosts load/override it from JSON (server can push updates,
    /// the client ships it embedded). Code defaults below ARE the baseline balance.
    /// Sim.Core never reads files - it only receives this object.
    /// </summary>
    public sealed class BalanceConfig
    {
        /// <summary>Bumped whenever a change invalidates stored seeds/replays.</summary>
        public int Version { get; set; } = 1;

        public GenerationBalance Generation { get; set; } = new GenerationBalance();
        public MatchBalance Match { get; set; } = new MatchBalance();
        public SeasonBalance Season { get; set; } = new SeasonBalance();
    }

    /// <summary>Tunables for the season calendar and league table.</summary>
    public sealed class SeasonBalance
    {
        /// <summary>Career day of matchday 1.</summary>
        public int FirstMatchDay { get; set; } = 7;

        /// <summary>Days between consecutive matchdays.</summary>
        public int DaysBetweenRounds { get; set; } = 7;

        public int PointsForWin { get; set; } = 3;
        public int PointsForDraw { get; set; } = 1;

        /// <summary>Clubs promoted/relegated between adjacent divisions at season end.</summary>
        public int PromotedRelegatedCount { get; set; } = 3;
    }

    /// <summary>Tunables for procedural league/player generation.</summary>
    public sealed class GenerationBalance
    {
        // --- Club strength hierarchy ---
        public int TopClubStrength { get; set; } = 72;
        public int BottomClubStrength { get; set; } = 52;
        /// <summary>Random jitter (+/-) applied to each club's baseline strength.</summary>
        public int ClubStrengthJitter { get; set; } = 2;

        /// <summary>How much weaker each lower division is (applied to top/bottom strength).</summary>
        public int DivisionStrengthStep { get; set; } = 14;

        // --- Player skills ---
        /// <summary>Random jitter (+/-) applied to each player's target overall around the club baseline.</summary>
        public int PlayerTargetNoise { get; set; } = 6;
        /// <summary>Random jitter (+/-) applied to every individual skill.</summary>
        public int SkillNoise { get; set; } = 8;
        public int OutfieldGoalkeepingMin { get; set; } = 2;
        public int OutfieldGoalkeepingMax { get; set; } = 15;

        // --- Ages ---
        public int MinAge { get; set; } = 17;
        public int MaxAge { get; set; } = 36;
        /// <summary>Relative weight of each age from MinAge upward. Peak in the mid-20s.</summary>
        public int[] AgeWeights { get; set; } =
            { 2, 3, 4, 6, 7, 8, 9, 9, 9, 8, 8, 7, 6, 5, 4, 3, 2, 1, 1, 1 };

        // --- Development potential ---
        /// <summary>Age at which growth headroom reaches zero.</summary>
        public int PeakAge { get; set; } = 27;
        /// <summary>Potential headroom granted per year below PeakAge.</summary>
        public int HeadroomPerYear { get; set; } = 2;
        public int HeadroomNoiseMin { get; set; } = -3;
        public int HeadroomNoiseMax { get; set; } = 6;

        // --- Fresh-career condition ---
        public int MoraleMin { get; set; } = 50;
        public int MoraleMax { get; set; } = 70;
        public int FitnessMin { get; set; } = 95;
        public int FitnessMax { get; set; } = 100;

        // --- Contracts ---
        /// <summary>WeeklyWage = overall^2 * WageFactor.</summary>
        public int WageFactor { get; set; } = 4;
        public int ContractSeasonsMin { get; set; } = 1;
        public int ContractSeasonsMax { get; set; } = 4;
    }

    /// <summary>Tunables for the match engine (consumed from task 1.4 onward).</summary>
    public sealed class MatchBalance
    {
        /// <summary>Strength bonus for the home side, in percent of team strength.</summary>
        public int HomeAdvantagePercent { get; set; } = 6;

        /// <summary>Probability that any given minute produces an attacking action (~25 per match).</summary>
        public double ActionChancePerMinute { get; set; } = 0.28;

        /// <summary>
        /// Scales how often a chance becomes a goal (calibrated for ~2.6-2.8 goals/match).
        /// Note: generated defenses rate slightly above attacks (role-weight inflation),
        /// so the equal-teams ratio r sits just below 0.5 - this value compensates.
        /// </summary>
        public double GoalCoefficient { get; set; } = 0.92;

        /// <summary>Exponent applied to midfield ratings when deriving possession. Higher = dominance matters more.</summary>
        public int PossessionSharpness { get; set; } = 2;

        /// <summary>Exponent applied to attack/defense ratio per chance. Higher = quality gaps matter more.</summary>
        public int ChanceSharpness { get; set; } = 3;

        /// <summary>Of the chances that do not score, how many are saves (vs off target). Cosmetic.</summary>
        public int SavedShareOfFailedChancesPercent { get; set; } = 55;

        /// <summary>Relative shooting weight per PositionRole (enum order: GK, CB, FB, DM, CM, AM, W, ST).</summary>
        public int[] ScorerWeightsByRole { get; set; } = { 0, 2, 3, 4, 8, 16, 18, 30 };

        // --- Position stream (task 1.5) — distances in decimetres (Pitch coords) ---

        /// <summary>Position snapshots per match minute (4 = one frame every 15 sim-seconds).</summary>
        public int TicksPerMinute { get; set; } = 4;

        /// <summary>Formation anchor X per PositionRole, in 1/1000 of pitch length (home side; away is mirrored).</summary>
        public int[] FormationAnchorXPermilleByRole { get; set; } = { 40, 180, 200, 340, 440, 540, 620, 720 };

        /// <summary>Y margin from the touchline for wide roles (FullBack, Winger).</summary>
        public int WideRoleYMarginDm { get; set; } = 80;

        /// <summary>Y margin from the touchline for central roles.</summary>
        public int CentralRoleYMarginDm { get; set; } = 170;

        /// <summary>How far from the attacked goal line shots are taken.</summary>
        public int ShotSpotGoalDistanceDm { get; set; } = 140;

        /// <summary>Max lateral offset of a shot spot from the goal's centre line.</summary>
        public int ShotSpotHalfWidthDm { get; set; } = 160;

        /// <summary>Ball wanders via random waypoints roughly this many ticks apart between events.</summary>
        public int BallWaypointIntervalTicks { get; set; } = 12;

        /// <summary>Max random deviation of the ball from its waypoint-to-waypoint path.</summary>
        public int BallNoiseDm { get; set; } = 50;

        /// <summary>Max distance a player covers per tick while repositioning.</summary>
        public int PlayerMaxStepDmPerTick { get; set; } = 70;

        /// <summary>Random per-tick jitter applied to each player position.</summary>
        public int PlayerNoiseDm { get; set; } = 15;

        /// <summary>How strongly outfield players shift along X toward the ball (percent of ball offset from centre).</summary>
        public int PlayerBallPullXPercent { get; set; } = 30;

        /// <summary>How strongly outfield players drift along Y toward the ball (percent of distance).</summary>
        public int PlayerBallPullYPercent { get; set; } = 20;

        /// <summary>Ball pull percent override for goalkeepers (they mostly hold their line).</summary>
        public int GoalkeeperBallPullPercent { get; set; } = 6;

        /// <summary>Ticks before an event during which the shooter sprints toward the shot spot.</summary>
        public int ShooterApproachTicks { get; set; } = 3;
    }
}
