using VibeTrade.Backend.Features.Analytics.Dtos;

namespace VibeTrade.Backend.Features.Analytics.Interfaces;

public interface IAnalyticsTrackingService
{
    Task RecordPageViewAsync(PageViewRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken);

    Task RecordProductViewAsync(ProductViewRequest request, string? ipAddress, CancellationToken cancellationToken);
}
