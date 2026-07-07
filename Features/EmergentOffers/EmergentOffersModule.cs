using VibeTrade.Backend.Features.EmergentOffers.Dtos;
using VibeTrade.Backend.Features.EmergentOffers.Interfaces;

namespace VibeTrade.Backend.Features.EmergentOffers;

public static class EmergentOffersModule
{
    public static IServiceCollection AddEmergentOffersFeature(this IServiceCollection services)
    {
        services.AddScoped<IEmergentOfferCarrierSubscriptionService, EmergentOfferCarrierSubscriptionService>();
        services.AddScoped<IEmergentRouteTramoSubscriptionRequestService, EmergentRouteTramoSubscriptionRequestService>();
        return services;
    }

    public static WebApplication MapEmergentOffersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/emergent-offers").WithTags("Emergent offers");

        group.MapGet("/{emergentOfferId}/carrier-subscription", GetCarrierSubscriptionAsync);
        group.MapGet("/{emergentOfferId}/my-route-tramo-subscriptions", GetMyRouteTramoSubscriptionsAsync);
        group.MapPost("/{emergentOfferId}/tramo-subscription-requests", PostTramoSubscriptionRequestAsync);

        return app;
    }

    private static async Task<IResult> GetCarrierSubscriptionAsync(
        string emergentOfferId,
        HttpRequest request,
        IEmergentOfferCarrierSubscriptionService carrierSubscription,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var viewerUserId = currentUser.GetUserId(request);
        var status = await carrierSubscription.GetStatusAsync(viewerUserId, emergentOfferId, cancellationToken);
        return Results.Ok(new CarrierSubscriptionResponse(
            status.CanSubscribe,
            status.ReasonCode,
            status.Message));
    }

    private static async Task<IResult> GetMyRouteTramoSubscriptionsAsync(
        string emergentOfferId,
        HttpRequest request,
        IRouteTramoSubscriptionService routeTramoSubscriptions,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await routeTramoSubscriptions.ListForCarrierByEmergentPublicationAsync(
            userId,
            emergentOfferId,
            cancellationToken);
        if (list is null)
            return Results.NotFound();
        return Results.Ok(list);
    }

    private static async Task<IResult> PostTramoSubscriptionRequestAsync(
        string emergentOfferId,
        TramoSubscriptionRequestBody? body,
        HttpRequest request,
        IEmergentRouteTramoSubscriptionRequestService tramoSubscriptionRequest,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (string.IsNullOrWhiteSpace(userId))
            return Results.Unauthorized();

        if (body is null || string.IsNullOrWhiteSpace(body.StopId) || string.IsNullOrWhiteSpace(body.StoreServiceId))
            return Results.BadRequest(new { error = "invalid_payload", message = "Indica stopId y storeServiceId." });

        var (ok, code, message) = await tramoSubscriptionRequest.RequestAsync(
            userId,
            emergentOfferId,
            body.StopId.Trim(),
            body.StoreServiceId.Trim(),
            cancellationToken);

        if (!ok)
            return Results.BadRequest(new { error = code, message });

        return Results.NoContent();
    }
}
