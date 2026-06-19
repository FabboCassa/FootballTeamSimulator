using System;
using Sim.Core.Domain;

namespace Fts.Presenters
{
    /// <summary>
    /// Turns a player's <see cref="PlayerCondition"/> into the small display bits the
    /// squad/lineup rows show (task 4.2): a form arrow, a morale face, a fitness value
    /// for the bar, and a tooltip that always spells out the <em>cause</em> of every
    /// value — the anti-frustration "transparency" rule from ARCHITECTURE.md §4.4.
    ///
    /// Pure presentation: all wording goes through the translate delegate (loc keys
    /// under <c>condition.*</c>), so nothing here is hardcoded user-facing text.
    /// </summary>
    public readonly struct ConditionDisplay
    {
        public readonly string FormArrow;
        public readonly string MoraleFace;
        public readonly int Fitness;
        public readonly string Tooltip;

        private ConditionDisplay(string formArrow, string moraleFace, int fitness, string tooltip)
        {
            FormArrow = formArrow;
            MoraleFace = moraleFace;
            Fitness = fitness;
            Tooltip = tooltip;
        }

        public static ConditionDisplay Build(PlayerCondition c, Func<string, string> tr)
        {
            string tooltip =
                Fmt(tr, "condition.tooltip.fitness", c.Fitness, tr(FitnessCauseKey(c.Fitness))) + "\n" +
                Fmt(tr, "condition.tooltip.form", tr(FormCauseKey(c.Form))) + "\n" +
                Fmt(tr, "condition.tooltip.morale", tr(MoraleCauseKey(c.Morale)));

            return new ConditionDisplay(FormArrowFor(c.Form), MoraleFaceFor(c.Morale), c.Fitness, tooltip);
        }

        // ---- glyphs (geometric-shapes block, same family as the ● already used in the roster) ----

        private static string FormArrowFor(int form) =>
            form >= 70 ? "▲▲"   // ▲▲ excellent
          : form >= 58 ? "▲"          // ▲ good
          : form >= 43 ? "▬"          // ▬ steady
          : form >= 31 ? "▼"          // ▼ off form
          :              "▼▼";   // ▼▼ out of form

        private static string MoraleFaceFor(int morale) =>
            morale >= 70 ? ":D"
          : morale >= 58 ? ":)"
          : morale >= 43 ? ":|"
          : morale >= 31 ? ":("
          :                ":((";

        // ---- cause keys (each band has its own explanation, so low values always say why) ----

        private static string FitnessCauseKey(int fitness) =>
            fitness >= 85 ? "condition.fitness.fresh"
          : fitness >= 70 ? "condition.fitness.fatigued"
          : fitness >= 50 ? "condition.fitness.tired"
          :                 "condition.fitness.exhausted";

        private static string FormCauseKey(int form) =>
            form >= 70 ? "condition.form.hot"
          : form >= 58 ? "condition.form.good"
          : form >= 43 ? "condition.form.steady"
          : form >= 31 ? "condition.form.poor"
          :              "condition.form.cold";

        private static string MoraleCauseKey(int morale) =>
            morale >= 70 ? "condition.morale.happy"
          : morale >= 58 ? "condition.morale.content"
          : morale >= 43 ? "condition.morale.settled"
          : morale >= 31 ? "condition.morale.low"
          :                "condition.morale.unhappy";

        private static string Fmt(Func<string, string> tr, string key, params object[] args) =>
            string.Format(tr(key), args);
    }
}
