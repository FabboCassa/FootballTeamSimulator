namespace Fts.BalanceHarness;

/// <summary>
/// Command line of the balance harness. Everything is parameterised with a cheap default so a quick
/// regression run costs seconds, and the roadmap-sized run is one flag away (--long).
/// </summary>
internal sealed class HarnessOptions
{
    /// <summary>tactics | economy | difficulty | ladder | world | pitch | all.
    /// "pitch" is never part of "all": see Program.cs.</summary>
    public string Scenario { get; private set; } = "all";

    /// <summary>Root seed. Every world/match seed in the run is derived from it, so a run replays exactly.</summary>
    public ulong Seed { get; private set; } = 20_260_803;

    // --- pitch (phase 0 of the match engine rework) --------------------------------------------

    /// <summary>Matches measured for the shape/laws readings. A hundred is enough to settle the
    /// shape averages to well inside a metre, and costs seconds.</summary>
    public int PitchMatches { get; private set; } = 100;

    /// <summary>Turn the "against real football" bands into real PASS/FAIL checks. Off by default:
    /// phase 0 measures the gap, it does not close it, and a harness that always exits 1 stops
    /// being read. A phase that claims to have closed a band turns this on to keep it closed.</summary>
    public bool PitchStrict { get; private set; }

    /// <summary>Where to write the self-contained HTML replay of one match, if anywhere.</summary>
    public string? PitchDump { get; private set; }

    /// <summary>Which of the measured matches to write out. Index into the run, from 0.</summary>
    public int PitchDumpMatch { get; private set; }

    // --- tactics ------------------------------------------------------------------------------

    /// <summary>Repeats of every ordered instruction pairing in the 81-tactic field sweep.</summary>
    public int TacticRepeats { get; private set; } = 2;

    /// <summary>Repeats of every ordered formation pairing. Only 30 ordered pairs exist, so games are cheap
    /// here and precision is worth buying: 600 repeats = 3,000 games per shape, i.e. a standard error near
    /// 0.9 points of share, which is what it takes to tell a real gap from a wobble.</summary>
    public int FormationRepeats { get; private set; } = 600;

    /// <summary>Full league seasons used to price the best tactic against the neutral one.</summary>
    public int TacticSeasons { get; private set; } = 6;

    // --- economy ------------------------------------------------------------------------------

    public int EconomyWorlds { get; private set; } = 2;
    public int EconomySeasons { get; private set; } = 4;

    // --- difficulty ---------------------------------------------------------------------------

    /// <summary>Seasons per difficulty level PER isolation variant (identical user behaviour, only the AI
    /// changes). Five variants x three levels x this many seasons, so it is the run's main cost.</summary>
    public int DifficultySeasons { get; private set; } = 10;

    // --- ladder -------------------------------------------------------------------------------

    public int LadderSeasons { get; private set; } = 24;

    /// <summary>Share of the pyramid's seats held by human coaches; the rest play as AI. The ladder's
    /// mobility depends on a free seat existing somewhere, so this is swept, not fixed.</summary>
    public int[] LadderFillPercents { get; private set; } = { 50, 75, 100 };

    /// <summary>Spread of the coaches' latent skill, in Elo-equivalent points (std-dev is about a
    /// quarter of it). 400 puts the best coaches roughly a class above the worst.</summary>
    public int LadderSkillSpread { get; private set; } = 400;

    // --- world (task 11.1) --------------------------------------------------------------------

    /// <summary>Nation code played in full detail while the presets are measured around it.</summary>
    public string WorldNation { get; private set; } = "ITA";

    /// <summary>How many of that nation's tiers run at full detail.</summary>
    public int WorldTiers { get; private set; } = 3;

    public bool Long { get; private set; }

    public static HarnessOptions Parse(string[] args)
    {
        var o = new HarnessOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            string? value = i + 1 < args.Length ? args[i + 1] : null;

            switch (key)
            {
                case "--scenario" when value is not null: o.Scenario = value.ToLowerInvariant(); i++; break;
                case "--seed" when value is not null: o.Seed = ulong.Parse(value); i++; break;
                case "--pitch-matches" when value is not null: o.PitchMatches = int.Parse(value); i++; break;
                case "--pitch-dump" when value is not null: o.PitchDump = value; i++; break;
                case "--pitch-dump-match" when value is not null: o.PitchDumpMatch = int.Parse(value); i++; break;
                case "--pitch-strict": o.PitchStrict = true; break;
                case "--tactic-repeats" when value is not null: o.TacticRepeats = int.Parse(value); i++; break;
                case "--formation-repeats" when value is not null: o.FormationRepeats = int.Parse(value); i++; break;
                case "--tactic-seasons" when value is not null: o.TacticSeasons = int.Parse(value); i++; break;
                case "--economy-worlds" when value is not null: o.EconomyWorlds = int.Parse(value); i++; break;
                case "--economy-seasons" when value is not null: o.EconomySeasons = int.Parse(value); i++; break;
                case "--difficulty-seasons" when value is not null: o.DifficultySeasons = int.Parse(value); i++; break;
                case "--ladder-seasons" when value is not null: o.LadderSeasons = int.Parse(value); i++; break;
                case "--ladder-skill-spread" when value is not null: o.LadderSkillSpread = int.Parse(value); i++; break;
                case "--world-nation" when value is not null: o.WorldNation = value.ToUpperInvariant(); i++; break;
                case "--world-tiers" when value is not null: o.WorldTiers = int.Parse(value); i++; break;
                case "--ladder-fill" when value is not null:
                    o.LadderFillPercents = ParseInts(value); i++; break;
                case "--long":
                    o.Long = true;
                    o.TacticRepeats = 4;
                    o.FormationRepeats = 1500;
                    o.TacticSeasons = 12;
                    o.EconomyWorlds = 4;
                    o.EconomySeasons = 8;
                    o.DifficultySeasons = 16;
                    o.LadderSeasons = 40;
                    o.PitchMatches = 400;
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine(Usage);
                    Environment.Exit(0);
                    break;
                default:
                    if (key.StartsWith("--", StringComparison.Ordinal))
                    {
                        Console.WriteLine($"Unknown option '{key}'.");
                        Console.WriteLine(Usage);
                        Environment.Exit(2);
                    }
                    break;
            }
        }

        return o;
    }

    private static int[] ParseInts(string csv)
    {
        string[] parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) values[i] = int.Parse(parts[i]);
        return values;
    }

    public const string Usage = """
        fts-balance - Phase 10.1 balance harness

          --scenario <tactics|economy|difficulty|ladder|world|pitch|all>   what to measure (default: all)
                                        "pitch" is asked for BY NAME - it is the match engine
                                        rework instrument and is not part of "all"
          --seed <n>                    root seed (default 20260803); a run replays exactly
          --long                        the roadmap-sized run (slower, tighter numbers)

          --pitch-matches <n>           matches measured by the pitch scenario (default 100)
          --pitch-strict                turn the "against real football" bands into PASS/FAIL checks
          --pitch-dump <path>           write one match as a self-contained HTML replay
          --pitch-dump-match <n>        which match to write out (default 0)

          --tactic-repeats <n>          repeats per instruction pairing (default 2 => 12,960 matches)
          --formation-repeats <n>       repeats per formation pairing (default 600 = 3,000 games/shape)
          --tactic-seasons <n>          seasons pricing best-tactic vs neutral (default 6)
          --economy-worlds <n>          worlds run for the economy scenario (default 2)
          --economy-seasons <n>         seasons per economy world (default 4)
          --difficulty-seasons <n>      seasons per level PER isolation variant (default 10)
          --ladder-seasons <n>          ranked seasons simulated (default 24)
          --ladder-fill <a,b,c>         seat occupancy percentages to sweep (default 50,75,100)
          --ladder-skill-spread <n>     latent coach-skill spread in Elo points (default 400)
          --world-nation <CODE>         nation played in full detail by the world bench (default ITA)
          --world-tiers <n>             tiers of it at full detail (default 3)

        Exit code 0 when every check passes, 1 otherwise.
        """;
}
