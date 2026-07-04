using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Inventory.Dtos;
using VibeTrade.Backend.Features.Inventory.Interfaces;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Features.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryFeature(this IServiceCollection services)
    {
        services.AddScoped<IStoreCategoryService, StoreCategoryService>();
        services.AddScoped<IStoreInventoryAdminService, StoreInventoryAdminService>();
        services.AddScoped<ISupplierPortalService, SupplierPortalService>();
        return services;
    }

    public static WebApplication MapInventoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/market").WithTags("Inventory");

        group.MapGet("/stores/{storeId}/categories", ListStoreCategoriesAsync).AllowAnonymous();
        group.MapPost("/stores/{storeId}/categories", CreateStoreCategoryAsync);
        group.MapPatch("/stores/{storeId}/categories/{categoryId}", PatchStoreCategoryAsync);
        group.MapDelete("/stores/{storeId}/categories/{categoryId}", DeleteStoreCategoryAsync);

        group.MapPost("/stores/{storeId}/products/{productId}/approve", ApproveStoreProductAsync);
        group.MapPost("/stores/{storeId}/products/{productId}/remove-from-catalog", RemoveStoreProductFromCatalogAsync);

        group.MapGet("/stores/{storeId}/suppliers", ListStoreSuppliersAsync);
        group.MapPost("/stores/{storeId}/suppliers", CreateStoreSupplierAsync);

        group.MapGet("/stores/{storeId}/banners", ListStoreBannersAdminAsync);
        group.MapGet("/stores/{storeId}/catalog/banners", ListStoreBannersPublicAsync).AllowAnonymous();
        group.MapPost("/stores/{storeId}/banners", CreateStoreBannerAsync);
        group.MapPatch("/stores/{storeId}/banners/{bannerId}", PatchStoreBannerAsync);
        group.MapDelete("/stores/{storeId}/banners/{bannerId}", DeleteStoreBannerAsync);

        var portal = app.MapGroup("/api/v1/supplier-portal").WithTags("SupplierPortal");
        portal.MapPost("/login", SupplierPortalLoginAsync);
        portal.MapGet("/me", SupplierPortalMeAsync);
        portal.MapGet("/dashboard", SupplierPortalDashboardAsync);
        portal.MapPost("/inventory/bulk-update", SupplierPortalBulkUpdateAsync);

        return app;
    }

    private static async Task<IResult> ListStoreCategoriesAsync(
        string storeId,
        IStoreCategoryService categories,
        CancellationToken ct)
    {
        var list = await categories.ListAsync(storeId, ct);
        return list is null ? Results.NotFound() : Results.Ok(list);
    }

    private static async Task<IResult> CreateStoreCategoryAsync(
        string storeId,
        StoreCategoryCreateBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreCategoryService categories,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var dto = await categories.CreateAsync(storeId, userId, body, ct);
        return dto is null ? Results.BadRequest() : Results.Ok(dto);
    }

    private static async Task<IResult> PatchStoreCategoryAsync(
        string storeId,
        string categoryId,
        StoreCategoryPatchBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreCategoryService categories,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var ok = await categories.PatchAsync(storeId, categoryId, userId, body, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DeleteStoreCategoryAsync(
        string storeId,
        string categoryId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreCategoryService categories,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var ok = await categories.DeleteAsync(storeId, categoryId, userId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ApproveStoreProductAsync(
        string storeId,
        string productId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreInventoryAdminService inventory,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var ok = await inventory.ApproveProductAsync(storeId, productId, userId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> RemoveStoreProductFromCatalogAsync(
        string storeId,
        string productId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreInventoryAdminService inventory,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var ok = await inventory.RemoveProductFromCatalogAsync(storeId, productId, userId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ListStoreSuppliersAsync(
        string storeId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreInventoryAdminService inventory,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var list = await inventory.ListSuppliersAsync(storeId, userId, ct);
        return list is null ? Results.NotFound() : Results.Ok(list);
    }

    private static async Task<IResult> CreateStoreSupplierAsync(
        string storeId,
        StoreSupplierCreateBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreInventoryAdminService inventory,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var dto = await inventory.CreateSupplierAsync(storeId, userId, body, ct);
        return dto is null ? Results.BadRequest() : Results.Ok(dto);
    }

    private static async Task<IResult> ListStoreBannersAdminAsync(
        string storeId,
        string? type,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreInventoryAdminService inventory,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var list = await inventory.ListBannersAsync(storeId, type, activeOnly: false, ct);
        return Results.Ok(list ?? Array.Empty<StoreBannerDto>());
    }

    private static async Task<IResult> ListStoreBannersPublicAsync(
        string storeId,
        string? type,
        IStoreInventoryAdminService inventory,
        CancellationToken ct)
    {
        var kind = string.IsNullOrWhiteSpace(type) ? "main" : type;
        var list = await inventory.ListPublicBannersAsync(storeId, kind, ct);
        return list is null ? Results.NotFound() : Results.Ok(list);
    }

    private static async Task<IResult> CreateStoreBannerAsync(
        string storeId,
        StoreBannerCreateBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreInventoryAdminService inventory,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var dto = await inventory.CreateBannerAsync(storeId, userId, body, ct);
        return dto is null ? Results.BadRequest() : Results.Ok(dto);
    }

    private static async Task<IResult> PatchStoreBannerAsync(
        string storeId,
        string bannerId,
        StoreBannerPatchBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreInventoryAdminService inventory,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var ok = await inventory.PatchBannerAsync(storeId, bannerId, userId, body, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DeleteStoreBannerAsync(
        string storeId,
        string bannerId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreInventoryAdminService inventory,
        CancellationToken ct)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var ok = await inventory.DeleteBannerAsync(storeId, bannerId, userId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> SupplierPortalLoginAsync(
        [FromBody] SupplierLoginBody body,
        ISupplierPortalService portal,
        CancellationToken ct)
    {
        var supplier = await portal.AuthenticateAsync(body.Username ?? "", body.Password ?? "", ct);
        if (supplier is null)
            return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
        return Results.Ok(new { supplierId = supplier.Id, token = supplier.Id });
    }

    private static async Task<IResult> SupplierPortalMeAsync(
        [FromHeader(Name = "X-Supplier-Id")] string? supplierId,
        ISupplierPortalService portal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(supplierId)) return Results.Unauthorized();
        var me = await portal.GetMeAsync(supplierId, ct);
        return me is null ? Results.NotFound() : Results.Ok(me);
    }

    private static async Task<IResult> SupplierPortalDashboardAsync(
        [FromHeader(Name = "X-Supplier-Id")] string? supplierId,
        int? transactionsPage,
        int? transactionsPageSize,
        ISupplierPortalService portal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(supplierId)) return Results.Unauthorized();
        var dash = await portal.GetDashboardAsync(
            supplierId,
            transactionsPage ?? 1,
            transactionsPageSize ?? 15,
            ct);
        return dash is null ? Results.NotFound() : Results.Ok(dash);
    }

    private static async Task<IResult> SupplierPortalBulkUpdateAsync(
        [FromHeader(Name = "X-Supplier-Id")] string? supplierId,
        SupplierPortalInventoryBulkUpdateRequest body,
        ISupplierPortalService portal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(supplierId)) return Results.Unauthorized();
        var result = await portal.BulkUpdateInventoryAsync(supplierId, body, ct);
        return Results.Ok(result);
    }
}
