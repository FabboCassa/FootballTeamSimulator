namespace Fts.Services.Navigation
{
    /// <summary>Published by ScreenNavigator whenever the visible screen changes.</summary>
    public readonly struct ScreenChangedMessage
    {
        public readonly string ScreenName;
        public readonly int StackDepth;

        public ScreenChangedMessage(string screenName, int stackDepth)
        {
            ScreenName = screenName;
            StackDepth = stackDepth;
        }
    }
}
