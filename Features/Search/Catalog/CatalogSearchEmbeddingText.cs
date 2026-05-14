using System.Linq;
using System.Text;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Market.Dtos;

namespace VibeTrade.Backend.Features.Search.Catalog;

/// <summary>
/// Texto semántico para embedding TF‑IDF y campo <see cref="CatalogSearchDocument.SearchText"/>.
/// Excluye coords, URLs de medios, ids, fechas, monedas ISO y scores; incluye descripciones.
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
        AppendLine(sb, "Categories", CategoriesToPlain(s.Categories));
        AppendFoldedLine(sb, s.Name, s.NormalizedName, s.Pitch, CategoriesToPlain(s.Categories));
        if (s.TransportIncluded)
            sb.AppendLine("TransportIncluded: envío incluido");
        return Normalize(sb.ToString());
    }

    public static string ForProduct(StoreProductRow p, StoreRow store)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "StoreName", store.Name);
        AppendLine(sb, "StorePitch", store.Pitch);
        AppendLine(sb, "Categories", CategoriesToPlain(store.Categories));
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
        AppendLine(sb, "CustomFields", CustomFieldsToSearchText(p.CustomFields));
        AppendLine(sb, "OfferQa", OfferQaToSearchText(p.OfferQa));
        AppendFoldedLine(sb, store.Name, p.Name, p.Category, p.ShortDescription);
        return Normalize(sb.ToString());
    }

    public static string ForService(StoreServiceRow sv, StoreRow store)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "StoreName", store.Name);
        AppendLine(sb, "StorePitch", store.Pitch);
        AppendLine(sb, "Categories", CategoriesToPlain(store.Categories));
        AppendLine(sb, "Category", sv.Category);
        AppendLine(sb, "TipoServicio", sv.TipoServicio);
        AppendLine(sb, "Descripcion", sv.Descripcion);
        AppendLine(sb, "Incluye", sv.Incluye);
        AppendLine(sb, "NoIncluye", sv.NoIncluye);
        AppendLine(sb, "Entregables", sv.Entregables);
        AppendLine(sb, "PropIntelectual", sv.PropIntelectual);
        AppendLine(sb, "Riesgos", ServiceRiesgosToSearchText(sv.Riesgos));
        AppendLine(sb, "Dependencias", ServiceItemsBodyToSearchText(sv.Dependencias));
        AppendLine(sb, "Garantias", ServiceGarantiasToSearchText(sv.Garantias));
        AppendLine(sb, "CustomFields", CustomFieldsToSearchText(sv.CustomFields));
        AppendLine(sb, "OfferQa", OfferQaToSearchText(sv.OfferQa));
        AppendFoldedLine(sb, store.Name, sv.TipoServicio, sv.Category, sv.Descripcion);
        return Normalize(sb.ToString());
    }

    public static string ForEmergent(EmergentOfferRow e, StoreRow store, StoreProductRow? p, StoreServiceRow? s)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "StoreName", store.Name);
        AppendLine(sb, "StorePitch", store.Pitch);
        AppendLine(sb, "Categories", CategoriesToPlain(store.Categories));
        var snap = e.RouteSheetSnapshot ?? new EmergentRouteSheetSnapshot();
        AppendLine(sb, "EmergentTitulo", snap.Titulo);
        AppendLine(sb, "MercanciasResumen", snap.MercanciasResumen);
        AppendLine(sb, "MonedaPago", snap.MonedaPago);
        foreach (var leg in snap.Paradas ?? [])
        {
            AppendLine(sb, "Parada", $"{leg.Origen} → {leg.Destino}");
            AppendLine(sb, "ParadaPrecio", leg.PrecioTransportista);
            AppendLine(sb, "ParadaDetalle", $"{leg.Origen} {leg.Destino} {leg.PrecioTransportista} {leg.MonedaPago}");
        }

        if (p is not null)
        {
            AppendLine(sb, "BaseProductName", p.Name);
            AppendLine(sb, "BaseProductCategory", p.Category);
            AppendLine(sb, "BaseProductModel", p.Model);
            AppendLine(sb, "BaseProductShortDescription", p.ShortDescription);
        }

        if (s is not null)
        {
            AppendLine(sb, "BaseServiceCategory", s.Category);
            AppendLine(sb, "BaseServiceTipo", s.TipoServicio);
            AppendLine(sb, "BaseServiceDescripcion", s.Descripcion);
        }

        AppendLine(sb, "OfferQa", OfferQaToSearchText(e.OfferQa));
        AppendFoldedLine(
            sb,
            store.Name,
            snap.Titulo,
            snap.MercanciasResumen,
            p?.Name,
            p?.Category,
            s?.TipoServicio,
            s?.Category);
        sb.AppendLine("EmergentRoutePublication: hoja de ruta publicada");
        return Normalize(sb.ToString());
    }

    private static string ServiceRiesgosToSearchText(ServiceRiesgosBody? r) =>
        r is { Enabled: true, Items: { Count: > 0 } } ? string.Join(' ', r.Items) : "";

    private static string ServiceItemsBodyToSearchText(ServiceDependenciasBody? b) =>
        b is { Enabled: true, Items: { Count: > 0 } } ? string.Join(' ', b.Items) : "";

    private static string ServiceGarantiasToSearchText(ServiceGarantiasBody? g) =>
        g is { Enabled: true, Texto: { Length: > 0 } } ? (g.Texto ?? "").Trim() : "";

    private static string CustomFieldsToSearchText(IReadOnlyList<StoreCustomFieldBody>? fields)
    {
        if (fields is not { Count: > 0 })
            return "";
        return string.Join(' ', fields.Select(FlattenCustomField));
    }

    private static string FlattenCustomField(StoreCustomFieldBody f)
    {
        var parts = new List<string>();
        foreach (var s in new[] { f.Title, f.Body, f.AttachmentNote })
        {
            if (!string.IsNullOrWhiteSpace(s))
                parts.Add(s.Trim());
        }

        if (f.Attachments is { Count: > 0 } a)
        {
            foreach (var x in a)
            {
                var t = (x.FileName + " " + x.Url).Trim();
                if (t.Length > 0)
                    parts.Add(t);
            }
        }

        return string.Join(' ', parts);
    }

    private static string OfferQaToSearchText(IReadOnlyList<OfferQaComment>? items)
    {
        if (items is not { Count: > 0 })
            return "";
        return string.Join(
            ' ',
            items.Select(c =>
            {
                var parts = new List<string> { c.Text, c.Question, c.Answer }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!);
                return string.Join(' ', parts);
            }));
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

    private static string CategoriesToPlain(IReadOnlyList<string>? categories)
    {
        var parts = CatalogJsonColumnParsing.StringListOrEmpty(categories)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
        return parts.Count == 0 ? "" : string.Join(' ', parts);
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        return string.Join('\n', s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
