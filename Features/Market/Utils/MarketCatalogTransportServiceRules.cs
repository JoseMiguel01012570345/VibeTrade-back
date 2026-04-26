using System.Text.RegularExpressions;

namespace VibeTrade.Backend.Features.Market.Utils;

/// <summary>
/// Alineado con la heurística de transporte en el cliente (<c>transportEligibility.ts</c>).
/// </summary>
internal static class MarketCatalogTransportServiceRules
{
    private static readonly Regex TransportTaxonomy = new(
        @"transportista|log[ií]stica|logistica|transporte|flete|fulfillment|cadena|env[ií]o|envio|última milla|ultima milla",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ServiceTransportHint = new(
        @"transporte|log[ií]stica|logistica|flete|transport|cadena|fulfillment|última milla|ultima milla|picking|env[ií]o|almacenaje",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool QualifiesAsTransport(string? category, string? tipoServicio)
    {
        var cat = (category ?? "").Trim();
        var tipo = (tipoServicio ?? "").Trim();
        if (cat.Length > 0 && TransportTaxonomy.IsMatch(cat)) return true;
        if (tipo.Length > 0 && ServiceTransportHint.IsMatch(tipo)) return true;
        if (cat.Length > 0 && ServiceTransportHint.IsMatch(cat)) return true;
        return false;
    }

    public static bool HasAtLeastOnePhoto(IReadOnlyList<string>? photoUrls) =>
        photoUrls is { Count: > 0 } && photoUrls.Any(s => !string.IsNullOrWhiteSpace(s));
}
