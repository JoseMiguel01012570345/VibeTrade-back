namespace VibeTrade.Backend.Features.Notifications.BroadcastingDtos;

/// <summary>Broadcast a participantes: suscripciones de tramo actualizadas.</summary>
public sealed record RouteTramoSubscriptionsBroadcastArgs(
    string ThreadId,
    string RouteSheetId,
    string Change,
    string ActorUserId,
    string? EmergentPublicationOfferId = null);
