using System.Linq;
using System.Text;
using System.Text.Json;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Search;

/// <summary>
/// Texto semántico para embedding TF‑IDF y campo <see cref="CatalogSearchDocument.SearchText"/>.
/// Excluye coords, URLs de medios, ids, fechas, monedas ISO y scores; incluye descripciones y JSON léxico útil.
/// </summary>
internal static class CatalogSearchEmbeddingText
{
    private const int MaxFieldChars = 6000;

    public static string ForStore(StoreRow s)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "Name", s.Name);
        AppendLine(sb, "NormalizedName", s.NormalizedName);
        AppendLine(sb, "Pitch", s.Pitch);
        AppendLine(sb, "Website", s.WebsiteUrl);
        AppendLine(sb, "Categories", CategoriesJsonToPlain(s.CategoriesJson));
        AppendFoldedLine(sb, s.Name, s.NormalizedName, s.Pitch, CategoriesJsonToPlain(s.CategoriesJson));
        if (s.TransportIncluded)
            sb.AppendLine("TransportIncluded: envío incluido");
        return Normalize(sb.ToString());
    }

    public static string ForProduct(StoreProductRow p, StoreRow store)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "StoreName", store.Name);
        AppendLine(sb, "StorePitch", store.Pitch);
        AppendLine(sb, "Categories", CategoriesJsonToPlain(store.CategoriesJson));
        AppendLine(sb, "Name", p.Name);
        AppendLine(sb, "Category", p.Category);
        AppendLine(sb, "Model", p.Model);
        AppendLine(sb, "ShortDescription", p.ShortDescription);
        AppendLine(sb, "MainBenefit", p.MainBenefit);
        AppendLine(sb, "TechnicalSpecs", p.TechnicalSpecs);
        AppendLine(sb, "Condition", p.Condition);
        AppendLine(sb, "Price", p.Price);
        AppendLine(sb, "TaxesShippingInstall", p.TaxesShippingInstall);
        AppendLine(sb, "Availability", p.Availability);
        AppendLine(sb, "WarrantyReturn", p.WarrantyReturn);
        AppendLine(sb, "ContentIncluded", p.ContentIncluded);
        AppendLine(sb, "UsageConditions", p.UsageConditions);
        AppendLine(sb, "CustomFields", p.CustomFieldsJson);
        AppendLine(sb, "OfferQa", OfferQaJson.ToJsonb(p.OfferQa));
        AppendFoldedLine(sb, store.Name, p.Name, p.Category, p.ShortDescription);
        return Normalize(sb.ToString());
    }

    public static string ForService(StoreServiceRow sv, StoreRow store)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "StoreName", store.Name);
        AppendLine(sb, "StorePitch", store.Pitch);
        AppendLine(sb, "Categories", CategoriesJsonToPlain(store.CategoriesJson));
        AppendLine(sb, "Category", sv.Category);
        AppendLine(sb, "TipoServicio", sv.TipoServicio);
        AppendLine(sb, "Descripcion", sv.Descripcion);
        AppendLine(sb, "Incluye", sv.Incluye);
        AppendLine(sb, "NoIncluye", sv.NoIncluye);
        AppendLine(sb, "Entregables", sv.Entregables);
        AppendLine(sb, "PropIntelectual", sv.PropIntelectual);
        AppendLine(sb, "Riesgos", sv.RiesgosJson);
        AppendLine(sb, "Dependencias", sv.DependenciasJson);
        AppendLine(sb, "Garantias", sv.GarantiasJson);
        AppendLine(sb, "CustomFields", sv.CustomFieldsJson);
        AppendLine(sb, "OfferQa", OfferQaJson.ToJsonb(sv.OfferQa));
        AppendFoldedLine(sb, store.Name, sv.TipoServicio, sv.Category, sv.Descripcion);
        return Normalize(sb.ToString());
    }

    private static void AppendFoldedLine(StringBuilder sb, params string?[] parts)
    {
        var folded = string.Join(' ', parts.Select(p => StoreSearchTextNormalize.FoldForMatch(p)).Where(x => x.Length > 0));
        if (folded.Length == 0)
            return;
        AppendLine(sb, "Folded", folded);
    }

    private static void AppendLine(StringBuilder sb, string label, string? value)
    {
        var trimmed = Trim(value);
        if (trimmed is null)
            return;
        var t = Trunc(trimmed);
        if (t.Length == 0)
            return;
        sb.Append(label);
        sb.Append(": ");
        sb.AppendLine(t);
    }

    private static string? Trim(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        return s.Trim();
    }

    private static string Trunc(string s)
    {
        if (s.Length <= MaxFieldChars)
            return s;
        return s[..MaxFieldChars] + "…";
    }

    private static string CategoriesJsonToPlain(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return "";
            var parts = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var t = el.GetString();
                    if (!string.IsNullOrWhiteSpace(t))
                        parts.Add(t.Trim());
                }
            }

            return parts.Count == 0 ? "" : string.Join(' ', parts);
        }
        catch
        {
            return "";
        }
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        return string.Join('\n', s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
