using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Recommendations.Feed;

namespace VibeTrade.Backend.Features.Market;

public static partial class MarketModule
{
    private const long LargeCatalogBodyBytes = 524_288_000L;

    public static WebApplication MapMarketEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/market").WithTags("Market");

        group.MapGet("/catalog-categories", GetCatalogCategories);
        group.MapGet("/currencies", GetCurrencies);
        group.MapGet("/users/{userId}/published-transport-services", GetPublishedTransportServicesForUserAsync);
        group.MapGet("/stores/search", SearchStoresAsync).AllowAnonymous();
        group.MapGet("/stores/autocomplete", AutocompleteStoresAsync).AllowAnonymous();
        group.MapGet("/workspace", GetWorkspaceAsync);
        group.MapGet("/workspace/stores", GetWorkspaceStoresAsync);
        group.MapPut("/workspace/stores", PutWorkspaceStoresAsync)
            .WithMetadata(new RequestSizeLimitAttribute(LargeCatalogBodyBytes));
        group.MapPut("/workspace/catalogs", PutWorkspaceCatalogsAsync)
            .WithMetadata(new RequestSizeLimitAttribute(LargeCatalogBodyBytes));
        group.MapDelete("/stores/{storeId}", DeleteStoreAsync);
        group.MapPut("/stores/{storeId}/products/{productId}", PutStoreProductAsync)
            .WithMetadata(new RequestSizeLimitAttribute(LargeCatalogBodyBytes));
        group.MapDelete("/stores/{storeId}/products/{productId}", DeleteStoreProductAsync);
        group.MapPut("/stores/{storeId}/services/{serviceId}", PutStoreServiceAsync)
            .WithMetadata(new RequestSizeLimitAttribute(LargeCatalogBodyBytes));
        group.MapDelete("/stores/{storeId}/services/{serviceId}", DeleteStoreServiceAsync);
        group.MapGet("/offers/{offerId}/card", GetOfferCardAsync).AllowAnonymous();
        group.MapPost("/offers/{offerId}/like", PostOfferLikeAsync).AllowAnonymous();
        group.MapGet("/stores/{storeId}/catalog/search", SearchStoreCatalogAsync).AllowAnonymous();
        group.MapGet("/stores/{storeId}/comments", GetStoreCommentsAsync).AllowAnonymous();
        group.MapPost("/stores/{storeId}/comments", PostStoreCommentAsync);
        group.MapPost("/stores/{storeId}/comments/{commentId}/like", PostStoreCommentLikeAsync).AllowAnonymous();
        group.MapPost("/stores/{storeId}/detail", PostStoreDetailAsync);
        group.MapPost("/stores/by-name/{name}/detail", PostStoreDetailByNameAsync);

        return app;
    }

    private static IResult GetCatalogCategories() =>
        Results.Ok(new CatalogCategoriesResponse(CatalogCategories.ProductAndService));

    private static IResult GetCurrencies() =>
        Results.Ok(new CurrenciesResponse(CatalogCurrencies.All));

    private static async Task<IResult> GetPublishedTransportServicesForUserAsync(
        string userId,
        HttpRequest request,
        AppDbContext appDb,
        ICurrentUserAccessor currentUser,
        IOfferService offerService,
        CancellationToken cancellationToken)
    {
        var bearer = currentUser.GetUserId(request);
        if (bearer is null)
            return Results.Json(new { error = "unauthorized", message = "Sesión requerida." }, statusCode: StatusCodes.Status401Unauthorized);
        var uid = (userId ?? "").Trim();
        if (uid.Length < 2)
            return Results.BadRequest(new { error = "invalid_user", message = "Usuario inválido." });

        var storeIds = await appDb.Stores.AsNoTracking()
            .Where(s => s.OwnerUserId == uid)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        if (storeIds.Count == 0)
            return Results.Ok(new { services = Array.Empty<object>() });

        var services = await appDb.StoreServices.AsNoTracking()
            .Include(s => s.Store)
            .Where(s => storeIds.Contains(s.StoreId)
                && s.DeletedAtUtc == null
                && s.Published == true)
            .OrderBy(s => s.NombreServicio)
            .ThenBy(s => s.Category)
            .ToListAsync(cancellationToken);

        var list = services.Select(s =>
        {
            var v = offerService.ServiceCatalogRowFromEntity(s);
            return new
            {
                v.Id,
                v.StoreId,
                storeName = s.Store?.Name ?? "",
                v.Category,
                v.NombreServicio,
                v.Descripcion,
                v.Incluye,
                v.NoIncluye,
                v.Entregables,
                v.PropIntelectual,
                v.Published,
                v.FixedPrice,
                v.CurrencyCode,
                v.CustomFields,
                v.PhotoUrls,
                v.Riesgos,
                v.Dependencias,
                v.Garantias,
                v.PublicCommentCount,
                v.OfferLikeCount,
                v.ViewerLikedOffer,
            };
        }).ToList();

        return Results.Ok(new { services = list });
    }

    private static async Task<IResult> SearchStoresAsync(
        string? name,
        string? category,
        string? kinds,
        int? trustMin,
        double? lat,
        double? lng,
        double? km,
        int? limit,
        int? offset,
        IMarketCatalogStoreSearchService catalogStoreSearch,
        CancellationToken cancellationToken)
    {
        var response = await catalogStoreSearch.SearchCatalogAsync(
            name,
            category,
            kinds,
            trustMin,
            lat,
            lng,
            km,
            limit,
            offset,
            cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> AutocompleteStoresAsync(
        string? q,
        string? category,
        string? kinds,
        int? limit,
        IMarketCatalogStoreSearchService catalogStoreSearch,
        CancellationToken cancellationToken)
    {
        var response = await catalogStoreSearch.AutocompleteCatalogAsync(
            q,
            category,
            kinds,
            limit,
            cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetWorkspaceAsync(
        IMarketWorkspaceService marketWorkspace,
        CancellationToken cancellationToken)
    {
        var root = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        return Results.Ok(root);
    }

    private static async Task<IResult> GetWorkspaceStoresAsync(
        IMarketWorkspaceService marketWorkspace,
        CancellationToken cancellationToken)
    {
        var snapshot = await marketWorkspace.GetStoresSnapshotAsync(cancellationToken);
        return Results.Ok(snapshot);
    }

    private static async Task<IResult> PutWorkspaceStoresAsync(
        WorkspaceStorePutRequest body,
        IMarketWorkspaceService marketWorkspace,
        CancellationToken cancellationToken)
    {
        try
        {
            await marketWorkspace.SaveStoreProfilesAsync(body, cancellationToken);
        }
        catch (ArgumentException ex) when (string.Equals(ex.ParamName, CatalogArgumentParams.Currency, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "invalid_stores_body", message = "Indica la tienda con un campo \"id\" o usa la forma \"stores\".{...}." });
        }
        catch (InvalidOperationException ex) when (ex.Message == DuplicateStoreNameConflict.Message)
        {
            return Results.Conflict(new { error = "duplicate_store_name", message = DuplicateStoreNameConflict.Message });
        }

        return Results.Ok();
    }

    private static async Task<IResult> PutWorkspaceCatalogsAsync(
        WorkspaceStoreCatalogsPutRequest body,
        IMarketWorkspaceService marketWorkspace,
        CancellationToken cancellationToken)
    {
        try
        {
            await marketWorkspace.SaveStoreCatalogsAsync(body, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message == DuplicateStoreNameConflict.Message)
        {
            return Results.Conflict(new { error = "duplicate_store_name", message = DuplicateStoreNameConflict.Message });
        }
        catch (ArgumentException ex) when (string.Equals(ex.ParamName, CatalogArgumentParams.Currency, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }

        return Results.Ok();
    }

    private static async Task<IResult> DeleteStoreAsync(
        string storeId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMarketCatalogSyncService catalog,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Json(new { error = "unauthorized", message = "Sesión requerida." }, statusCode: StatusCodes.Status401Unauthorized);
        var r = await catalog.DeleteStoreAsync(storeId, userId, cancellationToken);
        return r switch
        {
            StoreCatalogUpsertResult.Ok => Results.NoContent(),
            StoreCatalogUpsertResult.Unauthorized => Results.Json(new { error = "unauthorized", message = "Sesión requerida." }, statusCode: StatusCodes.Status401Unauthorized),
            StoreCatalogUpsertResult.StoreNotFound => Results.NotFound(),
            StoreCatalogUpsertResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            StoreCatalogUpsertResult.IdMismatch => Results.BadRequest(new { error = "id_mismatch", message = "El id del cuerpo no coincide con la ruta." }),
            StoreCatalogUpsertResult.EntityNotFound => Results.NotFound(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> PutStoreProductAsync(
        string storeId,
        string productId,
        StoreProductPutRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMarketCatalogSyncService catalog,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Json(new { error = "unauthorized", message = "Sesión requerida." }, statusCode: StatusCodes.Status401Unauthorized);
        try
        {
            var r = await catalog.UpsertStoreProductAsync(storeId, productId, userId, body, cancellationToken);
            return MapCatalogUpsert(r);
        }
        catch (ArgumentException ex) when (string.Equals(ex.ParamName, CatalogArgumentParams.Currency, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }
    }

    private static async Task<IResult> DeleteStoreProductAsync(
        string storeId,
        string productId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMarketCatalogSyncService catalog,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Json(new { error = "unauthorized", message = "Sesión requerida." }, statusCode: StatusCodes.Status401Unauthorized);
        var r = await catalog.DeleteStoreProductAsync(storeId, productId, userId, cancellationToken);
        return MapCatalogUpsert(r);
    }

    private static async Task<IResult> PutStoreServiceAsync(
        string storeId,
        string serviceId,
        StoreServicePutRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMarketCatalogSyncService catalog,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Json(new { error = "unauthorized", message = "Sesión requerida." }, statusCode: StatusCodes.Status401Unauthorized);
        try
        {
            var r = await catalog.UpsertStoreServiceAsync(storeId, serviceId, userId, body, cancellationToken);
            return MapCatalogUpsert(r);
        }
        catch (ArgumentException ex) when (string.Equals(ex.ParamName, CatalogArgumentParams.Currency, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }
        catch (ArgumentException ex) when (string.Equals(ex.ParamName, CatalogArgumentParams.Validation, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "catalog_validation", message = ex.Message });
        }
    }

    private static async Task<IResult> DeleteStoreServiceAsync(
        string storeId,
        string serviceId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMarketCatalogSyncService catalog,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Json(new { error = "unauthorized", message = "Sesión requerida." }, statusCode: StatusCodes.Status401Unauthorized);
        var r = await catalog.DeleteStoreServiceAsync(storeId, serviceId, userId, cancellationToken);
        return MapCatalogUpsert(r);
    }

    private static async Task<IResult> GetOfferCardAsync(
        string offerId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMarketCatalogSyncService catalog,
        IOfferService offerService,
        CancellationToken cancellationToken)
    {
        var card = await catalog.TryGetPublicOfferCardAsync(offerId, cancellationToken);
        if (card is null)
            return Results.NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });
        var oid = offerId.Trim();
        var offers = new Dictionary<string, HomeOfferViewDto>(StringComparer.Ordinal) { [oid] = card.Value.Offer };
        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer(request, currentUser);
        await offerService.EnrichHomeOffersAsync(offers, likerKey, cancellationToken);
        if (!offers.TryGetValue(oid, out var enriched))
            return Results.NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });
        return Results.Ok(new { offer = enriched, store = card.Value.Store });
    }

    private static async Task<IResult> PostOfferLikeAsync(
        string offerId,
        ToggleEngagementBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IOfferService offerService,
        IAuthService auth,
        IChatService chat,
        INotificationService notifications,
        CancellationToken cancellationToken)
    {
        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer(request, currentUser);
        if (likerKey is null)
            return Results.Json(new { error = "auth_required", message = "Inicia sesión para dar me gusta." }, statusCode: StatusCodes.Status401Unauthorized);
        if (!await offerService.OfferExistsAsync(offerId, cancellationToken))
            return Results.NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });
        var (liked, likeCount) = await offerService.ToggleOfferLikeAsync(offerId, likerKey, cancellationToken);
        if (liked)
        {
            var sellerId = await chat.GetSellerUserIdForOfferAsync(offerId, cancellationToken);
            if (sellerId is not null)
            {
                var (likerSenderId, likerLabel, likerTrust) =
                    await ResolveEngagementLikerDisplayAsync(likerKey, auth, cancellationToken);
                if (!string.Equals(sellerId, likerSenderId, StringComparison.Ordinal))
                    await notifications.NotifyOfferLikeAsync(
                        new OfferLikeNotificationArgs(sellerId, offerId, likerLabel, likerTrust, likerSenderId),
                        cancellationToken);
            }
        }

        return Results.Ok(new { liked, likeCount });
    }

    private static async Task<IResult> SearchStoreCatalogAsync(
        string storeId,
        string? q,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreCatalogSearchService catalogSearch,
        IOfferService offerService,
        CancellationToken cancellationToken)
    {
        var response = await catalogSearch.SearchPublishedCatalogAsync(storeId, q, cancellationToken);
        if (response is null)
            return Results.NotFound(new { error = "store_not_found", message = "No existe una tienda con ese identificador." });

        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer(request, currentUser);
        await offerService.EnrichStoreCatalogBlockEngagementAsync(
            response.Products,
            response.Services,
            likerKey,
            cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> GetStoreCommentsAsync(
        string storeId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreCommentsService storeComments,
        CancellationToken cancellationToken)
    {
        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer(request, currentUser);
        var list = await storeComments.GetStoreCommentsAsync(storeId, likerKey, cancellationToken);
        if (list is null)
            return Results.NotFound(new { error = "store_not_found", message = "No existe una tienda con ese identificador." });
        return Results.Ok(list);
    }

    private static async Task<IResult> PostStoreCommentAsync(
        string storeId,
        StoreCommentPostBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IAuthService auth,
        IStoreCommentsService storeComments,
        CancellationToken cancellationToken)
    {
        var bearerUserId = currentUser.GetUserId(request);
        if (bearerUserId is null)
            return Results.Json(new { error = "auth_required", message = "Inicia sesión para comentar." }, statusCode: StatusCodes.Status401Unauthorized);

        var text = ((body?.Text) ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return Results.BadRequest(new { error = "invalid_comment", message = "Escribe un comentario." });
        if (text.Length > 12_000)
            return Results.BadRequest(new { error = "invalid_comment", message = "El texto es demasiado largo." });

        var authorId = bearerUserId.Trim();
        var snap = await auth.GetProfileSnapshotByUserIdAsync(authorId, cancellationToken);
        var authorName = string.IsNullOrWhiteSpace(snap?.DisplayName) ? "Usuario" : snap!.DisplayName.Trim();
        var authorTrust = snap?.TrustScore ?? 0;
        var parentId = string.IsNullOrWhiteSpace(body?.ParentId) ? null : body!.ParentId!.Trim();

        try
        {
            var item = await storeComments.AppendStoreCommentAsync(
                storeId,
                text,
                parentId,
                authorId,
                authorName,
                authorTrust,
                body?.CreatedAt,
                cancellationToken);
            if (item is null)
                return Results.NotFound(new { error = "store_not_found", message = "No existe una tienda con ese identificador." });
            return Results.Ok(item);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "invalid_comment", message = ex.Message });
        }
    }

    private static async Task<IResult> PostStoreCommentLikeAsync(
        string storeId,
        string commentId,
        ToggleEngagementBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IStoreCommentsService storeComments,
        CancellationToken cancellationToken)
    {
        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer(request, currentUser);
        if (likerKey is null)
            return Results.Json(new { error = "auth_required", message = "Inicia sesión para dar me gusta." }, statusCode: StatusCodes.Status401Unauthorized);
        var (liked, likeCount) = await storeComments.ToggleStoreCommentLikeAsync(storeId, commentId, likerKey, cancellationToken);
        return Results.Ok(new { liked, likeCount });
    }

    private static async Task<IResult> PostStoreDetailAsync(
        string storeId,
        StoreDetailBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMarketWorkspaceService marketWorkspace,
        IAuthService auth,
        IOfferService offerService,
        CancellationToken cancellationToken)
    {
        var root = await marketWorkspace.GetStoreDetailAsync(storeId, cancellationToken);
        if (root is null)
            return Results.NotFound();
        if (body is not null && (body.ViewerUserId is not null || body.ViewerRole is not null))
        {
            root.Viewer = new StoreDetailViewerView
            {
                UserId = body.ViewerUserId,
                Role = body.ViewerRole,
            };
        }

        if (!string.IsNullOrWhiteSpace(root.Store.OwnerUserId))
        {
            var ownerId = root.Store.OwnerUserId!.Trim();
            var snap = await auth.GetProfileSnapshotByUserIdAsync(ownerId, cancellationToken);
            if (snap is not null)
            {
                root.Owner = new StoreDetailOwnerView
                {
                    Id = snap.Id,
                    Name = snap.DisplayName,
                    AvatarUrl = snap.AvatarUrl,
                    TrustScore = snap.TrustScore,
                };
            }
        }

        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer(request, currentUser);
        await offerService.EnrichStoreCatalogBlockEngagementAsync(
            root.Catalog.Products,
            root.Catalog.Services,
            likerKey,
            cancellationToken);

        return Results.Ok(root);
    }

    /// <summary>
    /// Resuelve el detalle de una tienda por su <b>nombre exacto</b> (normalizado: minúsculas +
    /// espacios colapsados, igual que el cliente y el índice único). Permite URLs públicas
    /// <c>{base}/{nombre}</c> en vez de <c>/store/{id}</c>. Reutiliza la misma lógica que la
    /// resolución por id.
    /// </summary>
    private static async Task<IResult> PostStoreDetailByNameAsync(
        string name,
        StoreDetailBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IMarketWorkspaceService marketWorkspace,
        IAuthService auth,
        IOfferService offerService,
        AppDbContext appDb,
        CancellationToken cancellationToken)
    {
        var normalized = MarketStoreNameNormalizer.Normalize(name);
        if (normalized is null)
            return Results.NotFound();

        // El filtro global de consulta ya excluye tiendas borradas; el índice único garantiza a lo sumo una.
        var storeId = await appDb.Stores.AsNoTracking()
            .Where(s => s.NormalizedName == normalized)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrEmpty(storeId))
            return Results.NotFound();

        return await PostStoreDetailAsync(
            storeId,
            body,
            request,
            currentUser,
            marketWorkspace,
            auth,
            offerService,
            cancellationToken);
    }

    private static IResult MapCatalogUpsert(StoreCatalogUpsertResult r) =>
        r switch
        {
            StoreCatalogUpsertResult.Ok => Results.Ok(),
            StoreCatalogUpsertResult.Unauthorized => Results.Json(new { error = "unauthorized", message = "Sesión requerida." }, statusCode: StatusCodes.Status401Unauthorized),
            StoreCatalogUpsertResult.StoreNotFound => Results.NotFound(),
            StoreCatalogUpsertResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            StoreCatalogUpsertResult.IdMismatch => Results.BadRequest(new { error = "id_mismatch", message = "El id del cuerpo no coincide con la ruta." }),
            StoreCatalogUpsertResult.EntityNotFound => Results.NotFound(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };

    private static string? ResolveEngagementLikerKeyForAuthenticatedViewer(
        HttpRequest request,
        ICurrentUserAccessor currentUser)
    {
        var userId = currentUser.GetUserId(request);
        return string.IsNullOrWhiteSpace(userId) ? null : "u:" + userId.Trim();
    }

    private static async Task<(string SenderId, string Label, int Trust)> ResolveEngagementLikerDisplayAsync(
        string likerKey,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        if (likerKey.StartsWith("u:", StringComparison.Ordinal))
        {
            var uid = likerKey[2..].Trim();
            var snap = await auth.GetProfileSnapshotByUserIdAsync(uid, cancellationToken);
            var name = string.IsNullOrWhiteSpace(snap?.DisplayName) ? "Usuario" : snap!.DisplayName.Trim();
            var trust = snap?.TrustScore ?? 0;
            return (uid, name, trust);
        }

        return ("guest", "Visitante", 0);
    }
}
