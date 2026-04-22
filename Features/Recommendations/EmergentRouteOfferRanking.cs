using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Recommendations;

/// <summary>
/// Prioriza ofertas de catálogo asociadas a hojas de ruta publicadas (<c>emergent_offers</c>)
/// para usuarios con perfil transportista / categoría <c>carrier</c>.
/// </summary>
public static class EmergentRouteOfferRanking
{
    public const string EmergentKindRouteSheet = "route_sheet";

    public static async Task<string[]> MergeForCarrierViewersAsync(
        AppDbContext db,
        string viewerUserId,
        IReadOnlyList<string> primaryOrderedIds,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (!await CarrierServiceProfile.UserRunsCarrierOrTransportServicesAsync(db, viewerUserId, cancellationToken))
            return primaryOrderedIds.ToArray();

        var emergentIds = await db.EmergentOffers.AsNoTracking()
            .Where(e => e.Kind == EmergentKindRouteSheet && e.RetractedAtUtc == null)
            .OrderByDescending(e => e.PublishedAtUtc)
            .Select(e => e.OfferId)
            .Take(48)
            .ToListAsync(cancellationToken);

        if (emergentIds.Count == 0)
            return primaryOrderedIds.ToArray();

        var eligible = await FilterEligibleOfferIdsForViewerAsync(db, emergentIds, viewerUserId, cancellationToken);
        var maxPrepend = Math.Min(10, Math.Max(2, batchSize / 2));
        var prepend = new List<string>();
        foreach (var oid in emergentIds)
        {
            if (!eligible.Contains(oid))
                continue;
            if (prepend.Count >= maxPrepend)
                break;
            if (prepend.Contains(oid, StringComparer.Ordinal))
                continue;
            prepend.Add(oid);
        }

        if (prepend.Count == 0)
            return primaryOrderedIds.ToArray();

        var merged = new List<string>(batchSize);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string id)
        {
            if (merged.Count >= batchSize)
                return;
            if (!seen.Add(id))
                return;
            merged.Add(id);
        }

        foreach (var id in prepend)
            Add(id);
        foreach (var id in primaryOrderedIds)
            Add(id);

        return merged.ToArray();
    }

    private static async Task<HashSet<string>> FilterEligibleOfferIdsForViewerAsync(
        AppDbContext db,
        IReadOnlyList<string> offerIds,
        string viewerUserId,
        CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (offerIds.Count == 0)
            return set;

        var distinct = offerIds.Distinct(StringComparer.Ordinal).ToList();
        const int chunkSize = 500;
        for (var i = 0; i < distinct.Count; i += chunkSize)
        {
            var c = distinct.Skip(i).Take(chunkSize).ToList();
            var pIds = await db.StoreProducts.AsNoTracking()
                .Where(p => c.Contains(p.Id) && p.Published && p.Store.OwnerUserId != viewerUserId)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in pIds)
                set.Add(id);

            var sIds = await db.StoreServices.AsNoTracking()
                .Where(s => c.Contains(s.Id) && (s.Published == null || s.Published == true) && s.Store.OwnerUserId != viewerUserId)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in sIds)
                set.Add(id);
        }

        return set;
    }
}

internal static class CarrierServiceProfile
{
    private static readonly Regex TransportHints = new(
        @"transport|flete|log[ií]stic|cadena|fulfillment|última\s*milla|picking|env[ií]o|almacen",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task<bool> UserRunsCarrierOrTransportServicesAsync(
        AppDbContext db,
        string userId,
        CancellationToken cancellationToken)
    {
        var uid = (userId ?? "").Trim();
        if (uid.Length == 0)
            return false;

        var categoriesJsonList = await db.Stores.AsNoTracking()
            .Where(s => s.OwnerUserId == uid)
            .Select(s => s.CategoriesJson)
            .ToListAsync(cancellationToken);
        foreach (var cj in categoriesJsonList)
        {
            if (CategoriesJsonContainsCarrierOrTransport(cj))
                return true;
        }

        // Regex y helpers locales no son traducibles a SQL: filtrar en BD por tienda/publicación y evaluar texto en memoria.
        var serviceHints = await db.StoreServices.AsNoTracking()
            .Where(s => s.Store.OwnerUserId == uid && (s.Published == null || s.Published == true))
            .Select(s => new { s.Category, s.TipoServicio })
            .ToListAsync(cancellationToken);

        return serviceHints.Exists(s =>
            IsExplicitCarrierCategory(s.Category)
            || TransportHints.IsMatch($"{s.Category} {s.TipoServicio}"));
    }

    private static bool IsExplicitCarrierCategory(string category)
    {
        var t = (category ?? "").Trim();
        return string.Equals(t, "Transportista", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "carrier", StringComparison.OrdinalIgnoreCase); // legado
    }

    private static bool CategoriesJsonContainsCarrierOrTransport(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        return TransportHints.IsMatch(json)
            || json.Contains("Transportista", StringComparison.OrdinalIgnoreCase)
            || json.Contains("carrier", StringComparison.OrdinalIgnoreCase); // legado
    }
}
