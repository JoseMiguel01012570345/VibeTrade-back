using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Recommendations;

/// <summary>
/// Ofertas emergentes (p. ej. hoja de ruta en <c>emergent_offers</c>) para muestreos aleatorios acotados del feed.
/// </summary>
public static class EmergentRouteOfferRanking
{
    public const string EmergentKindRouteSheet = "route_sheet";

    /// <summary>
    /// Hasta <paramref name="take"/> ids de ofertas emergentes activas, elegibles para el viewer y no presentes en <paramref name="exclude"/>.
    /// Usado en muestreos aleatorios del feed (<see cref="RecommendationFeedV2.SampleRandomPublishedOfferIdsAsync"/>).
    /// </summary>
    public static async Task<List<string>> TakeRandomEmergentOfferIdsAsync(
        AppDbContext db,
        string viewerUserId,
        int take,
        IReadOnlySet<string> exclude,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
            return [];

        var viewer = (viewerUserId ?? "").Trim();
        var emergentOrdered = await db.EmergentOffers.AsNoTracking()
            .Where(e => e.Kind == EmergentKindRouteSheet && e.RetractedAtUtc == null)
            .OrderByDescending(e => e.PublishedAtUtc)
            .Select(e => e.OfferId)
            .Take(256)
            .ToListAsync(cancellationToken);

        if (emergentOrdered.Count == 0)
            return [];

        var eligible = await FilterEligibleOfferIdsForViewerAsync(db, emergentOrdered, viewer, cancellationToken);
        var pool = emergentOrdered
            .Where(id => eligible.Contains(id) && !exclude.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (pool.Count == 0)
            return [];

        Shuffle(pool, Random.Shared);
        return pool.Take(Math.Min(take, pool.Count)).ToList();
    }

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
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
