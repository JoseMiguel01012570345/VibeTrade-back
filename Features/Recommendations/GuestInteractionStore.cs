using Microsoft.Extensions.Caching.Memory;

namespace VibeTrade.Backend.Features.Recommendations;

public interface IGuestInteractionStore
{
    void Record(string guestId, string offerId, RecommendationInteractionType eventType);
    IReadOnlyList<(string OfferId, string EventType)> GetRecent(string guestId, int max = 250);
}

/// <summary>
/// Almacenamiento efímero para interacciones de invitados (sin cuenta).
/// Se guarda en memoria con expiración deslizante.
/// </summary>
public sealed class GuestInteractionStore(IMemoryCache cache) : IGuestInteractionStore
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromHours(8),
    };

    private sealed record GuestInteraction(string OfferId, string EventType, DateTimeOffset At);

    public void Record(string guestId, string offerId, RecommendationInteractionType eventType)
    {
        var gid = (guestId ?? "").Trim();
        var oid = (offerId ?? "").Trim();
        if (gid.Length == 0 || oid.Length == 0)
            return;

        var ev = eventType switch
        {
            RecommendationInteractionType.ChatStart => "chat_start", // might happend in the future
            RecommendationInteractionType.Inquiry => "inquiry", // might happend in the future
            _ => "click", // user can only do this right now
        };

        var key = BuildKey(gid);
        var list = cache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(CacheOptions);
            return new List<GuestInteraction>(capacity: 32);
        });
        if (list is null)
            return;

        lock (list)
        {
            list.Add(new GuestInteraction(oid, ev, DateTimeOffset.UtcNow));
            // evitar crecimiento sin control
            if (list.Count > 500)
                list.RemoveRange(0, Math.Min(20, list.Count - 500));
           
        }
    }

    public IReadOnlyList<(string OfferId, string EventType)> GetRecent(string guestId, int max = 250)
    {
        var gid = (guestId ?? "").Trim();
        if (gid.Length == 0)
            return Array.Empty<(string, string)>();

        var key = BuildKey(gid);
        if (!cache.TryGetValue(key, out List<GuestInteraction>? list) || list is null)
            return Array.Empty<(string, string)>();

        lock (list)
        {
            var take = Math.Clamp(max, 1, 500);
            return list
                .OrderByDescending(x => x.At)
                .Take(take)
                .Select(x => (x.OfferId, x.EventType))
                .ToArray();
        }
    }

    private static string BuildKey(string guestId) => $"guest-interactions:{guestId}";
}

