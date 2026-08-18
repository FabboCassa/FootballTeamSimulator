namespace Fts.Infrastructure.Jobs;

/// <summary>What this process does (Roadmap 10.4). The 9.6 load test's standing recommendation was to
/// take the Hangfire worker out of the API process: while they share one, matchday resolution competes
/// with player requests for the same CPU, and the measured cost per fixture tripled under load. The same
/// image runs in either role — only <c>Jobs__Role</c> differs — so there is one build, one migration
/// path and one version to reason about.</summary>
public enum ProcessRole
{
    /// <summary>Serve the API and run the scheduler in the same process. The default, and what the local
    /// compose stack and every test host do, so nothing about a dev run changes.</summary>
    Both = 0,

    /// <summary>Serve the API only: no Hangfire server, no recurring jobs. Scale these out freely — they
    /// hold no scheduling state, and the Hangfire CLIENT is still registered so an auction settlement can
    /// be enqueued from a request and picked up by the worker.</summary>
    Api = 1,

    /// <summary>Run the scheduler only: Hangfire server + the recurring registrations, and NO gameplay
    /// endpoints mapped (only /health and /health/ready, so an orchestrator can still probe it). Exactly
    /// ONE of these should run — the recurring jobs are minutely and the calendar tick is not designed
    /// to be raced by a second scheduler.</summary>
    Worker = 2,
}

/// <summary>Reads <c>Jobs:Role</c>. Lives in Infrastructure because both the composition root (which
/// decides whether to add the Hangfire SERVER) and the Api (which decides what to map) need the same
/// answer, and they must not be able to disagree.</summary>
public static class JobsRoleReader
{
    public const string ConfigKey = "Jobs:Role";

    /// <summary>Parses the configured role. Unset or empty means <see cref="ProcessRole.Both"/> — the
    /// pre-10.4 behaviour. An unrecognised value THROWS rather than falling back: a typo'd
    /// <c>Jobs__Role=wroker</c> silently degrading to "both" would put a second scheduler on the ladder,
    /// which is the one mistake this setting exists to prevent.</summary>
    public static ProcessRole Read(Microsoft.Extensions.Configuration.IConfiguration config)
    {
        var raw = config[ConfigKey];
        if (string.IsNullOrWhiteSpace(raw)) return ProcessRole.Both;

        return raw.Trim().ToLowerInvariant() switch
        {
            "both" => ProcessRole.Both,
            "api" => ProcessRole.Api,
            "worker" => ProcessRole.Worker,
            _ => throw new InvalidOperationException(
                $"{ConfigKey} must be 'both', 'api' or 'worker' (got '{raw}')."),
        };
    }

    /// <summary>True when this process should map the gameplay/admin HTTP surface.</summary>
    public static bool ServesApi(this ProcessRole role) => role is ProcessRole.Both or ProcessRole.Api;

    /// <summary>True when this process should run the Hangfire server and own the recurring jobs.</summary>
    public static bool ProcessesJobs(this ProcessRole role) => role is ProcessRole.Both or ProcessRole.Worker;
}
