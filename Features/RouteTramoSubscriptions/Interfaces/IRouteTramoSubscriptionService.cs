using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.Interfaces;

public interface IRouteTramoSubscriptionService
{
    Task RecordSubscriptionRequestAsync(
        RecordRouteTramoSubscriptionRequestArgs request,
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
    /// Vendedor del hilo: confirma suscripciones pendientes del transportista en la hoja publicada (todas o solo <paramref name="action.StopId"/>),
    /// actualiza teléfonos en la hoja persistida y notifica al carrier.
    /// </summary>
    /// <returns>Número de filas pasadas a confirmado; null si no aplica.</returns>
    Task<int?> AcceptCarrierPendingOnSheetAsync(
        TramoSellerSheetAction action,
        CancellationToken cancellationToken = default);

    /// <summary>Solo vendedor del hilo: rechaza solicitudes pendientes (todas o solo <paramref name="action.StopId"/>) y notifica con enlace a la oferta de ruta.</summary>
    Task<int?> RejectCarrierPendingOnSheetAsync(
        TramoSellerSheetAction action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transportista: se retira del hilo (des-suscripción a tramos, limpieza de teléfonos en hoja).
    /// En la demo, la baja de confianza aplica con tramos confirmados solo si alguna hoja implicada no está en estado «entregada»,
    /// salvo que ya haya salido del hilo al menos una de las partes del acuerdo (comprador o vendedor; <c>BuyerExpelledAtUtc</c> o <c>SellerExpelledAtUtc</c>).
    /// </summary>
    Task<CarrierWithdrawFromThreadResult?> WithdrawCarrierFromThreadAsync(
        string carrierUserId,
        string threadId,
        string withdrawReason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vendedor del hilo: retira a un transportista. Con <paramref name="routeSheetId"/> y <paramref name="stopId"/>
    /// solo ese tramo; sin ellos, todos los tramos activos del transportista en el hilo. Requiere al menos un tramo
    /// <c>confirmed</c> en el conjunto retirado; por cada tramo confirmado retirado aplica un ajuste de confianza a la
    /// tienda (demo). Si no quedan suscripciones activas, el transportista pierde el acceso al chat del hilo.
    /// </summary>
    Task<CarrierExpelledBySellerResult?> ExpelCarrierBySellerFromThreadAsync(
        string sellerUserId,
        string threadId,
        string carrierUserId,
        string reason,
        string? routeSheetId = null,
        string? stopId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transportista cuyo teléfono figura en la hoja: <c>Accepted</c> suscribe; si no, notifica rechazo al vendedor.
    /// </summary>
    Task<bool> CarrierRespondPreselectedRouteInviteAsync(
        CarrierPreselInviteRequest request,
        CancellationToken cancellationToken = default);
}
