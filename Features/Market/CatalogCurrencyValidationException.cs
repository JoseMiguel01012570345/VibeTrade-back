namespace VibeTrade.Backend.Features.Market;

/// <summary>
/// El workspace envía un producto o servicio sin monedas obligatorias (precio / aceptadas).
/// </summary>
public sealed class CatalogCurrencyValidationException(string message) : Exception(message);
