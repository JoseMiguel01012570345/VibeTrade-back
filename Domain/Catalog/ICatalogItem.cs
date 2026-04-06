namespace VibeTrade.Backend.Domain.Catalog;

/// <summary>Extensión futura: ítems de catálogo (producto vs servicio) bajo misma taxonomía.</summary>
public interface ICatalogItem
{
    string Id { get; }
    string StoreId { get; }
    string Category { get; }
}
