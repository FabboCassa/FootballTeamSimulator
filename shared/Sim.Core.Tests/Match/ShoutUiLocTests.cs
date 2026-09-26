using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The strings of the shouts UI (issue #44) — the in-match shout row and the plan rule editor —
    /// exist in both client string tables with the same placeholders, so neither language shows a
    /// raw key or drops a number.
    /// </summary>
    [TestFixture]
    public class ShoutUiLocTests
    {
        private static readonly (string Key, int Args)[] Keys =
        {
            ("match.shout.press_high", 0), ("match.shout.keep_ball", 0), ("match.shout.all_forward", 0),
            ("match.shout.encourage", 0), ("match.shout.concentrate", 0),
            ("inmatch.shouts", 0), ("inmatch.shout.ready", 0), ("inmatch.shout.pending", 0),
            ("inmatch.shout.active", 3), ("inmatch.shout.cooldown", 1),
            ("tactics.plan_caption", 0), ("tactics.plan.empty", 0), ("tactics.plan.rule", 3),
            ("tactics.plan.minute", 1), ("tactics.plan.from", 0), ("tactics.plan.if", 0),
            ("tactics.plan.shout", 0), ("tactics.plan.add", 0), ("tactics.plan.remove", 0),
            ("tactics.plan.full", 1),
            ("tactics.plan.when.always", 0), ("tactics.plan.when.losing", 0), ("tactics.plan.when.drawing", 0),
            ("tactics.plan.when.winning", 0), ("tactics.plan.when.not_winning", 0), ("tactics.plan.when.not_losing", 0),
            ("tactics.plan.action.shout", 1), ("tactics.plan.action.tactic", 0), ("tactics.plan.action.sub", 0),
            ("tactics.plan.action.join", 2)
        };

        private static readonly Regex Placeholder = new Regex(@"\{\d+\}");

        [TestCase("en.json")]
        [TestCase("it.json")]
        public void EveryShoutUiString_IsThere_WithItsPlaceholders(string file)
        {
            Dictionary<string, string> table = Load(file);

            Assert.Multiple(() =>
            {
                foreach ((string key, int args) in Keys)
                {
                    Assert.That(table.TryGetValue(key, out string? text), Is.True, $"{file} misses {key}");
                    if (text == null) continue;

                    string[] expected = Enumerable.Range(0, args).Select(i => "{" + i + "}").ToArray();
                    string[] found = Placeholder.Matches(text).Select(m => m.Value).Distinct().OrderBy(v => v).ToArray();
                    Assert.That(found, Is.EqualTo(expected), $"placeholders of {key} in {file}");
                    Assert.That(text.Trim(), Is.Not.Empty, $"{key} in {file}");
                }
            });
        }

        private static Dictionary<string, string> Load(string file)
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FootballTeamSimulator.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null, "repo root containing FootballTeamSimulator.sln");

            string path = Path.Combine(dir!, "client", "Assets", "Resources", "Localization", file);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }
    }
}
