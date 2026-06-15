namespace Sim.Core.Tactics
{
    /// <summary>One side's tactical setup for a match: the tactic plus how familiar the team is with it.</summary>
    public readonly struct TacticContext
    {
        public readonly Tactic Tactic;
        public readonly int Familiarity;

        public TacticContext(Tactic tactic, int familiarity)
        {
            Tactic = tactic;
            Familiarity = familiarity;
        }

        /// <summary>Neutral tactic at full familiarity: yields identity multipliers in the engine.</summary>
        public static TacticContext Neutral(int familiarityMax) =>
            new TacticContext(Tactic.Neutral, familiarityMax);
    }

    /// <summary>
    /// Both sides' tactical setups, passed (optionally) to MatchEngine.Simulate.
    /// When omitted, the engine runs exactly as before the tactics system (task 3.2),
    /// so existing seeds/golden masters are unchanged.
    /// </summary>
    public sealed class MatchTactics
    {
        public TacticContext Home { get; }
        public TacticContext Away { get; }

        public MatchTactics(TacticContext home, TacticContext away)
        {
            Home = home;
            Away = away;
        }
    }
}
