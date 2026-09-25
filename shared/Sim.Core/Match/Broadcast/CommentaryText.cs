using System;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// Writes a <see cref="CommentaryLine"/> out through a string table. The host supplies the lookup
    /// and the names, so the same line reads in any language and with whatever naming the screen has
    /// (a squad's surnames, or the shirt numbers of a bare online report).
    /// </summary>
    public static class CommentaryText
    {
        /// <param name="tr">The localisation lookup: key and format arguments to text.</param>
        /// <param name="nameOf">(home side, lineup slot) to a printable name.</param>
        /// <param name="clubOf">Home side or not to the club's name.</param>
        public static string Format(CommentaryLine line, Func<string, object[], string> tr,
            Func<bool, int, string> nameOf, Func<bool, string> clubOf)
        {
            string body = string.Empty;
            foreach (CommentaryClause step in line.Steps)
            {
                string text = Clause(step, tr, nameOf, clubOf);
                body = body.Length == 0 ? text : tr(CommentaryKeys.Join, new object[] { body, text });
            }

            if (line.Outcome.HasValue)
            {
                string outcome = Clause(line.Outcome.Value, tr, nameOf, clubOf);
                body = body.Length == 0 ? outcome : tr(CommentaryKeys.Result, new object[] { body, outcome });
            }

            body = Capitalised(body);
            return line.EndMinute != line.Minute
                ? tr(CommentaryKeys.Span, new object[] { line.Minute, line.EndMinute, body })
                : tr(CommentaryKeys.Line, new object[] { line.Minute, body });
        }

        private static string Clause(CommentaryClause c, Func<string, object[], string> tr,
            Func<bool, int, string> nameOf, Func<bool, string> clubOf)
        {
            string actor = c.Actor >= 0 ? nameOf(c.Home, c.Actor) : string.Empty;
            string target = c.Target >= 0 ? nameOf(c.TargetHome, c.Target) : string.Empty;
            return tr(c.Key, new object[] { actor, target, clubOf(c.Home) });
        }

        // Phrases are written in lower case so they chain mid-sentence; whichever one opens it is raised.
        private static string Capitalised(string s) =>
            s.Length == 0 || !char.IsLower(s[0]) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
