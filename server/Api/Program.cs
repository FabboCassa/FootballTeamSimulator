using Sim.Core;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Health endpoint - also proves the server runs the same Sim.Core DLL as the client.
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    simCore = SimCoreInfo.Version,
    utc = DateTime.UtcNow
}));

app.Run();

// Exposed for WebApplicationFactory in Api.Tests.
public partial class Program;
