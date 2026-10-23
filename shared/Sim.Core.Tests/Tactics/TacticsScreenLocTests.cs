using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Tactics
{
    /// <summary>
    /// Issue #99 (spec R21): every string the Tattiche screen asks for — formation names, style
    /// names and one-line descriptions, instruction captions, descriptions and levels — exists in
    /// both client string tables with the expected placeholders.
    /// </summary>
    [TestFixture]
    public class TacticsScreenLocTests
    {
        private static readonly (string Key, int Args)[] FixedKeys =
        {
            ("tactics.style_caption", 0), ("tactics.style.suggested", 0), ("tactics.style.custom", 1),
            ("tactics.formation_shape", 2), ("tactics.formation_caption", 0)
        };

        private static readonly Regex Placeholder = new Regex(@"\{\d+\}");

        private static IEnumerable<(string Key, int Args)> ScreenKeys()
        {
            foreach ((string key, int args) in FixedKeys) yield return (key, args);
            foreach (Formation f in Formations.Catalogue) yield return (TacticsLocKeys.Formation(f), 0);
            foreach (PlayingStyle s in Enum.GetValues(typeof(PlayingStyle)))
            {
                yield return (TacticsLocKeys.StyleName(s), 0);
                yield return (TacticsLocKeys.StyleDescription(s), 0);
            }
            foreach (InstructionPhase p in Enum.GetValues(typeof(InstructionPhase)))
                yield return (TacticsLocKeys.Phase(p), 0);
            foreach (InstructionAxis a in TacticEditor.Axes)
            {
                yield return (TacticsLocKeys.AxisName(a), 0);
                yield return (TacticsLocKeys.AxisDescription(a), 0);
                for (int level = 0; level < TacticEditor.LevelCount(a); level++)
                    yield return (TacticsLocKeys.Level(a, level), 0);
            }
        }

        [Test]
        public void Keys_FollowTheExistingNaming_SoTheV11StringsAreReused()
        {
            Assert.Multiple(() =>
            {
                Assert.That(TacticsLocKeys.Formation(Formation.F433), Is.EqualTo("tactics.formation.f433"));
                Assert.That(TacticsLocKeys.Formation(Formation.F442Diamond), Is.EqualTo("tactics.formation.f442diamond"));
                Assert.That(TacticsLocKeys.AxisName(InstructionAxis.Mentality), Is.EqualTo("tactics.axis.mentality"));
                Assert.That(TacticsLocKeys.Level(InstructionAxis.Pressing, (int)Pressing.High), Is.EqualTo("tactics.pressing.high"));
                Assert.That(TacticsLocKeys.Level(InstructionAxis.DefensiveLine, (int)DefensiveLine.VeryDeep),
                    Is.EqualTo("tactics.defensive_line.very_deep"));
                Assert.That(TacticsLocKeys.StyleName(PlayingStyle.TikiTaka), Is.EqualTo("tactics.style.tiki_taka"));
                Assert.That(TacticsLocKeys.StyleDescription(PlayingStyle.TikiTaka), Is.EqualTo("tactics.style.tiki_taka.desc"));
            });
        }

        [TestCase("en.json")]
        [TestCase("it.json")]
        public void EveryTacticsScreenString_IsThere_WithItsPlaceholders(string file)
        {
            Dictionary<string, string> table = Load(file);

            Assert.Multiple(() =>
            {
                foreach ((string key, int args) in ScreenKeys())
                {
                    Assert.That(table.TryGetValue(key, out string? text), Is.True, $"{file} misses {key}");
                    if (text == null) continue;

                    string[] expected = Enumerable.Range(0, args).Select(i => "{" + i + "}").ToArray();
                    string[] found = Placeholder.Matches(text).Select(m => m.Value).Distinct().OrderBy(v => v).ToArray();
                    Assert.That(found, Is.EqualTo(expected), $"placeholders of {key} in {file}");
                    Assert.That(text.Trim(), Is.Not.Empty, $"{key} in {file}");
                    Assert.That(text, Does.Not.Contain("\n"), $"{key} in {file} is one line");
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
