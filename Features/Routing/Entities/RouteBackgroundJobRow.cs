namespace VibeTrade.Backend.Features.Routing.Entities;

/// <summary>
/// Trabajo en cola de rutas (DB-backed). El worker lo reclama y el procesador lo despacha
/// por <see cref="JobType"/>. Réplica adaptada del subsistema de referencia.
/// </summary>
public sealed class RouteBackgroundJobRow
{
    public string Id { get; set; } = "";

    /// <summary><see cref="RouteBackgroundJobTypes"/>.</summary>
    public string JobType { get; set; } = "";

    /// <summary><see cref="RouteBackgroundJobStatuses"/>.</summary>
    public string Status { get; set; } = RouteBackgroundJobStatuses.Pending;

    /// <summary>Hilo de chat correlacionado (hoja de ruta vive en el hilo).</summary>
    public string? ThreadId { get; set; }

    /// <summary>Hoja de ruta correlacionada.</summary>
    public string? RouteSheetId { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? LastError { get; set; }
}
