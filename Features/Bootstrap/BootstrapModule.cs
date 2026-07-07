using VibeTrade.Backend.Features.Bootstrap.Interfaces;

namespace VibeTrade.Backend.Features.Bootstrap;

public static class BootstrapModule
{
    public static IServiceCollection AddBootstrapFeature(this IServiceCollection services)
    {
        services.AddScoped<IBootstrapService, BootstrapService>();
        services.AddScoped<IGuestBootstrapService, GuestBootstrapService>();
        return services;
    }

    public static WebApplication MapBootstrapEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/bootstrap", GetBootstrapAsync).WithTags("Bootstrap");
        app.MapGet("/api/v1/bootstrap/guest", GetGuestBootstrapAsync).WithTags("Bootstrap");
        return app;
    }

    private static async Task<IResult> GetBootstrapAsync(
        HttpRequest request,
        IBootstrapService bootstrap,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        string? viewerPhoneDigits = null;
        if (currentUser.TryGetUser(request, out var user) && !string.IsNullOrEmpty(user?.Phone))
        {
            viewerPhoneDigits = new string(user.Phone.Where(char.IsDigit).ToArray());
        }
        if (string.IsNullOrWhiteSpace(viewerPhoneDigits))
            return Results.Unauthorized();

        var root = await bootstrap.GetBootstrapAsync(viewerPhoneDigits, cancellationToken);
        return Results.Ok(root);
    }

    private static async Task<IResult> GetGuestBootstrapAsync(
        string? guestId,
        IGuestBootstrapService bootstrap,
        CancellationToken cancellationToken)
    {
        var gid = (guestId ?? "").Trim();
        if (gid.Length < 8)
            return Results.BadRequest(new { error = "invalid_guest_id", message = "guestId requerido." });

        var root = await bootstrap.GetGuestBootstrapAsync(gid, cancellationToken);
        return Results.Ok(root);
    }
}
