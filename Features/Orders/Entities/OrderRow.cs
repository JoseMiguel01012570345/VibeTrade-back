namespace VibeTrade.Backend.Features.Orders.Entities;

/// <summary>
/// Pedido de mercancía (Pedido). Aggregate de compra: un pedido por tienda,
/// pago único retenido, evidencia tienda → cliente y enlace a la hoja de ruta.
/// </summary>
public sealed class OrderRow
{
    public string Id { get; set; } = "";

    /// <summary>Número público visible al comprador (formato <c>VT-XXXXXXXX</c>).</summary>
    public string PublicNumber { get; set; } = "";

    /// <summary>Comprador (usuario de plataforma).</summary>
    public string BuyerUserId { get; set; } = "";

    /// <summary>Tienda vendedora.</summary>
    public string StoreId { get; set; } = "";

    /// <summary>Dueño de la tienda al crear el pedido (snapshot para operaciones/chat).</summary>
    public string SellerUserId { get; set; } = "";

    /// <summary><see cref="OrderStatuses"/>.</summary>
    public string Status { get; set; } = OrderStatuses.Procesado;

    // Datos de contacto/entrega del comprador.
    public string CustomerFirstName { get; set; } = "";
    public string CustomerLastName { get; set; } = "";
    public string PhonePrimary { get; set; } = "";
    public string PhoneSecondary { get; set; } = "";

    /// <summary><see cref="OrderDeliveryModes"/>.</summary>
    public string DeliveryMode { get; set; } = OrderDeliveryModes.Shipping;

    public string DeliveryAddress { get; set; } = "";
    public double? DeliveryLatitude { get; set; }
    public double? DeliveryLongitude { get; set; }

    // Importes (moneda única del carrito).
    public string CurrencyCode { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Total { get; set; }

    /// <summary>Tarifa por km de la tienda vigente al crear el pedido (auditoría).</summary>
    public decimal PricePerKmSnapshot { get; set; }

    /// <summary>Distancia estimada de reparto en km (línea recta al crear; hoja de ruta la refina).</summary>
    public double? RouteDistanceKm { get; set; }

    // Pago retenido único.
    /// <summary><see cref="OrderPaymentStatuses"/>.</summary>
    public string PaymentStatus { get; set; } = OrderPaymentStatuses.Held;
    public string? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public DateTimeOffset? PaymentHeldAtUtc { get; set; }
    public DateTimeOffset? PaymentReleasedAtUtc { get; set; }
    public DateTimeOffset? PaymentRefundedAtUtc { get; set; }

    // Evidencia de entrega tienda → cliente.
    /// <summary><see cref="OrderClientEvidenceDecisions"/>.</summary>
    public string ClientEvidenceDecision { get; set; } = OrderClientEvidenceDecisions.None;
    public List<string> ClientEvidenceUrls { get; set; } = new();
    public string? ClientEvidenceNote { get; set; }
    public DateTimeOffset? ClientEvidenceSubmittedAtUtc { get; set; }
    public DateTimeOffset? ClientEvidenceDecidedAtUtc { get; set; }
    public string? ClientEvidenceRejectReason { get; set; }

    // Enlace a la hoja de ruta (se completa al pasar a «En tránsito»; ver Logistics/RouteSheets).
    public string? RouteSheetId { get; set; }

    // Afiliado (snapshot).
    public string? AffiliateCodeSnapshot { get; set; }
    public decimal? AffiliateCommissionAmount { get; set; }

    // Disparadores de deuda (ver Features/Debts).
    public bool WarehouseDebtsRecorded { get; set; }
    public bool AffiliateDebtRecorded { get; set; }
    public bool CarrierDebtRecorded { get; set; }

    // Invalidación / baja lógica.
    public bool IsInvalidated { get; set; }
    public DateTimeOffset? InvalidatedAtUtc { get; set; }
    public string? InvalidatedReason { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<OrderLineRow> Lines { get; set; } = new List<OrderLineRow>();
}
