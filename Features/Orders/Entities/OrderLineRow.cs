namespace VibeTrade.Backend.Features.Orders.Entities;

/// <summary>Línea de pedido: snapshot del producto comprado (nombre, ficha técnica y precio).</summary>
public sealed class OrderLineRow
{
    public string Id { get; set; } = "";

    public string OrderId { get; set; } = "";

    public OrderRow Order { get; set; } = null!;

    /// <summary>Referencia al catálogo; null si el producto se elimina tras liquidar (la línea conserva el snapshot).</summary>
    public string? ProductId { get; set; }

    public string StoreId { get; set; } = "";

    public string ProductName { get; set; } = "";

    /// <summary>Ficha técnica al momento de la compra (snapshot).</summary>
    public string TechnicalSpecs { get; set; } = "";

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public string CurrencyCode { get; set; } = "";
}
