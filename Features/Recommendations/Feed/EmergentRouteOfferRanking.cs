using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Recommendations.Feed;

/// <summary>
/// Ofertas emergentes (hoja de ruta publicada en <c>emergent_offers</c>) para muestreos del feed.
/// El id devuelto es <see cref="Data.Entities.EmergentOfferRow.Id" /> (prefijo <c>emo_</c>), no el producto/servicio del hilo.
/// </summary>
public static class EmergentRouteOfferRanking
{
    public const string EmergentKindRouteSheet = "route_sheet";

    /// <summary>
    /// Hasta <paramref name="take"/> ids de publicaciones emergentes activas, no publicadas por el viewer,
    /// no presentes en <paramref name="exclude"/>.
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
            .Where(e => e.Kind == EmergentKindRouteSheet
                        && e.RetractedAtUtc == null
                        && e.PublisherUserId != viewer
                        && db.ChatRouteSheets.Any(r =>
                            r.ThreadId == e.ThreadId
                            && r.RouteSheetId == e.RouteSheetId
                            && r.DeletedAtUtc == null
                            && r.PublishedToPlatform))
            .OrderByDescending(e => e.PublishedAtUtc)
            .Select(e => e.Id)
            .Take(256)
            .ToListAsync(cancellationToken);
        
       if (emergentOrdered.Count == 0)
            return [];

        RecommendationUtils.Shuffle(emergentOrdered, Random.Shared);
        var chosen = new HashSet<string>(exclude, StringComparer.Ordinal);
        var result = new List<string>(Math.Min(take, emergentOrdered.Count));
        foreach (var id in emergentOrdered)
        {
            if (result.Count >= take)
                break;
            if (!chosen.Add(id))
                continue;
            result.Add(id);
        }
        return result;
    }
}
