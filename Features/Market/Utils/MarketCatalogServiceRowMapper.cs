using System.Text.Json;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogServiceRowMapper
{
    public static void Apply(JsonElement item, StoreServiceRow row, DateTimeOffset now)
    {
        row.Published = item.TryGetProperty("published", out var p)
            ? p.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
        row.Category = MarketCatalogJsonHelpers.GetString(item, "category") ?? "";
        row.TipoServicio = MarketCatalogJsonHelpers.GetString(item, "tipoServicio") ?? "";
        row.Descripcion = MarketCatalogJsonHelpers.GetString(item, "descripcion") ?? "";
        row.RiesgosJson = MarketCatalogJsonHelpers.SerializeJsonElement(item, "riesgos") ?? row.RiesgosJson;
        row.Incluye = MarketCatalogJsonHelpers.GetString(item, "incluye") ?? "";
        row.NoIncluye = MarketCatalogJsonHelpers.GetString(item, "noIncluye") ?? "";
        row.DependenciasJson = MarketCatalogJsonHelpers.SerializeJsonElement(item, "dependencias") ?? row.DependenciasJson;
        row.Entregables = MarketCatalogJsonHelpers.GetString(item, "entregables") ?? "";
        row.GarantiasJson = MarketCatalogJsonHelpers.SerializeJsonElement(item, "garantias") ?? row.GarantiasJson;
        row.PropIntelectual = MarketCatalogJsonHelpers.GetString(item, "propIntelectual") ?? "";
        row.MonedasJson = MarketCatalogCurrency.SerializeMonedasFromCatalogItemJson(item);
        row.CustomFieldsJson = MarketCatalogJsonHelpers.SerializeJsonElement(item, "customFields") ?? "[]";
        row.UpdatedAt = now;
    }
}
