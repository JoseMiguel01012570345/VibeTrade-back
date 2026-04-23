namespace VibeTrade.Backend.Features.Chat;

public interface IRouteTramoSubscriptionService
{
    Task RecordSubscriptionRequestAsync(
        string threadId,
        string routeSheetId,
        string stopId,
        int stopOrden,
        string carrierUserId,
        string? storeServiceId,
        string transportServiceLabel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suscripciones en hojas <strong>publicadas en plataforma</strong> del hilo; enriquecidas con datos de perfil y hoja.
    /// </summary>
    Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> ListPublishedForThreadAsync(
        string viewerUserId,
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transportista autenticado: suscripciones persistidas para la publicación emergente (<c>emo_*</c>), sin depender del id del hilo en cliente.
    /// </summary>
    Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> ListForCarrierByEmergentPublicationAsync(
        string carrierUserId,
        string emergentOfferId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Comprador o vendedor del hilo: confirma todas las suscripciones pendientes del transportista en la hoja publicada,
    /// actualiza teléfonos en la hoja persistida y notifica al carrier.
    /// </summary>
    /// <returns>Número de filas pasadas a confirmado; null si no aplica.</returns>
    Task<int?> AcceptCarrierPendingOnSheetAsync(
        string actorUserId,
        string threadId,
        string routeSheetId,
        string carrierUserId,
        CancellationToken cancellationToken = default);
}

public sealed record RouteTramoSubscriptionItemDto(
    string RouteSheetId,
    string StopId,
    int Orden,
    string CarrierUserId,
    string DisplayName,
    string Phone,
    int TrustScore,
    string? StoreServiceId,
    string TransportServiceLabel,
    string Status,
    string OrigenLine,
    string DestinoLine,
    long CreatedAtUnixMs,
    string? CarrierServiceStoreId);
