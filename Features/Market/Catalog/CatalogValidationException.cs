namespace VibeTrade.Backend.Features.Market.Catalog;

/// <summary>Validación de negocio al persistir ítems de catálogo (mensaje listo para el cliente).</summary>
public sealed class CatalogValidationException(string message) : Exception(message);
