namespace VibeTrade.Backend.Features.Recommendations.Dtos;

public enum SeedKind
{
    User,
    Contact,
    Random,
    Emergent,
}

public sealed record SeedOffer(string OfferId, SeedKind Kind, double Weight);
