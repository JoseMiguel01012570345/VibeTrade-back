namespace VibeTrade.Backend.Features.RouteTramoSubscriptions;

/// <summary>Constantes de política de suscripción a tramos (sin acoplar otras features al servicio).</summary>
public static class RouteTramoSubscriptionPolicy
{
    public const string AcceptCarrierPendingConflictMessage =
        "Los tramos pendientes de este transportista ya tienen otro transportista confirmado.";
}
