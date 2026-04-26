using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogIncomingServicePhotos
{
    /// <summary>Filtra <c>photoUrls</c> de servicios a solo imágenes (media API o URLs permitidas).</summary>
    public static async Task<List<string>> FilterToStoredImageListAsync(
        AppDbContext db,
        IReadOnlyList<string>? photoUrls,
        CancellationToken cancellationToken)
    {
        if (photoUrls is not { Count: > 0 })
            return new List<string>();

        var raw = new List<string>(photoUrls.Count);
        foreach (var u0 in photoUrls)
        {
            var u = (u0 ?? "").Trim();
            if (u.Length > 0) raw.Add(u);
        }

        if (raw.Count == 0)
            return new List<string>();

        return await FilterRawUrlListToStoredImageListAsync(db, raw, cancellationToken);
    }

    private static async Task<List<string>> FilterRawUrlListToStoredImageListAsync(
        AppDbContext db,
        List<string> raw,
        CancellationToken cancellationToken)
    {
        var mediaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in raw)
        {
            var id = MarketCatalogPhotoRules.TryGetStoredMediaIdFromCatalogUrl(u);
            if (id is not null)
                mediaIds.Add(id);
        }

        var mimeById = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mediaIds.Count > 0)
        {
            var rows = await db.StoredMedia.AsNoTracking()
                .Where(m => mediaIds.Contains(m.Id))
                .Select(m => new { m.Id, m.MimeType })
                .ToListAsync(cancellationToken);
            foreach (var r in rows)
                mimeById[r.Id] = r.MimeType ?? "";
        }

        var kept = new List<string>(raw.Count);
        foreach (var u in raw)
        {
            var id = MarketCatalogPhotoRules.TryGetStoredMediaIdFromCatalogUrl(u);
            if (id is not null)
            {
                if (mimeById.TryGetValue(id, out var mt) &&
                    mt.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    kept.Add(u);
                continue;
            }

            if (MarketCatalogPhotoRules.IsLikelyNonMediaCatalogImageUrl(u))
                kept.Add(u);
        }

        return kept;
    }
}
