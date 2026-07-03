using VibeTrade.Backend.Features.Orders.Dtos;
using VibeTrade.Backend.Features.Orders.Interfaces;
using VibeTrade.Backend.Infrastructure;
using VibeTrade.Backend.Infrastructure.Interfaces;

namespace VibeTrade.Backend.Features.Orders;

public static class OrdersModule
{
    public static IServiceCollection AddOrdersFeature(this IServiceCollection services)
    {
        services.AddScoped<IOrderService, OrderService>();
        return services;
    }

    public static WebApplication MapOrdersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Orders");

        // Comprador
        group.MapPost("/orders/preview", PreviewAsync);
        group.MapPost("/orders", CreateAsync);
        group.MapGet("/orders/mine", ListMineAsync);
        group.MapGet("/orders/track/{publicNumber}", TrackAsync);
        group.MapPost("/orders/{orderId}/evidence-decision", DecideEvidenceAsync);

        // Tienda / vendedor
        group.MapGet("/stores/{storeId}/orders", ListForStoreAsync);
        group.MapGet("/orders/{orderId}", GetForSellerAsync);
        group.MapPost("/orders/{orderId}/advance", AdvanceAsync);
        group.MapPost("/orders/{orderId}/client-evidence", UploadEvidenceAsync);
        group.MapPost("/orders/{orderId}/invalidate", InvalidateAsync);

        return app;
    }

    private static IResult ToError(OrderError error) => error.Code switch
    {
        "unauthorized" => Results.Unauthorized(),
        "forbidden" => Results.Json(new { error = error.Code, message = error.Message }, statusCode: StatusCodes.Status403Forbidden),
        "order_not_found" or "store_not_found" or "product_unavailable" =>
            Results.NotFound(new { error = error.Code, message = error.Message }),
        _ => Results.BadRequest(new { error = error.Code, message = error.Message }),
    };

    private static async Task<IResult> PreviewAsync(
        CreateOrderRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (value, error) = await orders.PreviewAsync(userId, body, cancellationToken);
        return error is not null ? ToError(error) : Results.Ok(value);
    }

    private static async Task<IResult> CreateAsync(
        CreateOrderRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (value, error) = await orders.CreateAsync(userId, body, cancellationToken);
        return error is not null ? ToError(error) : Results.Ok(value);
    }

    private static async Task<IResult> ListMineAsync(
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await orders.ListMyOrdersAsync(userId, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> TrackAsync(
        string publicNumber,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (value, error) = await orders.GetTrackingByPublicNumberAsync(userId, publicNumber, cancellationToken);
        return error is not null ? ToError(error) : Results.Ok(value);
    }

    private static async Task<IResult> DecideEvidenceAsync(
        string orderId,
        DecideClientEvidenceRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (value, error) = await orders.DecideClientEvidenceAsync(
            userId, orderId, body.Accept, body.RejectReason, cancellationToken);
        return error is not null ? ToError(error) : Results.Ok(value);
    }

    private static async Task<IResult> ListForStoreAsync(
        string storeId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await orders.ListForStoreAsync(userId, storeId, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetForSellerAsync(
        string orderId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (value, error) = await orders.GetForSellerAsync(userId, orderId, cancellationToken);
        return error is not null ? ToError(error) : Results.Ok(value);
    }

    private static async Task<IResult> AdvanceAsync(
        string orderId,
        AdvanceOrderRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (value, error) = await orders.AdvanceStatusAsync(userId, orderId, body.ToStatus, cancellationToken);
        return error is not null ? ToError(error) : Results.Ok(value);
    }

    private static async Task<IResult> UploadEvidenceAsync(
        string orderId,
        UploadClientEvidenceRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (value, error) = await orders.UploadClientEvidenceAsync(
            userId, orderId, body.Urls, body.Note, cancellationToken);
        return error is not null ? ToError(error) : Results.Ok(value);
    }

    private static async Task<IResult> InvalidateAsync(
        string orderId,
        InvalidateOrderRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOrderService orders,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (value, error) = await orders.InvalidateAsync(userId, orderId, body.Reason, cancellationToken);
        return error is not null ? ToError(error) : Results.Ok(value);
    }
}
