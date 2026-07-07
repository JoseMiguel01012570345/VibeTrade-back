using VibeTrade.Backend.Features.Debts.Dtos;
using VibeTrade.Backend.Features.Debts.Interfaces;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Features.Debts;

public static class DebtsModule
{
    public static IServiceCollection AddDebtsFeature(this IServiceCollection services)
    {
        services.AddScoped<IDebtsService, DebtsService>();
        return services;
    }

    public static WebApplication MapDebtsEndpoints(this WebApplication app)
    {
        const string tag = "Finance / Debts";

        app.MapGet("/api/v1/admin/debts", GetOverviewAsync).WithTags(tag);
        app.MapPost("/api/v1/admin/debts/liquidate", LiquidateAsync).WithTags(tag);

        return app;
    }

    private static async Task<IResult> GetOverviewAsync(
        HttpRequest request,
        IDebtsService debts,
        ICurrentUserAccessor currentUser,
        IUserRoleService roles,
        bool? includeLiquidated,
        bool? includeDeleted,
        CancellationToken cancellationToken)
    {
        var gate = await currentUser.RequireRolesAsync(
            request, roles, cancellationToken, RoleNames.SuperAdmin, RoleNames.Admin);
        if (!gate.Ok)
            return gate.Error!;

        var overview = await debts.GetOverviewAsync(
                includeLiquidated ?? false,
                includeDeleted ?? false,
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(overview);
    }

    private static async Task<IResult> LiquidateAsync(
        HttpRequest request,
        LiquidateDebtsRequest body,
        IDebtsService debts,
        ICurrentUserAccessor currentUser,
        IUserRoleService roles,
        CancellationToken cancellationToken)
    {
        var gate = await currentUser.RequireRolesAsync(
            request, roles, cancellationToken, RoleNames.SuperAdmin, RoleNames.Admin);
        if (!gate.Ok)
            return gate.Error!;

        var (value, error) = await debts.LiquidateAsync(body, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return Results.BadRequest(new { error = error.Code, message = error.Message });
        return Results.Ok(value);
    }
}
