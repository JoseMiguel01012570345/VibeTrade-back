namespace VibeTrade.Backend.Features.Recommendations.Dtos;

public sealed record TrackInteractionBody(string? OfferId, string? EventType);

public sealed record TrackGuestInteractionBody(string? GuestId, string? OfferId, string? EventType);
