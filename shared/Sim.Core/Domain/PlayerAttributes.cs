namespace Sim.Core.Domain
{
    /// <summary>
    /// Player skills, each in [1, 100]. Setters clamp so an invalid state is unrepresentable.
    /// Which skills matter depends on the player's role (weighting added with the match engine, task 1.4).
    /// </summary>
    public sealed class PlayerAttributes
    {
        private int _pace = 50, _strength = 50, _stamina = 50, _technique = 50, _passing = 50,
                    _dribbling = 50, _shooting = 50, _defending = 50, _positioning = 50, _goalkeeping = 50;

        public int Pace        { get => _pace;        set => _pace        = AttributeScale.ClampSkill(value); }
        public int Strength    { get => _strength;    set => _strength    = AttributeScale.ClampSkill(value); }
        public int Stamina     { get => _stamina;     set => _stamina     = AttributeScale.ClampSkill(value); }
        public int Technique   { get => _technique;   set => _technique   = AttributeScale.ClampSkill(value); }
        public int Passing     { get => _passing;     set => _passing     = AttributeScale.ClampSkill(value); }
        public int Dribbling   { get => _dribbling;   set => _dribbling   = AttributeScale.ClampSkill(value); }
        public int Shooting    { get => _shooting;    set => _shooting    = AttributeScale.ClampSkill(value); }
        public int Defending   { get => _defending;   set => _defending   = AttributeScale.ClampSkill(value); }
        public int Positioning { get => _positioning; set => _positioning = AttributeScale.ClampSkill(value); }
        public int Goalkeeping { get => _goalkeeping; set => _goalkeeping = AttributeScale.ClampSkill(value); }

        /// <summary>Number of skills addressable by <see cref="this[int]"/>.</summary>
        public const int SkillCount = 10;

        /// <summary>
        /// Skill access by index, in the canonical order used everywhere in the engine:
        /// 0 Pace, 1 Strength, 2 Stamina, 3 Technique, 4 Passing, 5 Dribbling, 6 Shooting,
        /// 7 Defending, 8 Positioning, 9 Goalkeeping (see <see cref="PlayerRating"/>'s weight
        /// table). Setters clamp like the named properties. Lets systems iterate skills
        /// generically (e.g. the training model) without a duplicated switch.
        /// </summary>
        public int this[int skillIndex]
        {
            get => skillIndex switch
            {
                0 => _pace,
                1 => _strength,
                2 => _stamina,
                3 => _technique,
                4 => _passing,
                5 => _dribbling,
                6 => _shooting,
                7 => _defending,
                8 => _positioning,
                9 => _goalkeeping,
                _ => throw new System.IndexOutOfRangeException($"skillIndex {skillIndex} out of [0,{SkillCount - 1}]")
            };
            set
            {
                switch (skillIndex)
                {
                    case 0: Pace = value; break;
                    case 1: Strength = value; break;
                    case 2: Stamina = value; break;
                    case 3: Technique = value; break;
                    case 4: Passing = value; break;
                    case 5: Dribbling = value; break;
                    case 6: Shooting = value; break;
                    case 7: Defending = value; break;
                    case 8: Positioning = value; break;
                    case 9: Goalkeeping = value; break;
                    default: throw new System.IndexOutOfRangeException($"skillIndex {skillIndex} out of [0,{SkillCount - 1}]");
                }
            }
        }
    }
}
