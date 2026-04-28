using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogServiceRowMapper
{
    public static void Apply(StoreServicePutRequest s, StoreServiceRow row, DateTimeOffset now)
    {
        row.Published = s.Published;
        row.Category = s.Category ?? "";
        row.TipoServicio = s.TipoServicio ?? "";
        row.Descripcion = s.Descripcion ?? "";
        if (s.Riesgos is not null)
            row.Riesgos = s.Riesgos;
        row.Incluye = s.Incluye ?? "";
        row.NoIncluye = s.NoIncluye ?? "";
        if (s.Dependencias is not null)
            row.Dependencias = s.Dependencias;
        row.Entregables = s.Entregables ?? "";
        if (s.Garantias is not null)
            row.Garantias = s.Garantias;
        row.PropIntelectual = s.PropIntelectual ?? "";
        row.Monedas = MarketCatalogCurrency.BuildMonedasList(s);
        row.CustomFields = s.CustomFields is not null
            ? s.CustomFields.ToList()
            : row.CustomFields;
        row.UpdatedAt = now;
    }
}
