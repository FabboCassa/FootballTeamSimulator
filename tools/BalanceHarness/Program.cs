using System.Diagnostics;
using Fts.BalanceHarness;
using Sim.Core.Config;

// ---------------------------------------------------------------------------------------------
// Phase 10.1 balance harness. Prints one [balance-*] line per measurement, then a PASS/FAIL block,
// then exits 0 (all checks passed) or 1. The lines under "judgement calls" are deliberately NOT
// checks: their right value is a design decision, and the working agreement is that we read them
// together before touching BalanceConfig.
// ---------------------------------------------------------------------------------------------

HarnessOptions options = HarnessOptions.Parse(args);
var config = new BalanceConfig();
var checks = new CheckList();
var clock = Stopwatch.StartNew();

Console.WriteLine("fts-balance - Football Team Simulator balance harness (task 10.1)");
Console.WriteLine($"scenario={options.Scenario} seed={options.Seed} profile={(options.Long ? "long" : "default")}");
Console.WriteLine($"BalanceConfig version {config.Version}");

bool all = options.Scenario == "all";
if (all || options.Scenario == "tactics") TacticsScenario.Run(options, config, checks);
if (all || options.Scenario == "economy") EconomyScenario.Run(options, config, checks);
if (all || options.Scenario == "difficulty") DifficultyScenario.Run(options, config, checks);
if (all || options.Scenario == "ladder") LadderScenario.Run(options, checks);
if (all || options.Scenario == "world") WorldScenario.Run(options, config, checks);

// Deliberately NOT part of "all": the pitch scenario is the measuring instrument of the match
// engine rework (phase 0, docs/engine/MATCH_ENGINE_PLAN.md), and today it reports a match that
// is a long way from football. Folding that into the default run would turn every balance run
// red and hide a real regression in the other five. Ask for it by name.
if (options.Scenario == "pitch") PitchScenario.Run(options, config, checks);

// Nor is this one, and for the same reason plus one of its own: the `instructions` scenario plays
// the same fixtures twelve times over to isolate one axis at a time (engine phase 8), which is a
// measurement of the MOVEMENT layer and has nothing to say about the shipped balance. It answers
// the only question phase 8 asks - when the coach asks for something, does the picture do it?
if (options.Scenario == "instructions") InstructionsScenario.Run(options, config, checks);

if (checks.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine($"Nothing ran - unknown scenario '{options.Scenario}'.");
    Console.WriteLine(HarnessOptions.Usage);
    return 2;
}

checks.Print("balance");
clock.Stop();
Console.WriteLine($"  finished in {clock.Elapsed.TotalSeconds:F1}s");
return checks.AllPassed ? 0 : 1;
