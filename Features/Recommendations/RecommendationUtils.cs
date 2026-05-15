using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Catalog.Dtos;
using VibeTrade.Backend.Features.Recommendations.Dtos;

namespace VibeTrade.Backend.Features.Recommendations;

/// <summary>Constantes y helpers puros del feed de recomendaciones (sin DI), incl. texto para scoring y <see cref="VibeTrade.Backend.Features.Recommendations.Feed.RecommendationFeedV2"/>.</summary>
public static class RecommendationUtils
{
    /// <summary>Valor por omisión de <c>take</c> en la API cuando el cliente no lo envía.</summary>
    public const int DefaultBatchSize = 20;

    /// <summary>Prefetch de recomendaciones en bootstrap; la API no limita el <c>take</c> a este valor.</summary>
    public const int DefaultBootstrapTake = 140;

    public const double ScoreThreshold = 0.35d;

    /// <summary>El tamaño de lote sigue al <c>take</c> del cliente (valores &lt; 1 se sustituyen por <see cref="DefaultBatchSize" />).</summary>
    public static int NormalizeClientTake(int take) =>
        take < 1 ? DefaultBatchSize : take;

    public static (List<InteractionPoint> UserEvents, List<InteractionPoint> ContactEvents) SplitEventsForViewer(
        string viewerId,
        HashSet<string> contactSet,
        IReadOnlyList<InteractionPoint> relevantEvents)
    {
        var userEvents = relevantEvents
            .Where(x => string.Equals(x.UserId, viewerId, StringComparison.Ordinal))
            .ToList();
        var contactEvents = relevantEvents
            .Where(x => contactSet.Contains(x.UserId))
            .ToList();
        return (userEvents, contactEvents);
    }

    public static string InteractionTypeToStorageValue(RecommendationInteractionType eventType) =>
        eventType switch
        {
            RecommendationInteractionType.Click => "click",
            RecommendationInteractionType.Inquiry => "inquiry",
            RecommendationInteractionType.ChatStart => "chat_start",
            _ => "click",
        };

    public static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public static Queue<string> ToQueueByScoreOrder(IReadOnlyList<(string OfferId, double Score)> hits) =>
        new(hits.Select(h => h.OfferId));

    /// <summary>Quita duplicados conservando el primer orden.</summary>
    public static Queue<string> ToDistinctQueue(IEnumerable<string> ids)
    {
        var se = new HashSet<string>(StringComparer.Ordinal);
        var q = new Queue<string>();
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !se.Add(id))
                continue;
            q.Enqueue(id);
        }

        return q;
    }

    public static (int Q1, int Q2, int Q3, int Q4) QuotasFiftyTwentyFifteenFifteen(int cap)
    {
        if (cap <= 0)
            return (0, 0, 0, 0);
        var q1 = (int)(cap * 0.5d);
        var q2 = (int)(cap * 0.2d);
        var q3 = (int)(cap * 0.15d);
        var q4 = cap - q1 - q2 - q3;
        return (q1, q2, q3, q4);
    }

    public static bool TryParseInteractionEventType(string? raw, out RecommendationInteractionType eventType)
    {
        switch ((raw ?? "").Trim().ToLowerInvariant())
        {
            case "click":
                eventType = RecommendationInteractionType.Click;
                return true;
            case "inquiry":
                eventType = RecommendationInteractionType.Inquiry;
                return true;
            case "chat_start":
                eventType = RecommendationInteractionType.ChatStart;
                return true;
            default:
                eventType = RecommendationInteractionType.Click;
                return false;
        }
    }

    public static string ConcatOfferMainTextProduct(StoreProductRow p)
    {
        var parts = new[]
        {
            p.Name, p.Category, p.Model, p.ShortDescription, p.MainBenefit, p.TechnicalSpecs,
            p.Condition, p.Price, p.Availability, p.WarrantyReturn, p.ContentIncluded,
            p.UsageConditions, CustomFieldsPlain(p.CustomFields),
        };
        return string.Join(' ', parts.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    public static string ConcatOfferMainTextService(StoreServiceRow s)
    {
        var parts = new[]
        {
            s.Category, s.TipoServicio, s.Descripcion, s.Incluye, s.NoIncluye, s.Entregables,
            s.PropIntelectual, ServiceRiesgosPlain(s.Riesgos), ServiceDependenciasPlain(s.Dependencias),
            ServiceGarantiasPlain(s.Garantias), CustomFieldsPlain(s.CustomFields),
        };
        return string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public static string CustomFieldsPlain(IReadOnlyList<StoreCustomFieldBody>? list)
    {
        if (list is not { Count: > 0 })
            return "";
        return string.Join(' ', list.Select(f => string.Join(' ', f.Title, f.Body, f.AttachmentNote).Trim())
            .Where(s => s.Length > 0));
    }

    public static string ServiceRiesgosPlain(ServiceRiesgosBody? r) =>
        r is { Enabled: true, Items: { Count: > 0 } } ? string.Join(' ', r.Items) : "";

    public static string ServiceDependenciasPlain(ServiceDependenciasBody? b) =>
        b is { Enabled: true, Items: { Count: > 0 } } ? string.Join(' ', b.Items) : "";

    public static string ServiceGarantiasPlain(ServiceGarantiasBody? g)
    {
        if (g is not { Enabled: true })
            return "";
        return (g.Texto ?? "").Trim();
    }

    public static IEnumerable<IEnumerable<T>> Chunk<T>(IEnumerable<T> source, int size)
    {
        using var e = source.GetEnumerator();
        while (e.MoveNext())
        {
            var chunk = new List<T>(size) { e.Current };
            for (var i = 1; i < size && e.MoveNext(); i++)
                chunk.Add(e.Current);
            yield return chunk;
        }
    }
}
