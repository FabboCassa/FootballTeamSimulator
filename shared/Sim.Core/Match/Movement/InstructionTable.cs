namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// An instruction table (defensive · balanced · attacking, and so on) read at a spread: the
    /// distance of the chosen entry from the middle one, in percent. 100 is the table as it is;
    /// the middle entry is returned exactly at any spread, so a neutral side never moves (the
    /// invariant of <see cref="MovementTactics"/>). V11 reads every table this way (R8).
    /// </summary>
    public static class InstructionTable
    {
        private const int Middle = 1;

        /// <summary>An additive table; a missing entry is 0.</summary>
        public static int Pick(int[] table, int index, int spreadPercent)
        {
            int value = MovementTactics.Pick(table, index);
            if (spreadPercent == 100 || index == Middle) return value;
            int middle = MovementTactics.Pick(table, Middle);
            return middle + (value - middle) * spreadPercent / 100;
        }

        /// <summary>A percentage table; a missing entry is 100, the identity.</summary>
        public static int Percent(int[] table, int index, int spreadPercent)
        {
            int value = MovementTactics.Percent(table, index);
            if (spreadPercent == 100 || index == Middle) return value;
            int middle = MovementTactics.Percent(table, Middle);
            return middle + (value - middle) * spreadPercent / 100;
        }
    }
}
