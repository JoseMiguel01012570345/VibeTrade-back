namespace VibeTrade.Backend.Features.Recommendations.Interfaces;

public interface IGuestRecommendationService
{
    Task<RecommendationBatchResponse> GetBatchAsync(
        string guestId,
        int take,
        CancellationToken cancellationToken = default);
}
