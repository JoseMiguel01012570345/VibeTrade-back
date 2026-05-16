namespace VibeTrade.Backend.Features.Logistics.Interfaces;

/// <summary>Pausa (IDLE / custodia tienda) y reanudación de un tramo por el vendedor del hilo.</summary>
public interface ISellerRouteStopDeliveryCustodyService
{
    /// <summary>
    /// Pone el tramo en <see cref="VibeTrade.Backend.Data.Entities.RouteStopDeliveryStates.IdleStoreCustody"/> y libera titular.
    /// Solo un tramo en IDLE por hoja a la vez.
    /// </summary>
    Task<SellerRouteStopCustodyResult> PauseForStoreCustodyAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Reanuda el tramo en <c>in_transit</c> con titular confirmado en ese stop.</summary>
    Task<SellerRouteStopCustodyResult> ResumeFromIdleAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        string targetCarrierUserId,
        CancellationToken cancellationToken = default);
}

public sealed record SellerRouteStopCustodyResult(bool Ok, string? ErrorCode, string? Message);
