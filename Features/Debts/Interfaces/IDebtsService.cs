using VibeTrade.Backend.Features.Debts.Dtos;

namespace VibeTrade.Backend.Features.Debts.Interfaces;

/// <summary>
/// Generación y liquidación de deudas (almacén/afiliado/transportista) del flujo de mercancía (wiki cap. 11).
/// Almacén/afiliado se liquidan manualmente; transportista se auto-liquida al aceptarse la evidencia.
/// </summary>
public interface IDebtsService
{
    /// <summary>Genera deuda de almacén (+ afiliado si aplica) al pasar el pedido a «En tránsito». Idempotente por pedido.</summary>
    Task RecordWarehouseAndAffiliateDebtsAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>Genera y auto-liquida las deudas de transportista de los tramos entregados del pedido. Idempotente por pedido.</summary>
    Task RecordCarrierDebtsOnDeliveredAsync(string orderId, CancellationToken cancellationToken = default);

    Task<DebtsOverviewDto> GetOverviewAsync(
        bool includeLiquidated,
        bool includeDeleted,
        CancellationToken cancellationToken = default);

    Task<(LiquidateDebtsResponse? Value, DebtError? Error)> LiquidateAsync(
        LiquidateDebtsRequest request,
        CancellationToken cancellationToken = default);
}
