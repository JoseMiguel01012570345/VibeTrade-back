using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Utils;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Api;

/// <summary>Mercado: workspace (GET), tiendas/catálogo/consultas (PUT por recurso), búsqueda y detalle.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class MarketController(
    IMarketWorkspaceService marketWorkspace,
    IMarketCatalogSyncService catalog,
    IAuthService auth,
    IUserAccountSyncService userAccountSync,
    IRecommendationService recommendations,
    IMarketCatalogStoreSearchService catalogStoreSearch,
    IChatService chat,
    IOfferEngagementService offerEngagement) : ControllerBase
{
    public sealed record CatalogCategoriesResponse(IReadOnlyList<string> Categories);

    public sealed record CurrenciesResponse(IReadOnlyList<string> Currencies);

    public sealed record StoreDetailBody(string? ViewerUserId, string? ViewerRole);

    public sealed record PostInquiryAskedBy(string Id, string Name, int TrustScore);

    public sealed record ToggleEngagementBody(string? GuestId);

    /// <param name="Question">Legado; preferí <paramref name="Text"/>.</param>
    public sealed record PostInquiryBody(
        string OfferId,
        string? Question,
        string? Text,
        string? ParentId,
        PostInquiryAskedBy AskedBy,
        long? CreatedAt);

    /// <summary>Categorías permitidas para productos, servicios y sugerencias en acuerdos (misma lista).</summary>
    [HttpGet("catalog-categories")]
    [ProducesResponseType(typeof(CatalogCategoriesResponse), StatusCodes.Status200OK)]
    public ActionResult<CatalogCategoriesResponse> GetCatalogCategories()
    {
        return Ok(new CatalogCategoriesResponse(CatalogCategories.ProductAndService));
    }

    /// <summary>Monedas permitidas en fichas de catálogo (misma lista para productos y servicios).</summary>
    [HttpGet("currencies")]
    [ProducesResponseType(typeof(CurrenciesResponse), StatusCodes.Status200OK)]
    public ActionResult<CurrenciesResponse> GetCurrencies()
    {
        return Ok(new CurrenciesResponse(CatalogCurrencies.All));
    }

    /// <summary>
    /// Busca en Elasticsearch (índice de catálogo): tiendas, productos y servicios (nombre, categoría, distancia km).
    /// Sin Elasticsearch o si la búsqueda falla: respuesta vacía. Tolerancia a typos: fuzzy de Lucene en la query (<c>ElasticsearchStoreSearchQuery</c>).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("stores/search")]
    [ProducesResponseType(typeof(StoreSearchResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreSearchResponse>> SearchStores(
        [FromQuery] string? name,
        [FromQuery] string? category,
        [FromQuery] string? kinds,
        [FromQuery] int? trustMin,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double? km,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
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
        return Ok(response);
    }

    /// <summary>
    /// Autocomplete público para el input "Buscar" del catálogo (tiendas + ofertas publicadas).
    /// Devuelve sugerencias de texto para <c>name</c> (no ejecuta búsqueda completa).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("stores/autocomplete")]
    [ProducesResponseType(typeof(StoreAutocompleteResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreAutocompleteResponse>> AutocompleteStores(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? kinds,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var response = await catalogStoreSearch.AutocompleteCatalogAsync(
            q,
            category,
            kinds,
            limit,
            cancellationToken);
        return Ok(response);
    }

    /// <summary>Obtiene el snapshot actual del mercado; si la base está vacía, aplica seed embebido.</summary>
    [HttpGet("workspace")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetWorkspace(CancellationToken cancellationToken)
    {
        using var doc = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        var json = doc.RootElement.GetRawText();
        return Content(json, "application/json");
    }

    /// <summary>Lista de tiendas desde tablas relacionales (<c>stores</c>).</summary>
    [HttpGet("workspace/stores")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaceStores(CancellationToken cancellationToken)
    {
        using var doc = await marketWorkspace.GetStoresSnapshotAsync(cancellationToken);
        return Content(doc.RootElement.GetRawText(), "application/json");
    }

    /// <summary>
    /// Metadatos de tienda (perfil). Cuerpo: ficha plana con <c>id</c> (nombre, categorías, etc.) o legado
    /// <c>{"stores":{"&lt;id&gt;":{...}}}</c>. Se fusiona con el workspace en servidor.
    /// </summary>
    [HttpPut("workspace/stores")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PutWorkspaceStores([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        JsonDocument toSave;
        try
        {
            toSave = MarketWorkspaceStoresPutBodyNormalizer.Normalize(body);
        }
        catch (ArgumentException)
        {
            body.Dispose();
            return BadRequest(new { error = "invalid_stores_body", message = "Indicá la tienda con un campo \"id\" o usá la forma \"stores\".{...}." });
        }

        try
        {
            await marketWorkspace.SaveStoreProfilesAsync(toSave, cancellationToken);
        }
        catch (DuplicateStoreNameException)
        {
            return Conflict(new { error = "duplicate_store_name", message = "Ya existe una tienda con ese nombre en la plataforma." });
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }

        return Ok();
    }

    /// <summary>
    /// Catálogo (productos/servicios y pitch). Cuerpo parcial:
    /// <c>{"stores":{...},"storeCatalogs":{"&lt;id&gt;":{...}}}</c>. Se fusiona con el workspace en servidor.
    /// </summary>
    [HttpPut("workspace/catalogs")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PutWorkspaceCatalogs([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        try
        {
            await marketWorkspace.SaveStoreCatalogsAsync(body, cancellationToken);
        }
        catch (DuplicateStoreNameException)
        {
            return Conflict(new { error = "duplicate_store_name", message = "Ya existe una tienda con ese nombre en la plataforma." });
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }

        return Ok();
    }

    /// <summary>Persiste una sola ficha de producto (cuerpo = JSON del producto, sin envolver en catálogo).</summary>
    [HttpPut("stores/{storeId}/products/{productId}")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutStoreProduct(
        string storeId,
        string productId,
        [FromBody] JsonDocument body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        try
        {
            var r = await catalog.UpsertStoreProductAsync(storeId, productId, userId, body.RootElement, cancellationToken);
            return MapCatalogUpsert(r);
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }
    }

    [HttpDelete("stores/{storeId}/products/{productId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteStoreProduct(
        string storeId,
        string productId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        var r = await catalog.DeleteStoreProductAsync(storeId, productId, userId, cancellationToken);
        return MapCatalogUpsert(r);
    }

    /// <summary>Persiste una sola ficha de servicio (cuerpo = JSON del servicio).</summary>
    [HttpPut("stores/{storeId}/services/{serviceId}")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutStoreService(
        string storeId,
        string serviceId,
        [FromBody] JsonDocument body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        try
        {
            var r = await catalog.UpsertStoreServiceAsync(storeId, serviceId, userId, body.RootElement, cancellationToken);
            return MapCatalogUpsert(r);
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }
    }

    [HttpDelete("stores/{storeId}/services/{serviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteStoreService(
        string storeId,
        string serviceId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        var r = await catalog.DeleteStoreServiceAsync(storeId, serviceId, userId, cancellationToken);
        return MapCatalogUpsert(r);
    }

    /// <summary>Comentario público en la ficha (estilo reels: <c>parentId</c> opcional). No abre hilo de chat.</summary>
    [HttpPost("inquiries")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostInquiry([FromBody] PostInquiryBody body, CancellationToken cancellationToken)
    {
        var q = ((body.Question ?? body.Text) ?? "").Trim();
        if (string.IsNullOrWhiteSpace(body.OfferId) || string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "invalid_inquiry", message = "Indicá la oferta y el texto." });
        var askedById = (body.AskedBy?.Id ?? "").Trim();
        var askedByName = (body.AskedBy?.Name ?? "").Trim();
        var askedByTrust = body.AskedBy?.TrustScore ?? 0;
        var isAnonymous = askedById.Length == 0 || string.Equals(askedById, "guest", StringComparison.OrdinalIgnoreCase);
        if (isAnonymous)
        {
            askedById = "guest";
            if (askedByName.Length == 0) askedByName = "Anónimo";
            askedByTrust = 0;
        }

        if (q.Length > 12_000)
            return BadRequest(new { error = "invalid_inquiry", message = "El texto es demasiado largo." });

        var parentId = string.IsNullOrWhiteSpace(body.ParentId) ? null : body.ParentId.Trim();
        var offerOid = body.OfferId.Trim();

        try
        {
            var item = await catalog.AppendOfferInquiryAsync(
                offerOid,
                q,
                parentId,
                askedById,
                askedByName,
                askedByTrust,
                body.CreatedAt,
                cancellationToken);
            if (item is null)
                return NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });

            if (!isAnonymous)
            {
                await recommendations.RecordInteractionAsync(
                    askedById,
                    offerOid,
                    RecommendationInteractionType.Inquiry,
                    cancellationToken);
            }

            var preview = q.Length > 500 ? q[..500] + "…" : q;
            var sellerId = await chat.GetSellerUserIdForOfferAsync(offerOid, cancellationToken);
            if (parentId is null)
            {
                if (sellerId is not null
                    && !string.Equals(askedById, sellerId, StringComparison.Ordinal))
                {
                    await chat.NotifyOfferCommentAsync(
                        sellerId,
                        offerOid,
                        preview,
                        askedByName,
                        askedByTrust,
                        askedById,
                        cancellationToken);
                }
            }
            else
            {
                var parentAuthor = await catalog.TryGetOfferCommentAuthorIdAsync(offerOid, parentId, cancellationToken);
                if (parentAuthor is not null
                    && !string.Equals(parentAuthor, askedById, StringComparison.Ordinal))
                {
                    await chat.NotifyOfferCommentAsync(
                        parentAuthor,
                        offerOid,
                        preview,
                        askedByName,
                        askedByTrust,
                        askedById,
                        cancellationToken);
                }
            }

            await chat.BroadcastOfferCommentsUpdatedAsync(offerOid, cancellationToken);

            return Content(item.ToJsonString(), "application/json");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_inquiry", message = ex.Message });
        }
    }

    /// <summary>Array JSON de comentarios públicos (<c>OfferQaJson</c>) para refrescar la ficha.</summary>
    [HttpGet("offers/{offerId}/qa")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOfferQa(
        string offerId,
        [FromQuery] string? guestId,
        CancellationToken cancellationToken)
    {
        var json = await catalog.GetOfferQaJsonForOfferAsync(offerId, cancellationToken);
        if (json is null)
            return NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });
        var likerKey = ResolveEngagementLikerKey(guestId);
        var enriched = await offerEngagement.EnrichOfferQaJsonAsync(offerId, json, likerKey, cancellationToken);
        return Content(enriched ?? "[]", "application/json");
    }

    /// <summary>Alternar like en la oferta (sesión o <c>guestId</c> en el cuerpo).</summary>
    [HttpPost("offers/{offerId}/like")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostOfferLike(
        string offerId,
        [FromBody] ToggleEngagementBody? body,
        CancellationToken cancellationToken)
    {
        var likerKey = ResolveEngagementLikerKey(body?.GuestId);
        if (likerKey is null)
            return Unauthorized(new { error = "auth_required", message = "Iniciá sesión o enviá guestId (8+ caracteres)." });
        if (!await offerEngagement.OfferExistsAsync(offerId, cancellationToken))
            return NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });
        var (liked, likeCount) = await offerEngagement.ToggleOfferLikeAsync(offerId, likerKey, cancellationToken);
        if (liked)
        {
            var sellerId = await chat.GetSellerUserIdForOfferAsync(offerId, cancellationToken);
            if (sellerId is not null)
            {
                var (likerSenderId, likerLabel, likerTrust) =
                    await ResolveEngagementLikerDisplayAsync(likerKey, cancellationToken);
                if (!string.Equals(sellerId, likerSenderId, StringComparison.Ordinal))
                    await chat.NotifyOfferLikeAsync(sellerId, offerId, likerLabel, likerTrust, likerSenderId, cancellationToken);
            }
        }

        return Ok(new { liked, likeCount });
    }

    /// <summary>Alternar like en un comentario QA (<c>id</c> del ítem en <c>OfferQaJson</c>).</summary>
    [HttpPost("offers/{offerId}/qa/{qaCommentId}/like")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostOfferQaCommentLike(
        string offerId,
        string qaCommentId,
        [FromBody] ToggleEngagementBody? body,
        CancellationToken cancellationToken)
    {
        var likerKey = ResolveEngagementLikerKey(body?.GuestId);
        if (likerKey is null)
            return Unauthorized(new { error = "auth_required", message = "Iniciá sesión o enviá guestId (8+ caracteres)." });
        if (!await offerEngagement.OfferExistsAsync(offerId, cancellationToken))
            return NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });
        var (liked, likeCount) = await offerEngagement.ToggleQaCommentLikeAsync(
            offerId,
            qaCommentId,
            likerKey,
            cancellationToken);
        if (liked)
        {
            var authorId = await catalog.TryGetOfferCommentAuthorIdAsync(offerId, qaCommentId, cancellationToken);
            var aid = (authorId ?? "").Trim();
            if (aid.Length > 0 && !string.Equals(aid, "guest", StringComparison.OrdinalIgnoreCase))
            {
                var authorProfile = await userAccountSync.GetProfileSnapshotByUserIdAsync(aid, cancellationToken);
                if (authorProfile is not null)
                {
                    var (likerSenderId, likerLabel, likerTrust) =
                        await ResolveEngagementLikerDisplayAsync(likerKey, cancellationToken);
                    if (!string.Equals(aid, likerSenderId, StringComparison.Ordinal))
                        await chat.NotifyQaCommentLikeAsync(aid, offerId, likerLabel, likerTrust, likerSenderId, cancellationToken);
                }
            }
        }

        return Ok(new { liked, likeCount });
    }

    private async Task<(string SenderId, string Label, int Trust)> ResolveEngagementLikerDisplayAsync(
        string likerKey,
        CancellationToken cancellationToken)
    {
        if (likerKey.StartsWith("u:", StringComparison.Ordinal))
        {
            var uid = likerKey[2..].Trim();
            var snap = await userAccountSync.GetProfileSnapshotByUserIdAsync(uid, cancellationToken);
            var name = string.IsNullOrWhiteSpace(snap?.DisplayName) ? "Usuario" : snap!.DisplayName.Trim();
            var trust = snap?.TrustScore ?? 0;
            return (uid, name, trust);
        }

        return ("guest", "Visitante", 0);
    }

    private string? ResolveEngagementLikerKey(string? guestId)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (!string.IsNullOrWhiteSpace(userId))
            return "u:" + userId.Trim();
        var g = (guestId ?? "").Trim();
        return g.Length >= 8 ? "g:" + g : null;
    }

    /// <summary>Sincronización masiva de <c>offers[*].qa</c> (legado / herramientas).</summary>
    [HttpPut("inquiries")]
    [HttpPut("workspace/inquiries")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PutWorkspaceInquiries([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        try
        {
            await marketWorkspace.SaveOfferInquiriesAsync(body, cancellationToken);
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }

        return Ok();
    }

    /// <summary>
    /// Detalle de tienda + catálogo (carga bajo demanda). El cuerpo identifica al visitante para futura personalización.
    /// </summary>
    [HttpPost("stores/{storeId}/detail")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostStoreDetail(
        string storeId,
        [FromBody] StoreDetailBody? body,
        CancellationToken cancellationToken)
    {
        using var doc = await marketWorkspace.GetStoreDetailAsync(storeId, cancellationToken);
        if (doc is null)
            return NotFound();
        var root = JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
        if (body is not null && (body.ViewerUserId is not null || body.ViewerRole is not null))
        {
            root["viewer"] = new JsonObject
            {
                ["userId"] = body.ViewerUserId is null ? null : JsonValue.Create(body.ViewerUserId),
                ["role"] = body.ViewerRole is null ? null : JsonValue.Create(body.ViewerRole),
            };
        }

        if (root["store"] is JsonObject storeObj &&
            storeObj.TryGetPropertyValue("ownerUserId", out var ouNode) &&
            ouNode is JsonValue ouVal &&
            ouVal.TryGetValue<string>(out var ownerId) &&
            !string.IsNullOrWhiteSpace(ownerId))
        {
            var snap = await userAccountSync.GetProfileSnapshotByUserIdAsync(ownerId, cancellationToken);
            if (snap is not null)
            {
                root["owner"] = new JsonObject
                {
                    ["id"] = snap.Id,
                    ["name"] = snap.DisplayName,
                    ["avatarUrl"] = snap.AvatarUrl is { } a ? JsonValue.Create(a) : null,
                    ["trustScore"] = snap.TrustScore,
                };
            }
        }

        return Content(root.ToJsonString(), "application/json");
    }

    private IActionResult MapCatalogUpsert(StoreCatalogUpsertResult r) =>
        r switch
        {
            StoreCatalogUpsertResult.Ok => Ok(),
            StoreCatalogUpsertResult.Unauthorized => Unauthorized(new { error = "unauthorized", message = "Sesión requerida." }),
            StoreCatalogUpsertResult.StoreNotFound => NotFound(),
            StoreCatalogUpsertResult.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            StoreCatalogUpsertResult.IdMismatch => BadRequest(new { error = "id_mismatch", message = "El id del cuerpo no coincide con la ruta." }),
            StoreCatalogUpsertResult.EntityNotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
}
