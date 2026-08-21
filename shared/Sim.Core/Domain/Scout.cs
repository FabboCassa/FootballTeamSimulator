namespace Sim.Core.Domain
{
    /// <summary>
    /// A scout employed by a club (task 5.4, given attributes and a contract in task 11.2). Plain
    /// data; the scouting behaviour (how fast a watched player's ranges narrow, and how sharp the
    /// resulting report is) lives in <see cref="Scouting.ScoutingModel"/> and
    /// <see cref="Scouting.ScoutQuality"/>.
    ///
    /// Two dials, deliberately separate:
    ///   • <see cref="Level"/> is SPEED — how much knowledge a week of his work is worth. It is
    ///     what task 5.4 shipped, and what the scouting facility tier still drives.
    ///   • The three attributes below are PRECISION and REACH — how well he reads what he sees,
    ///     and how much a wide brief costs him. They are what makes one scout different from
    ///     another once the user is choosing who to send where (task 11.2).
    ///
    /// Every attribute defaults to <see cref="NeutralAttribute"/>, which resolves to exactly the
    /// pre-11.2 numbers — so an old save, whose scouts carry none of these fields, deserializes
    /// into scouts that behave precisely as they did before.
    ///
    /// Additive to the domain and never referenced by the match engine, so adding scouts to a
    /// world leaves golden masters/replays unaffected. A club with no scouts still scouts slowly
    /// at ScoutingBalance.BaseClubScoutLevel.
    /// </summary>
    public sealed class Scout
    {
        /// <summary>The attribute value that behaves exactly like the pre-11.2 department (100% of everything).</summary>
        public const int NeutralAttribute = 50;

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        private int _level = 1;

        /// <summary>Scout ability in [1, ScoutingBalance.MaxScoutLevel]; higher = faster knowledge. Clamped low end to 1.</summary>
        public int Level
        {
            get => _level;
            set => _level = value < 1 ? 1 : value;
        }

        /// <summary>
        /// How well he judges what a player IS, on the [1, 100] attribute scale (task 11.2). Raises
        /// the knowledge his ability and attribute bands are built at, so his reads are tighter and
        /// less off-centre than a poor judge's at the same number of weeks watched.
        /// </summary>
        public int JudgingAbility { get; set; } = NeutralAttribute;

        /// <summary>
        /// How well he judges what a player will BECOME, on the [1, 100] scale (task 11.2).
        /// Independent of <see cref="JudgingAbility"/> on purpose: a scout can be a fine judge of a
        /// finished footballer and hopeless with a seventeen-year-old.
        /// </summary>
        public int JudgingPotential { get; set; } = NeutralAttribute;

        /// <summary>
        /// How well he works far from one stand, on the [1, 100] scale (task 11.2). Scales the
        /// weekly knowledge he accrues on an AREA brief, so an adaptable scout is the one worth
        /// sending to a continent and a homebody is the one you park on a single club.
        /// </summary>
        public int Adaptability { get; set; } = NeutralAttribute;

        /// <summary>
        /// His terms (task 11.2). Wage and remaining seasons, the same <see cref="Domain.Contract"/>
        /// a player carries. Additive and, at the default zero wage, free — the user's decision for
        /// 11.2 was that the LIMITER is the number of scouts, not their cost, so nothing in
        /// Sim.Core charges this yet; it exists so the finance pass can start billing it without
        /// another save bump.
        /// </summary>
        public Contract Contract { get; set; } = new Contract();
    }
}
