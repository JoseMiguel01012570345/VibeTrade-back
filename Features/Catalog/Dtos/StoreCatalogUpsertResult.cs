namespace VibeTrade.Backend.Features.Catalog.Dtos;

/// <summary>Resultado de operaciones granulares sobre productos/servicios de catálogo.</summary>
public enum StoreCatalogUpsertResult
{
    Ok,
    Unauthorized,
    StoreNotFound,
    Forbidden,
    IdMismatch,
    EntityNotFound,
}
