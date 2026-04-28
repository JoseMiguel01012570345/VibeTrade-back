using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VibeTrade.Backend.Api;

/// <summary>Estado del servicio y dependencias (p. ej. PostgreSQL).</summary>
[ApiController]
[Route("health")]
[Tags("Health")]
public sealed class HealthController(HealthCheckService healthChecks) : ControllerBase
{
    /// <summary>Ejecuta los health checks registrados (incluye base de datos).</summary>
    /// <returns>200 con <c>status: Healthy</c> o 503 con detalle por dependencia.</returns>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(cancellationToken);
        if (report.Status == HealthStatus.Healthy)
            return Ok(new { status = report.Status.ToString() });

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            status = report.Status.ToString(),
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), e.Value.Description, e.Value.Duration }),
        });
    }
}
