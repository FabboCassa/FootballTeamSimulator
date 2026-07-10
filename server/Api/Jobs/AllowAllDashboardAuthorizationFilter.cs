using Hangfire.Dashboard;

namespace Fts.Api.Jobs;

/// <summary>
/// Lets anyone view the Hangfire dashboard (Phase 7.4). Safe only because the dashboard is mapped
/// exclusively in non-Production environments (see Program.cs) — Hangfire's default filter otherwise
/// allows local requests only, which blocks the dashboard from inside a Docker container during dev.
/// A real deployment never maps the dashboard, so this permissive filter never runs in prod.
/// </summary>
public sealed class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
