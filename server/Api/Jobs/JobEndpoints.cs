using Fts.Application.Notifications;
using Fts.Infrastructure.Jobs;
using Hangfire;

namespace Fts.Api.Jobs;

/// <summary>
/// Dev-only diagnostics for the Hangfire scheduler (Phase 7.4): enqueue a job on demand so you can
/// watch it run in the dashboard, and fire a push through the same background-job path Phase 8 will
/// use for real (outbid alerts, match reminders). Mapped only in non-Production and behind the
/// <c>Jobs:ExposeTestEndpoint</c> flag (see Program.cs) — off in prod.
/// </summary>
public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/jobs");

        // Enqueue an immediate heartbeat — returns the Hangfire job id; watch it complete on /hangfire.
        group.MapPost("/heartbeat", (IBackgroundJobClient jobs) =>
        {
            var jobId = jobs.Enqueue<HeartbeatJob>(j => j.ExecuteAsync(CancellationToken.None));
            return Results.Ok(new { jobId });
        });

        // Enqueue a push to a given account through a background job (the Phase-8 pattern). Returns
        // the job id; with FCM unconfigured the job logs+skips, with FCM live the device receives it.
        group.MapPost("/push/{userId:guid}", (Guid userId, PushMessage message, IBackgroundJobClient jobs) =>
        {
            var jobId = jobs.Enqueue<INotificationService>(
                s => s.SendToUserAsync(userId, message, CancellationToken.None));
            return Results.Ok(new { jobId });
        });

        return app;
    }
}
