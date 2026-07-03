using VibeTrade.Backend.Features.Statistics.Dtos;

namespace VibeTrade.Backend.Features.Statistics.Interfaces;

public interface IStatisticsService
{
    Task<StatisticsOverviewDto> GetOverviewAsync(StatisticsQuery query, CancellationToken ct);

    Task<StatisticsDeliveredOrdersDto> GetDeliveredOrdersAsync(StatisticsQuery query, CancellationToken ct);

    Task<StatisticsTripKmDto> GetTripKmAsync(StatisticsQuery query, CancellationToken ct);

    Task<StatisticsOrderLocationsDto> GetOrderLocationsAsync(
        StatisticsQuery query, string? status, int page, int pageSize, CancellationToken ct);

    Task<StatisticsTrafficDto> GetTrafficAsync(StatisticsQuery query, CancellationToken ct);

    Task<StatisticsProductViewsDto> GetProductViewsAsync(StatisticsQuery query, int limit, CancellationToken ct);

    Task<StatisticsOrderFunnelDto> GetOrderFunnelAsync(StatisticsQuery query, CancellationToken ct);

    Task<StatisticsOrdersByHourDto> GetOrdersByHourAsync(StatisticsQuery query, CancellationToken ct);

    Task<StatisticsCustomersDto> GetCustomersAsync(StatisticsQuery query, CancellationToken ct);

    Task<StatisticsCancellationsDto> GetCancellationsAsync(StatisticsQuery query, CancellationToken ct);

    Task<StatisticsLandingExitDto> GetLandingExitAsync(StatisticsQuery query, int limit, CancellationToken ct);

    Task<StatisticsRevenueAveragesDto> GetRevenueAveragesAsync(StatisticsQuery query, CancellationToken ct);
}
