namespace VibeTrade.Backend.Features.Inventory.Entities;

/// <summary>Proveedor (TCP) vinculado a una tienda con acceso al portal.</summary>
public sealed class StoreSupplierRow
{
    public string Id { get; set; } = "";

    public string StoreId { get; set; } = "";

    public StoreRow Store { get; set; } = null!;

    public string BusinessName { get; set; } = "";

    public string PortalUsername { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public bool Active { get; set; } = true;

    /// <summary>Fondos acumulados en la plataforma (deuda TCP simplificada).</summary>
    public decimal PlatformDebtAmount { get; set; }

    public string PlatformDebtCurrencyCode { get; set; } = "USD";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
