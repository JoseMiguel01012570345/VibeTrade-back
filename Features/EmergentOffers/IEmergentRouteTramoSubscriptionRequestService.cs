namespace VibeTrade.Backend.Features.EmergentOffers;

public interface IEmergentRouteTramoSubscriptionRequestService
{
    /// <summary>
    /// Valida servicio de transporte del usuario y notifica a comprador y vendedor del hilo.
    /// </summary>
    Task<(bool Ok, string? ErrorCode, string? Message)> RequestAsync(
        string carrierUserId,
        string emergentOfferId,
        string stopId,
        string storeServiceId,
        CancellationToken cancellationToken = default);
}
