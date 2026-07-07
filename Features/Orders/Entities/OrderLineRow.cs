namespace VibeTrade.Backend.Features.Orders.Entities;

using VibeTrade.Backend.Features.Orders;

/// <summary>Línea de pedido: snapshot de producto o servicio comprado.</summary>
public sealed class OrderLineRow
{
    public string Id { get; set; } = "";

    public string OrderId { get; set; } = "";

    public OrderRow Order { get; set; } = null!;

    /// <summary><see cref="OrderLineKinds"/>.</summary>
    public string LineKind { get; set; } = OrderLineKinds.Product;

    /// <summary>Referencia al catálogo de producto; null en líneas de servicio o si el producto se elimina.</summary>
    public string? ProductId { get; set; }

    /// <summary>Referencia al catálogo de servicio; null en líneas de producto.</summary>
    public string? ServiceId { get; set; }

    public string StoreId { get; set; } = "";

    /// <summary>Nombre mostrado (producto o servicio).</summary>
    public string ProductName { get; set; } = "";

    /// <summary>Ficha técnica al momento de la compra (solo productos).</summary>
    public string TechnicalSpecs { get; set; } = "";

    /// <summary>Tipo de servicio al momento de la compra (solo servicios).</summary>
    public string? ServiceTipo { get; set; }

    /// <summary>Mes de recurrencia del contrato de catálogo (solo servicios).</summary>
    public int? RecurrenceMonth { get; set; }

    /// <summary>Día de recurrencia del contrato de catálogo (solo servicios).</summary>
    public int? RecurrenceDay { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public string CurrencyCode { get; set; } = "";
}
