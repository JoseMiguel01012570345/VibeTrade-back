using VibeTrade.Backend.Features.Users.Dtos;
using VibeTrade.Backend.Features.Users.Interfaces;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Features.Users;

public static class UsersModule
{
    public static IServiceCollection AddUsersFeature(this IServiceCollection services)
    {
        services.AddScoped<IUsersAdminService, UsersAdminService>();
        return services;
    }

    public static WebApplication MapUsersEndpoints(this WebApplication app)
    {
        const string tag = "Admin / Users";
        var group = app.MapGroup("/api/v1/admin/users").WithTags(tag);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id}", UpdateAsync);
        group.MapPut("/{id}/roles", SetRolesAsync);
        group.MapPut("/{id}/password", SetPasswordAsync);
        group.MapDelete("/{id}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        IUsersAdminService users,
        ICurrentUserAccessor currentUser,
        IUserRoleService roles,
        string? q,
        CancellationToken cancellationToken)
    {
        var gate = await currentUser.RequireRolesAsync(request, roles, cancellationToken, RoleNames.SuperAdmin);
        if (!gate.Ok)
            return gate.Error!;

        var list = await users.ListAsync(q, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request,
        CreateUserRequest body,
        IUsersAdminService users,
        ICurrentUserAccessor currentUser,
        IUserRoleService roles,
        CancellationToken cancellationToken)
    {
        var gate = await currentUser.RequireRolesAsync(request, roles, cancellationToken, RoleNames.SuperAdmin);
        if (!gate.Ok)
            return gate.Error!;

        var (value, error) = await users.CreateAsync(body, cancellationToken);
        return error is not null
            ? Results.BadRequest(new { error = error.Code, message = error.Message })
            : Results.Ok(value);
    }

    private static async Task<IResult> UpdateAsync(
        string id,
        HttpRequest request,
        UpdateUserRequest body,
        IUsersAdminService users,
        ICurrentUserAccessor currentUser,
        IUserRoleService roles,
        CancellationToken cancellationToken)
    {
        var gate = await currentUser.RequireRolesAsync(request, roles, cancellationToken, RoleNames.SuperAdmin);
        if (!gate.Ok)
            return gate.Error!;

        var (value, error) = await users.UpdateAsync(id, body, cancellationToken);
        return ResultFromMutation(value, error);
    }

    private static async Task<IResult> SetRolesAsync(
        string id,
        HttpRequest request,
        SetUserRolesRequest body,
        IUsersAdminService users,
        ICurrentUserAccessor currentUser,
        IUserRoleService roles,
        CancellationToken cancellationToken)
    {
        var gate = await currentUser.RequireRolesAsync(request, roles, cancellationToken, RoleNames.SuperAdmin);
        if (!gate.Ok)
            return gate.Error!;

        var (value, error) = await users.SetRolesAsync(id, body, cancellationToken);
        return ResultFromMutation(value, error);
    }

    private static async Task<IResult> SetPasswordAsync(
        string id,
        HttpRequest request,
        SetUserPasswordRequest body,
        IUsersAdminService users,
        ICurrentUserAccessor currentUser,
        IUserRoleService roles,
        CancellationToken cancellationToken)
    {
        var gate = await currentUser.RequireRolesAsync(request, roles, cancellationToken, RoleNames.SuperAdmin);
        if (!gate.Ok)
            return gate.Error!;

        var error = await users.SetPasswordAsync(id, body, cancellationToken);
        if (error is null)
            return Results.NoContent();
        return error.Code == "not_found"
            ? Results.NotFound()
            : Results.BadRequest(new { error = error.Code, message = error.Message });
    }

    private static async Task<IResult> DeleteAsync(
        string id,
        HttpRequest request,
        IUsersAdminService users,
        ICurrentUserAccessor currentUser,
        IUserRoleService roles,
        CancellationToken cancellationToken)
    {
        var gate = await currentUser.RequireRolesAsync(request, roles, cancellationToken, RoleNames.SuperAdmin);
        if (!gate.Ok)
            return gate.Error!;

        var error = await users.DeleteAsync(id, gate.UserId!, cancellationToken);
        if (error is null)
            return Results.NoContent();
        return error.Code == "not_found"
            ? Results.NotFound()
            : Results.BadRequest(new { error = error.Code, message = error.Message });
    }

    private static IResult ResultFromMutation(AdminUserDto? value, UsersOpError? error)
    {
        if (error is null)
            return Results.Ok(value);
        return error.Code == "not_found"
            ? Results.NotFound()
            : Results.BadRequest(new { error = error.Code, message = error.Message });
    }
}
