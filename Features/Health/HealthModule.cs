using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VibeTrade.Backend.Features.Health;

public static class HealthModule
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", GetHealthAsync)
            .WithTags("Health")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetHealthAsync(
        HealthCheckService healthChecks,
        CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(cancellationToken);
        if (report.Status == HealthStatus.Healthy)
            return Results.Ok(new { status = report.Status.ToString() });

        return Results.Json(
            new
            {
                status = report.Status.ToString(),
                entries = report.Entries.ToDictionary(
                    e => e.Key,
                    e => new { status = e.Value.Status.ToString(), e.Value.Description, e.Value.Duration }),
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
