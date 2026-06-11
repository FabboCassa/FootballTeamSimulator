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
    }
}
