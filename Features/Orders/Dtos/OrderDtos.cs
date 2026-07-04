namespace VibeTrade.Backend.Features.Orders.Dtos;

/// <summary>Línea de carrito enviada por el comprador en el checkout.</summary>
public sealed record CheckoutCartLine(string ProductId, int Quantity);

/// <summary>Comando de creación de pedido (checkout).</summary>
public sealed record CreateOrderRequest(
    string CustomerFirstName,
    string CustomerLastName,
    string PhonePrimary,
    string? PhoneSecondary,
    string DeliveryMode,
    string? DeliveryAddress,
    double? DeliveryLatitude,
    double? DeliveryLongitude,
    string? PaymentMethod,
    string? AffiliateCode,
    IReadOnlyList<CheckoutCartLine> Lines);

/// <summary>Vista previa de importes del checkout (sin persistir).</summary>
public sealed record CheckoutPreviewResponse(
    string StoreId,
    string CurrencyCode,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total,
    decimal PricePerKm,
    double? RouteDistanceKm,
    IReadOnlyList<CheckoutPreviewLine> Lines);

public sealed record CheckoutPreviewLine(
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string CurrencyCode);

/// <summary>Resultado de crear el pedido.</summary>
public sealed record CreateOrderResponse(
    string OrderId,
    string PublicNumber,
    string Status,
    string CurrencyCode,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total);

public sealed record OrderLineDto(
    string Id,
    string? ProductId,
    string ProductName,
    string TechnicalSpecs,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string CurrencyCode);

/// <summary>Detalle de pedido para la tienda/admin.</summary>
public sealed record OrderDetailDto(
    string Id,
    string PublicNumber,
    string Status,
    string BuyerUserId,
    string StoreId,
    string SellerUserId,
    string CustomerFirstName,
    string CustomerLastName,
    string PhonePrimary,
    string PhoneSecondary,
    string DeliveryMode,
    string DeliveryAddress,
    double? DeliveryLatitude,
    double? DeliveryLongitude,
    string CurrencyCode,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total,
    string PaymentStatus,
    string? PaymentMethod,
    string ClientEvidenceDecision,
    IReadOnlyList<string> ClientEvidenceUrls,
    string? ClientEvidenceNote,
    DateTimeOffset? ClientEvidenceSubmittedAtUtc,
    DateTimeOffset? ClientEvidenceDecidedAtUtc,
    string? ClientEvidenceRejectReason,
    string? RouteSheetId,
    bool IsInvalidated,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<OrderLineDto> Lines);

/// <summary>Vista de rastreo para el comprador.</summary>
public sealed record OrderTrackingDto(
    string Id,
    string PublicNumber,
    string Status,
    string StoreId,
    string StoreName,
    string DeliveryMode,
    string DeliveryAddress,
    string CurrencyCode,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total,
    string PaymentStatus,
    string ClientEvidenceDecision,
    IReadOnlyList<string> ClientEvidenceUrls,
    string? ClientEvidenceNote,
    DateTimeOffset? ClientEvidenceSubmittedAtUtc,
    string? RouteSheetId,
    double? RouteDistanceKm,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<OrderLineDto> Lines);

public sealed record OrderSummaryDto(
    string Id,
    string PublicNumber,
    string Status,
    string StoreId,
    string CurrencyCode,
    decimal Total,
    string PaymentStatus,
    DateTimeOffset CreatedAtUtc);

public sealed record AdvanceOrderRequest(string ToStatus);

public sealed record UploadClientEvidenceRequest(IReadOnlyList<string> Urls, string? Note);

public sealed record DecideClientEvidenceRequest(bool Accept, string? RejectReason);

public sealed record InvalidateOrderRequest(string? Reason);
