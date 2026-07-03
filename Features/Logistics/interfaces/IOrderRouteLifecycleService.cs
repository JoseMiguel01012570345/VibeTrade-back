namespace VibeTrade.Backend.Features.Logistics.Interfaces;

/// <summary>
/// Ciclo de vida de los tramos de la hoja de ruta de un <b>Pedido</b> (mercancía).
/// Re-parenta la logística del acuerdo (servicios) al pedido: al pasar a «En tránsito» los tramos
/// del transportista confirmado quedan pagados y se activa la titularidad del primer tramo.
/// </summary>
public interface IOrderRouteLifecycleService
{
    /// <summary>Vincula la hoja de ruta al pedido (rellena <c>Order.RouteSheetId</c> y <c>ChatRouteSheet.OrderId</c>).</summary>
    Task LinkRouteSheetAsync(
        string orderId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pedido → «En tránsito»: crea/actualiza los <c>RouteStopDeliveryRow</c> por tramo (pagados),
    /// otorga la titularidad del primer tramo a su transportista confirmado y registra el evento.
    /// No-op si el pedido no tiene hoja de ruta ligada.
    /// </summary>
    Task OnOrderInTransitAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>true</c> si el pedido no tiene tramos, o todos los tramos están en estado resuelto
    /// (evidencia aceptada o reembolsado). Puerta para la evidencia tienda → cliente.
    /// </summary>
    Task<bool> AllMerchandiseLegsResolvedAsync(string orderId, CancellationToken cancellationToken = default);
}
