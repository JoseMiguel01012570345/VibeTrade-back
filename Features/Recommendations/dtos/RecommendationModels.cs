namespace VibeTrade.Backend.Features.Recommendations.Dtos;

public sealed record OfferCandidate(
    string OfferId,
    string StoreId,
    string Category,
    string OwnerUserId,
    int TrustScore,
    DateTimeOffset UpdatedAt,
    int InquiryCount,
    double PopularityWeight);

public sealed record InteractionPoint(
    string UserId,
    string OfferId,
    string EventType,
    DateTimeOffset CreatedAt);

public sealed record ViewerContacts(
    List<string> ContactIds,
    HashSet<string> ContactSet,
    List<string> RelevantUserIds);
