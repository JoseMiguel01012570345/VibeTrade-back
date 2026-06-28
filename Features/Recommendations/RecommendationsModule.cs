using VibeTrade.Backend.Features.Recommendations.Feed;
using VibeTrade.Backend.Features.Recommendations.Guest;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Features.Search;
using VibeTrade.Backend.Features.Search.Interfaces;

namespace VibeTrade.Backend.Features.Recommendations;

public static class RecommendationsModule
{
    public static IServiceCollection AddRecommendationsFeature(this IServiceCollection services)
    {
        services.AddScoped<IOfferPopularityWeightService, RecommendationService.OfferPopularityWeightService>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddScoped<IRecommendationElasticsearchQuery, RecommendationElasticsearchQuery>();
        services.AddScoped<RecommendationFeedV2>();
        services.AddSingleton<IGuestInteractionStore, GuestInteractionStore>();
        services.AddScoped<IGuestRecommendationService, GuestRecommendationService>();
        return services;
    }

    public static WebApplication MapRecommendationsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/recommendations").WithTags("Recommendations");

        group.MapGet("/", GetRecommendationsAsync);
        group.MapPost("/interactions", PostInteractionAsync);
        group.MapGet("/guest", GetGuestRecommendationsAsync);
        group.MapPost("/guest/interactions", PostGuestInteraction);

        return app;
    }

    private static async Task<IResult> GetRecommendationsAsync(
        int? take,
        HttpRequest request,
        IRecommendationService recommendations,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var batch = await recommendations.GetBatchAsync(
            userId,
            take ?? RecommendationUtils.DefaultBatchSize,
            cancellationToken);
        return Results.Ok(batch);
    }

    private static async Task<IResult> PostInteractionAsync(
        TrackInteractionBody body,
        HttpRequest request,
        IRecommendationService recommendations,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(body.OfferId))
            return Results.BadRequest(new { error = "invalid_offer_id", message = "Indica la oferta." });
        if (!RecommendationUtils.TryParseInteractionEventType(body.EventType, out var eventType))
            return Results.BadRequest(new { error = "invalid_event_type", message = "Usa click, inquiry o chat_start." });

        await recommendations.RecordInteractionAsync(
            userId,
            body.OfferId.Trim(),
            eventType,
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetGuestRecommendationsAsync(
        string? guestId,
        int? take,
        IGuestRecommendationService guestRecommendations,
        CancellationToken cancellationToken)
    {
        var gid = (guestId ?? "").Trim();
        if (gid.Length < 8)
            return Results.BadRequest(new { error = "invalid_guest_id", message = "guestId requerido." });

        var batch = await guestRecommendations.GetBatchAsync(
            gid,
            take ?? RecommendationUtils.DefaultBatchSize,
            cancellationToken);
        return Results.Ok(batch);
    }

    private static IResult PostGuestInteraction(
        TrackGuestInteractionBody body,
        IGuestInteractionStore guestInteractions)
    {
        var gid = (body.GuestId ?? "").Trim();
        if (gid.Length < 8)
            return Results.BadRequest(new { error = "invalid_guest_id", message = "guestId requerido." });
        if (string.IsNullOrWhiteSpace(body.OfferId))
            return Results.BadRequest(new { error = "invalid_offer_id", message = "Indica la oferta." });
        if (!RecommendationUtils.TryParseInteractionEventType(body.EventType, out var eventType))
            return Results.BadRequest(new { error = "invalid_event_type", message = "Usa click, inquiry o chat_start." });

        guestInteractions.Record(gid, body.OfferId.Trim(), eventType);
        return Results.NoContent();
    }
}
