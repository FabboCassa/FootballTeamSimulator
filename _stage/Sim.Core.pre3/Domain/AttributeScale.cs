namespace Sim.Core.Domain
{
    /// <summary>Value-range helpers shared by domain entities.</summary>
    public static class AttributeScale
    {
        public const int MinSkill = 1;
        public const int MaxSkill = 100;
        public const int MinCondition = 0;
        public const int MaxCondition = 100;

        public static int ClampSkill(int value) =>
            value < MinSkill ? MinSkill : value > MaxSkill ? MaxSkill : value;

        public static int ClampCondition(int value) =>
            value < MinCondition ? MinCondition : value > MaxCondition ? MaxCondition : value;
    }
}
