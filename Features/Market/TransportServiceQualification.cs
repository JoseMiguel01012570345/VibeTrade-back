using System.Text.RegularExpressions;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market;

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
