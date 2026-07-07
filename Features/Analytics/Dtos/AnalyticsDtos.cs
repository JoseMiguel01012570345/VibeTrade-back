namespace VibeTrade.Backend.Features.Analytics.Dtos;

/// <summary>La IP se determina en el servidor (X-Forwarded-For / RemoteIpAddress); el cliente no la envía.</summary>
public sealed record PageViewRequest(string? SessionKey, string? Path);

public sealed record ProductViewRequest(string? SessionKey, string? ProductId);
