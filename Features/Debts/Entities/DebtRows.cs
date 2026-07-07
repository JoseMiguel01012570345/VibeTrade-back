namespace VibeTrade.Backend.Features.Debts.Entities;

/// <summary>
/// Deuda de la plataforma con el almacén (tienda) por la mercancía de un pedido. Se genera al pasar el
/// pedido a «En tránsito» y se liquida <b>manualmente</b> en Finanzas (wiki cap. 11).
/// </summary>
public sealed class WarehouseDebtRow
{
    public string Id { get; set; } = "";

    /// <summary>Almacén = tienda vendedora.</summary>
    public string StoreId { get; set; } = "";

    public string OrderId { get; set; } = "";

    /// <summary>Número público del pedido (snapshot para listados).</summary>
    public string OrderPublicNumber { get; set; } = "";

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "";

    public bool Liquidated { get; set; }

    public DateTimeOffset? LiquidatedAtUtc { get; set; }

    /// <summary>Baja lógica: oculta la deuda sin borrar el registro.</summary>
    public bool Deleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Deuda de la plataforma con el afiliado por la comisión de un pedido referido. Se genera al pasar el
/// pedido a «En tránsito» y se liquida manualmente en Finanzas.
/// </summary>
public sealed class AffiliateDebtRow
{
    public string Id { get; set; } = "";

    public string? AffiliateId { get; set; }

    public string AffiliateCode { get; set; } = "";

    public string OrderId { get; set; } = "";

    public string OrderPublicNumber { get; set; } = "";

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "";

    public bool Liquidated { get; set; }

    public DateTimeOffset? LiquidatedAtUtc { get; set; }

    public bool Deleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Deuda de la plataforma con el transportista por un tramo entregado (km × tarifa/km). Se genera al
/// aceptarse la evidencia y se <b>auto-liquida</b> (wiki cap. 11).
/// </summary>
public sealed class CarrierDebtRow
{
    public string Id { get; set; } = "";

    public string CarrierUserId { get; set; } = "";

    public string OrderId { get; set; } = "";

    public string OrderPublicNumber { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    public string RouteStopId { get; set; } = "";

    public double TotalKm { get; set; }

    public decimal RatePerKm { get; set; }

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "";

    /// <summary>Deuda de transportista se crea ya liquidada (auto-liquidación).</summary>
    public bool Liquidated { get; set; } = true;

    public DateTimeOffset? LiquidatedAtUtc { get; set; }

    public bool Deleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
