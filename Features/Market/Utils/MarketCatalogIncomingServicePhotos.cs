using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogIncomingServicePhotos
{
    /// <summary>Filtra <c>photoUrls</c> de servicios a solo imágenes (media API o URLs permitidas).</summary>
    public static async Task<string> FilterToStoredImageJsonAsync(
        AppDbContext db,
        JsonElement photoUrlsArray,
        CancellationToken cancellationToken)
    {
        if (photoUrlsArray.ValueKind != JsonValueKind.Array)
            return "[]";

        var raw = new List<string>();
        foreach (var el in photoUrlsArray.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
                continue;
            var u = (el.GetString() ?? "").Trim();
            if (u.Length > 0)
                raw.Add(u);
        }

        if (raw.Count == 0)
            return "[]";

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

        return JsonSerializer.Serialize(kept);
    }
}
