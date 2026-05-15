using System.Text.RegularExpressions;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Dtos;

namespace VibeTrade.Backend.Features.Offers;

/// <summary>Utilidades de ids emergentes, formato de precio/texto y fusión de hoja de ruta viva.</summary>
public static class OfferUtils
{
    public const string EmergentPublicationIdPrefix = "emo_";

    public static bool IsEmergentPublicationId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.StartsWith(EmergentPublicationIdPrefix, StringComparison.Ordinal);

    /// <summary>Clave estable para emparejar filas de hoja de ruta (chat) con una publicación emergente.</summary>
    public static string EmergentOfferRouteSheetKey(string threadId, string routeSheetId) =>
        string.Concat((threadId ?? "").Trim(), "\u001f", (routeSheetId ?? "").Trim());

    internal static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N")[..16];

    internal static string OfferDescriptionForProduct(StoreProductRow p)
    {
        var a = (p.ShortDescription ?? "").Trim();
        if (a.Length > 0)
            return a;
        var b = (p.MainBenefit ?? "").Trim();
        return b.Length > 0 ? b : "";
    }

    internal static string FormatProductPrice(StoreProductRow p)
    {
        var price = (p.Price ?? "").Trim();
        var mon = (p.MonedaPrecio ?? "").Trim();
        return $"{price} {mon}";
    }

    internal static string? FormatServicePriceLine(StoreServiceRow s)
    {
        try
        {
            var codes = CatalogJsonColumnParsing.StringListOrEmpty(s.Monedas)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return codes.Count == 0 ? null : string.Join(" · ", codes);
        }
        catch
        {
            return "Consultar";
        }
    }

    internal static string RouteSummaryLine(EmergentRouteSheetSnapshot snap)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(snap.MercanciasResumen))
            parts.Add(snap.MercanciasResumen.Trim());
        foreach (var leg in snap.Paradas ?? [])
        {
            if (!string.IsNullOrWhiteSpace(leg.Origen) && !string.IsNullOrWhiteSpace(leg.Destino))
                parts.Add($"{leg.Origen} → {leg.Destino}");
        }
        return string.Join(" · ", parts);
    }

    public static void ApplyLiveParadaStopIds(HomeOfferViewDto offer, RouteSheetPayload payload)
    {
        if (offer.EmergentRouteParadas is not { Count: > 0 } arr)
            return;

        var live = (payload.Paradas ?? [])
            .OrderBy(p => p.Orden)
            .ToList();
        if (live.Count == 0)
            return;

        foreach (var legNode in arr)
        {
            RouteStopPayload? match = null;
            if (legNode.Orden > 0)
                match = live.FirstOrDefault(p => p.Orden == legNode.Orden);

            var sid = (match?.Id ?? "").Trim();
            if (sid.Length > 0)
                legNode.StopId = sid;
            if (match?.OsrmRoadKm is double kmLive && kmLive >= 0 && !double.IsNaN(kmLive) && !double.IsInfinity(kmLive))
                legNode.OsrmRoadKm = kmLive;
            if (match?.OsrmRouteLatLngs is { Count: >= 2 })
                legNode.OsrmRouteLatLngs = match.OsrmRouteLatLngs;
        }
    }
}

/// <summary>
/// Misma heurística que el cliente (<c>transportEligibility.ts</c>) para servicios de transporte / logística.
/// </summary>
public static partial class TransportServiceQualification
{
    private static readonly Regex ServiceTransportHint = ServiceTransportHintRegex();
    private static readonly Regex TransportTaxonomy = TransportTaxonomyRegex();

    [GeneratedRegex(@"transporte|logística|logistica|flete|transport|cadena|fulfillment|última milla|picking|envío|almacenaje|envio|ultima milla", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ServiceTransportHintRegex();

    [GeneratedRegex(@"transportista|log[ií]stica|logistica|transporte|flete|fulfillment|cadena|envío|envio|última milla|ultima milla", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TransportTaxonomyRegex();

    public static bool ServiceQualifiesAsTransport(StoreServiceRow s)
    {
        if (s.Published == false)
            return false;
        var tipo = (s.TipoServicio ?? "").Trim();
        var cat = (s.Category ?? "").Trim();
        if (cat.Length > 0 && TransportTaxonomy.IsMatch(cat))
            return true;
        if (tipo.Length > 0 && ServiceTransportHint.IsMatch(tipo))
            return true;
        if (cat.Length > 0 && ServiceTransportHint.IsMatch(cat))
            return true;
        return false;
    }
}
