namespace VibeTrade.Backend.Features.Statistics.Dtos;

/// <summary>
/// Filtro común de estadísticas. <see cref="StoreScope"/> null = todas las tiendas (superadmin);
/// no-null = solo pedidos/productos de esas tiendas (dueño/admin).
/// </summary>
public record StatisticsQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    bool DeliveredOnly = true,
    bool IncludeInvalidated = false,
    bool IncludeDeleted = false,
    IReadOnlyCollection<string>? StoreScope = null)
{
    /// <summary>true cuando el usuario ve todo el sitio (métricas globales de tráfico permitidas).</summary>
    public bool IsGlobalScope => StoreScope is null;
}

public static class StatisticsQueryParser
{
    public static bool TryParse(
        DateTimeOffset? from,
        DateTimeOffset? to,
        out StatisticsQuery query,
        out string? error)
    {
        query = default!;
        error = null;
        if (!from.HasValue || !to.HasValue)
        {
            error = "Los parámetros from y to son obligatorios.";
            return false;
        }

        if (to.Value < from.Value)
        {
            error = "to debe ser posterior o igual a from.";
            return false;
        }

        query = new StatisticsQuery(from.Value, to.Value);
        return true;
    }

    public static StatisticsQuery WithFlags(
        StatisticsQuery baseQuery,
        bool deliveredOnly,
        bool includeInvalidated,
        bool includeDeleted) =>
        baseQuery with
        {
            DeliveredOnly = deliveredOnly,
            IncludeInvalidated = includeInvalidated,
            IncludeDeleted = includeDeleted,
        };

    public static StatisticsQuery WithScope(
        StatisticsQuery baseQuery,
        IReadOnlyCollection<string>? storeScope) =>
        baseQuery with { StoreScope = storeScope };
}
