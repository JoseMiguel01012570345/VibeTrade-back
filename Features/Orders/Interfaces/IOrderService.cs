using VibeTrade.Backend.Features.Orders.Dtos;

namespace VibeTrade.Backend.Features.Orders.Interfaces;

/// <summary>Error de operación de pedido con código estable para el cliente.</summary>
public sealed record OrderError(string Code, string Message);

/// <summary>Casos de uso del aggregate Pedido: checkout, ciclo de estados, evidencia y rastreo.</summary>
public interface IOrderService
{
    Task<(CheckoutPreviewResponse? Value, OrderError? Error)> PreviewAsync(
        string buyerUserId,
        CreateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<(CreateOrderResponse? Value, OrderError? Error)> CreateAsync(
        string buyerUserId,
        CreateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<(OrderDetailDto? Value, OrderError? Error)> GetForSellerAsync(
        string userId,
        string orderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummaryDto>> ListForStoreAsync(
        string userId,
        string storeId,
        CancellationToken cancellationToken = default);

    Task<(OrderDetailDto? Value, OrderError? Error)> AdvanceStatusAsync(
        string userId,
        string orderId,
        string toStatus,
        CancellationToken cancellationToken = default);

    Task<(OrderDetailDto? Value, OrderError? Error)> UploadClientEvidenceAsync(
        string userId,
        string orderId,
        IReadOnlyList<string> urls,
        string? note,
        CancellationToken cancellationToken = default);

    Task<(OrderDetailDto? Value, OrderError? Error)> InvalidateAsync(
        string userId,
        string orderId,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummaryDto>> ListMyOrdersAsync(
        string buyerUserId,
        CancellationToken cancellationToken = default);

    Task<(OrderTrackingDto? Value, OrderError? Error)> GetTrackingByPublicNumberAsync(
        string buyerUserId,
        string publicNumber,
        CancellationToken cancellationToken = default);

    Task<(OrderTrackingDto? Value, OrderError? Error)> DecideClientEvidenceAsync(
        string buyerUserId,
        string orderId,
        bool accept,
        string? rejectReason,
        CancellationToken cancellationToken = default);
}
