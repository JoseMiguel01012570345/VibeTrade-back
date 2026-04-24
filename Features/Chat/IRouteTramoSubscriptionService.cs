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
        string? carrierContactPhone = null,
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
    /// Vendedor del hilo: confirma suscripciones pendientes del transportista en la hoja publicada (todas o solo <paramref name="stopId"/>),
    /// actualiza teléfonos en la hoja persistida y notifica al carrier.
    /// </summary>
    /// <returns>Número de filas pasadas a confirmado; null si no aplica.</returns>
    Task<int?> AcceptCarrierPendingOnSheetAsync(
        string actorUserId,
        string threadId,
        string routeSheetId,
        string carrierUserId,
        string? stopId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Solo vendedor del hilo: rechaza solicitudes pendientes (todas o solo <paramref name="stopId"/>) y notifica con enlace a la oferta de ruta.</summary>
    Task<int?> RejectCarrierPendingOnSheetAsync(
        string actorUserId,
        string threadId,
        string routeSheetId,
        string carrierUserId,
        string? stopId = null,
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
