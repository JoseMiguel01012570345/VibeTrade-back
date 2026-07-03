namespace VibeTrade.Backend.Features.Statistics.Dtos;

public record StatisticsDateSeriesPoint(string Date, int Count);

public record StatisticsCurrencyAmount(string CurrencyCode, decimal Amount);

public record StatisticsOverviewDto(
    int TotalOrders,
    int DeliveredOrders,
    IReadOnlyList<StatisticsCurrencyAmount> RevenueByCurrency,
    double TotalTripKm,
    int TripCount,
    int UniqueVisitors,
    int PageViews,
    int ProductViews);

public record StatisticsDeliveredOrdersDto(
    int Total,
    IReadOnlyList<StatisticsDateSeriesPoint> Series);

public record StatisticsTripKmBucket(string Label, double MinKm, double MaxKm, int Count);

public record StatisticsTripKmDto(
    double TotalKm,
    int TripCount,
    double MinKm,
    double MedianKm,
    double P90Km,
    IReadOnlyList<StatisticsTripKmBucket> Histogram,
    IReadOnlyList<StatisticsDateSeriesPoint> TripsPerDay);

public record StatisticsOrderLocationPoint(
    string OrderId,
    string PublicNumber,
    string Status,
    double Latitude,
    double Longitude,
    DateTimeOffset CreatedAt);

public record StatisticsOrderLocationsDto(
    int Total,
    IReadOnlyList<StatisticsOrderLocationPoint> Points);

public record StatisticsTrafficPathRow(string Path, int Views);

public record StatisticsTrafficIpRow(string IpAddress, string DisplayLabel, int Sessions, int PageViews);

public record StatisticsTrafficDto(
    int UniqueVisitors,
    int PageViews,
    IReadOnlyList<StatisticsDateSeriesPoint> UniqueVisitorsPerDay,
    IReadOnlyList<StatisticsDateSeriesPoint> PageViewsPerDay,
    IReadOnlyList<StatisticsTrafficPathRow> TopPaths,
    IReadOnlyList<StatisticsTrafficIpRow> TopIps);

public record StatisticsProductViewRow(string ProductId, string ProductName, int Views);

public record StatisticsProductViewsDto(
    int TotalViews,
    IReadOnlyList<StatisticsDateSeriesPoint> ViewsPerDay,
    IReadOnlyList<StatisticsProductViewRow> TopProducts);

public record StatisticsFunnelRow(string Status, int Count);

public record StatisticsOrderFunnelDto(IReadOnlyList<StatisticsFunnelRow> Stages);

public record StatisticsPeakHourCell(int DayOfWeek, int Hour, int Count);

public record StatisticsOrdersByHourDto(IReadOnlyList<StatisticsPeakHourCell> Cells);

public record StatisticsCustomersDto(
    int NewCustomers,
    int ReturningCustomers,
    IReadOnlyList<StatisticsDateSeriesPoint> NewCustomersPerDay,
    IReadOnlyList<StatisticsDateSeriesPoint> ReturningCustomersPerDay);

public record StatisticsCancellationsDto(
    int InvalidatedCount,
    int DeletedCount,
    int TotalOrders,
    double InvalidationRatePercent,
    double DeletionRatePercent);

public record StatisticsLandingExitRow(string Path, int Count, bool IsLanding);

public record StatisticsLandingExitDto(
    IReadOnlyList<StatisticsLandingExitRow> LandingPages,
    IReadOnlyList<StatisticsLandingExitRow> ExitPages);

public record StatisticsRevenueAverageBucket(string Bucket, decimal Amount);

public record StatisticsRevenueAverageSeries(
    string Granularity,
    string CurrencyCode,
    IReadOnlyList<StatisticsRevenueAverageBucket> Series,
    decimal AverageAmount);

public record StatisticsRevenueAveragesDto(
    StatisticsRevenueAverageSeries Daily,
    StatisticsRevenueAverageSeries Monthly,
    StatisticsRevenueAverageSeries Hourly);
