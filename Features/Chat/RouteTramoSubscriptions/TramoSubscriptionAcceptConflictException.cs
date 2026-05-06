namespace VibeTrade.Backend.Features.Chat.RouteTramoSubscriptions;

/// <summary>No se puede confirmar al transportista: los tramos pendientes ya tienen otro confirmado.</summary>
public sealed class TramoSubscriptionAcceptConflictException(string message) : Exception(message);
