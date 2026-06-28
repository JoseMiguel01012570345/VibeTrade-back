namespace VibeTrade.Backend.Features.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsFeature(this IServiceCollection services)
    {
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRouteTramoSubscriptionNotificationService, RouteTramoSubscriptionNotificationService>();
        services.AddScoped<IRouteSheetThreadNotificationService, RouteSheetThreadNotificationService>();
        services.AddScoped<IBroadcastingService, BroadcastingService>();
        services.AddScoped<ISignalRBroadcastService>(sp => sp.GetRequiredService<IBroadcastingService>());
        return services;
    }

    public static WebApplication MapChatNotificationsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/me").WithTags("Notifications");

        group.MapGet("/notifications", GetNotificationsAsync);
        group.MapPost("/notifications/mark-read", MarkReadAsync);

        return app;
    }

    private static async Task<IResult> GetNotificationsAsync(
        string? from,
        string? to,
        HttpRequest request,
        INotificationService notifications,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!DateTimeOffset.TryParse(from!.Trim(), out var f))
                return Results.BadRequest(new { error = "invalid_from", message = "Parámetro 'from' no es una fecha ISO válida." });
            fromUtc = f;
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!DateTimeOffset.TryParse(to!.Trim(), out var t))
                return Results.BadRequest(new { error = "invalid_to", message = "Parámetro 'to' no es una fecha ISO válida." });
            toUtc = t;
        }
        if (fromUtc != null && toUtc != null && fromUtc > toUtc)
            return Results.BadRequest(new { error = "invalid_range", message = "La fecha de inicio debe ser anterior o igual al fin." });
        var list = await notifications.ListNotificationsAsync(userId, fromUtc, toUtc, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> MarkReadAsync(
        MarkReadBody? body,
        HttpRequest request,
        INotificationService notifications,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        await notifications.MarkNotificationsReadAsync(userId, body?.Ids, cancellationToken);
        return Results.NoContent();
    }
}
