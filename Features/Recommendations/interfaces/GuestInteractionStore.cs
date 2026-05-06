namespace VibeTrade.Backend.Features.Recommendations.Interfaces;

public interface IGuestInteractionStore
{
    void Record(string guestId, string offerId, RecommendationInteractionType eventType);
    IReadOnlyList<(string OfferId, string EventType, DateTimeOffset At)> GetRecent(string guestId, int max = 250);
}

