using MediatR;
using VibeTrade.Backend.Features.SavedOffers.Dtos;
using VibeTrade.Backend.Features.SavedOffers.Interfaces;
using VibeTrade.Backend.Features.SavedOffers.RemoveSavedOffer;
using VibeTrade.Backend.Features.SavedOffers.SaveOffer;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Features.SavedOffers;

public static class SavedOffersModule
{
    public static IServiceCollection AddSavedOffersFeature(this IServiceCollection services) => services;

    public static WebApplication MapSavedOffersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/me/saved-offers")
            .WithTags("Saved offers");

        group.MapPost("/", SaveOfferAsync)
            .Produces<SavedOfferIdsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{productId}", RemoveSavedOfferAsync)
            .Produces<SavedOfferIdsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> SaveOfferAsync(
        SaveOfferBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        if (body is null || string.IsNullOrWhiteSpace(body.ProductId))
            return Results.BadRequest(new { error = "invalid_body", message = "Indica productId." });

        var result = await mediator.Send(new SaveOfferCommand(userId, body.ProductId), cancellationToken);
        return result.Error switch
        {
            SavedOfferMutationError.UserNotFound => Results.NotFound(new
            {
                error = "user_not_found",
                message = "No se encontró la cuenta de usuario.",
            }),
            SavedOfferMutationError.NotFound => Results.NotFound(new
            {
                error = "not_found",
                message = "No existe una oferta con ese id.",
            }),
            SavedOfferMutationError.OwnProduct => Results.BadRequest(new
            {
                error = "own_product",
                message = "No puedes guardar productos o servicios de tus propias tiendas.",
            }),
            _ => Results.Ok(new SavedOfferIdsResponse(result.SavedOfferIds)),
        };
    }

    private static async Task<IResult> RemoveSavedOfferAsync(
        string productId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var ids = await mediator.Send(new RemoveSavedOfferCommand(userId, productId), cancellationToken);
        return Results.Ok(new SavedOfferIdsResponse(ids ?? Array.Empty<string>()));
    }
}
