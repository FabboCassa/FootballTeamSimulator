using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// One Sim.Core build driven through reflection, so the engine under test and an older reference
    /// build (e.g. the v11 DLL) run the very same calls in the same process. Every call goes through
    /// the same reflection path for both builds, so its overhead cannot favour either side.
    /// </summary>
    internal sealed class SimCoreHandle
    {
        private const ulong LeagueSeed = 20260611;
        private const ulong DefaultRngSequence = 54;

        private readonly object _config;
        private readonly ConstructorInfo _engine;
        private readonly ConstructorInfo _rng;
        private readonly MethodInfo _simulate;
        private readonly MethodInfo _hash;
        private readonly object _home;
        private readonly object _away;

        private SimCoreHandle(Assembly simCore, int homeClub, int awayClub)
        {
            Type configType = TypeOf(simCore, "Sim.Core.Config.BalanceConfig");
            Type engineType = TypeOf(simCore, "Sim.Core.Match.MatchEngine");
            Type rngType = TypeOf(simCore, "Sim.Core.Random.Pcg32");
            Type lineupType = TypeOf(simCore, "Sim.Core.Match.Lineup");
            Type clubType = TypeOf(simCore, "Sim.Core.Domain.Club");

            _config = Activator.CreateInstance(configType)!;
            _rng = rngType.GetConstructor(new[] { typeof(ulong), typeof(ulong) })!;
            _engine = engineType.GetConstructor(new[] { configType, typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })!;
            _simulate = engineType.GetMethod("Simulate", new[]
            {
                lineupType, lineupType, TypeOf(simCore, "Sim.Core.Random.IRandomSource"), TypeOf(simCore, "Sim.Core.Tactics.MatchTactics")
            })!;
            _hash = TypeOf(simCore, "Sim.Core.Match.MatchReportHasher").GetMethod("Hash")!;
            Version = (int)engineType.GetField("Version")!.GetRawConstantValue()!;

            Type generatorType = TypeOf(simCore, "Sim.Core.Generation.LeagueGenerator");
            object generator = generatorType.GetConstructors().Single(c => c.GetParameters().Length == 2).Invoke(new object?[] { null, null });
            object league = generatorType.GetMethod("Generate")!.Invoke(generator, new[] { Rng(LeagueSeed) })!;
            var clubs = (IList)league.GetType().GetProperty("Clubs")!.GetValue(league)!;
            MethodInfo bestEleven = TypeOf(simCore, "Sim.Core.Match.LineupSelector").GetMethod("BestEleven", new[] { clubType })!;
            _home = bestEleven.Invoke(null, new[] { clubs[homeClub] })!;
            _away = bestEleven.Invoke(null, new[] { clubs[awayClub] })!;
        }

        /// <summary>The build's <c>MatchEngine.Version</c>.</summary>
        public int Version { get; }

        /// <summary>The build already loaded in this process (the engine under test).</summary>
        public static SimCoreHandle Of(Assembly simCore, int homeClub, int awayClub) => new SimCoreHandle(simCore, homeClub, awayClub);

        /// <summary>A Sim.Core.dll from disk, isolated in its own load context so it can sit beside the current one.</summary>
        public static SimCoreHandle Load(string dllPath, int homeClub, int awayClub)
        {
            var context = new AssemblyLoadContext("sim-core-reference:" + dllPath, isCollectible: true);
            return new SimCoreHandle(context.LoadFromAssemblyPath(System.IO.Path.GetFullPath(dllPath)), homeClub, awayClub);
        }

        /// <summary>
        /// Plays one match on a fresh engine with the given flags (the home side is the first club when
        /// <paramref name="swapVenue"/> is false) and returns the report hash and the milliseconds
        /// <c>Simulate</c> took; building the engine is not timed.
        /// </summary>
        public (ulong Hash, double Ms) Play(ulong seed, bool swapVenue, bool generatePositions)
        {
            object engine = _engine.Invoke(new[] { _config, true, true, true, generatePositions, true });
            object rng = Rng(seed);
            long start = Stopwatch.GetTimestamp();
            object report = _simulate.Invoke(engine, new[] { swapVenue ? _away : _home, swapVenue ? _home : _away, rng, null })!;
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            return ((ulong)_hash.Invoke(null, new[] { report })!, ms);
        }

        private object Rng(ulong seed) => _rng.Invoke(new object[] { seed, DefaultRngSequence });

        private static Type TypeOf(Assembly simCore, string name) => simCore.GetType(name, throwOnError: true)!;
    }
}
