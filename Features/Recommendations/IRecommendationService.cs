namespace VibeTrade.Backend.Features.Recommendations;

public interface IRecommendationService
{
    Task<RecommendationBatchResponse> GetBatchAsync(
        string viewerUserId,
        int take,
        CancellationToken cancellationToken = default);

    Task RecordInteractionAsync(
        string userId,
        string offerId,
        RecommendationInteractionType eventType,
        CancellationToken cancellationToken = default);
}
