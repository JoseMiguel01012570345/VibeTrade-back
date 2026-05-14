namespace VibeTrade.Backend.Data.Entities;

/// <summary>Hoja de ruta persistida en un hilo de chat (sincronizada desde el cliente).</summary>
public sealed class ChatRouteSheetRow
{
    public string ThreadId { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    public RouteSheetPayload Payload { get; set; } = new();

    public bool PublishedToPlatform { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Borrado lógico; la fila permanece para auditoría.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public string? DeletedByUserId { get; set; }
}
