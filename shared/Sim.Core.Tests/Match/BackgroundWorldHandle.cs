using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The background world of one Sim.Core build driven through reflection, like <see cref="SimCoreHandle"/>:
    /// a generated Large world whose whole background season one <c>AdvanceTo</c> call resolves through the
    /// fast model, so the per-call reflection cost is spread over thousands of fixtures. With
    /// <c>styled</c>, every background club plays a catalogue style, cycling: in its first suggested
    /// formation on the current build, and as the style's four v11 axes on a reference build that has
    /// no styles; otherwise every club plays without instructions.
    /// </summary>
    internal sealed class BackgroundWorldHandle
    {
        private const ulong WorldSeed = 20261023;

        private readonly object _progressor;
        private readonly MethodInfo _advance;
        private readonly object _world;
        private readonly object? _tactics;
        private readonly IList _fixtures;
        private readonly PropertyInfo _played;

        private BackgroundWorldHandle(Assembly simCore, bool styled)
        {
            object config = Activator.CreateInstance(TypeOf(simCore, "Sim.Core.Config.BalanceConfig"))!;
            _world = Generate(simCore, config);
            object season = _world.GetType().GetProperty("BackgroundSeason")!.GetValue(_world)!;
            _fixtures = (IList)season.GetType().GetProperty("Fixtures")!.GetValue(season)!;
            Type fixtureType = TypeOf(simCore, "Sim.Core.Domain.Fixture");
            _played = fixtureType.GetProperty("Played")!;

            Type progressorType = TypeOf(simCore, "Sim.Core.Career.BackgroundLeagueProgressor");
            _progressor = progressorType.GetConstructors().Single().Invoke(new[] { config });
            bool current = simCore == typeof(PlayingStyles).Assembly;
            _advance = progressorType.GetMethods().Single(m => m.Name == "AdvanceTo"
                && m.GetParameters()[3].ParameterType.GetGenericArguments()[1].Name == (styled && current ? "Tactic" : "TacticInstructions"));
            if (styled) _tactics = current ? StyledTactics(ClubIds(fixtureType)) : V11Instructions(simCore, ClubIds(fixtureType));
        }

        public int Fixtures => _fixtures.Count;

        public static BackgroundWorldHandle Of(Assembly simCore, bool styled) => new BackgroundWorldHandle(simCore, styled);

        public static BackgroundWorldHandle Load(string dllPath, bool styled)
        {
            var context = new AssemblyLoadContext("sim-core-background:" + dllPath, isCollectible: true);
            return new BackgroundWorldHandle(context.LoadFromAssemblyPath(System.IO.Path.GetFullPath(dllPath)), styled);
        }

        /// <summary>Marks every background fixture unplayed (not timed), then times one whole-season resolve; returns ms.</summary>
        public double ResolveSeason()
        {
            foreach (object fixture in _fixtures) _played.SetValue(fixture, false);
            long start = Stopwatch.GetTimestamp();
            int resolved = (int)_advance.Invoke(_progressor, new[] { _world, int.MaxValue, WorldSeed, _tactics })!;
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (resolved != _fixtures.Count) throw new InvalidOperationException($"resolved {resolved} of {_fixtures.Count}");
            return ms;
        }

        private static object Generate(Assembly simCore, object config)
        {
            Type scopeType = TypeOf(simCore, "Sim.Core.Domain.WorldScope");
            Type nationType = TypeOf(simCore, "Sim.Core.Domain.PlayableNation");
            Type optionsType = TypeOf(simCore, "Sim.Core.Generation.WorldGenerationOptions");
            Type generatorType = TypeOf(simCore, "Sim.Core.Generation.WorldGenerator");

            object scope = Activator.CreateInstance(scopeType)!;
            scopeType.GetProperty("Size")!.SetValue(scope, Enum.Parse(TypeOf(simCore, "Sim.Core.Domain.DatabaseSize"), "Large"));
            object nation = Activator.CreateInstance(nationType)!;
            nationType.GetProperty("Code")!.SetValue(nation, "ITA");
            ((IList)scopeType.GetProperty("Playable")!.GetValue(scope)!).Add(nation);
            object options = Activator.CreateInstance(optionsType)!;
            optionsType.GetProperty("Scope")!.SetValue(options, scope);

            object generator = generatorType.GetConstructor(new[] { optionsType, config.GetType() })!.Invoke(new[] { options, config });
            return generatorType.GetMethod("Generate", new[] { typeof(ulong) })!.Invoke(generator, new object[] { WorldSeed })!;
        }

        private SortedSet<int> ClubIds(Type fixtureType)
        {
            var ids = new SortedSet<int>();
            foreach (object fixture in _fixtures)
            {
                ids.Add((int)fixtureType.GetProperty("HomeClubId")!.GetValue(fixture)!);
                ids.Add((int)fixtureType.GetProperty("AwayClubId")!.GetValue(fixture)!);
            }

            return ids;
        }

        /// <summary>Club id → the next catalogue style in its first suggested formation.</summary>
        private static Dictionary<int, Tactic> StyledTactics(SortedSet<int> clubIds)
        {
            var tactics = new Dictionary<int, Tactic>();
            int next = 0;
            foreach (int id in clubIds)
            {
                StyleDefinition def = PlayingStyles.All[next++ % PlayingStyles.All.Count];
                tactics[id] = new Tactic(def.SuggestedFormations[0], def.Instructions);
            }

            return tactics;
        }

        /// <summary>Club id → the next catalogue style's four v11 axes, as the reference build's instruction set.</summary>
        private static object V11Instructions(Assembly simCore, SortedSet<int> clubIds)
        {
            Type set = TypeOf(simCore, "Sim.Core.Tactics.TacticInstructions");
            Type mentality = TypeOf(simCore, "Sim.Core.Tactics.Mentality"), pressing = TypeOf(simCore, "Sim.Core.Tactics.Pressing");
            Type tempo = TypeOf(simCore, "Sim.Core.Tactics.Tempo"), width = TypeOf(simCore, "Sim.Core.Tactics.Width");
            ConstructorInfo ctor = set.GetConstructor(new[] { mentality, pressing, tempo, width })!;

            var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(int), set))!;
            int next = 0;
            foreach (int id in clubIds)
            {
                TacticInstructions i = PlayingStyles.All[next++ % PlayingStyles.All.Count].Instructions;
                dictionary[id] = ctor.Invoke(new[]
                {
                    Enum.Parse(mentality, i.Mentality.ToString()), Enum.Parse(pressing, i.Pressing.ToString()),
                    Enum.Parse(tempo, i.Tempo.ToString()), Enum.Parse(width, i.Width.ToString())
                });
            }

            return dictionary;
        }

        private static Type TypeOf(Assembly simCore, string name) => simCore.GetType(name, throwOnError: true)!;
    }
}
