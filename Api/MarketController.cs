using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Market.Utils;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Api;

/// <summary>Mercado: workspace JSON, CRUD de fichas, búsqueda, QA público, engagement y detalle de tienda.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Market")]
public sealed class MarketController(
    AppDbContext appDb,
    IMarketWorkspaceService marketWorkspace,
    IMarketCatalogSyncService catalog,
    ICurrentUserAccessor currentUser,
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

    /// <summary>Cuerpo para <c>POST /inquiries</c> (la API usa la sesión para <c>askedBy</c>).</summary>
    /// <param name="OfferId">Id del producto o servicio (oferta).</param>
    /// <param name="Question">Legado; preferí <paramref name="Text"/>.</param>
    /// <param name="Text">Texto de la pregunta o comentario público.</param>
    /// <param name="ParentId">Opcional: id del comentario padre (hilo tipo reels).</param>
    /// <param name="AskedBy">En el DTO de cliente; el servidor puede sobreescribir con la sesión.</param>
    /// <param name="CreatedAt">Epoch ms opcional (cliente).</param>
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
    /// Servicios de catálogo publicados del usuario en tiendas con transporte (<c>TransportIncluded</c>), para elegir ficha al invitar por teléfono en la hoja de ruta.
    /// </summary>
    [HttpGet("users/{userId}/published-transport-services")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPublishedTransportServicesForUser(
        string userId,
        CancellationToken cancellationToken)
    {
        var bearer = currentUser.GetUserId(Request);
        if (bearer is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        var uid = (userId ?? "").Trim();
        if (uid.Length < 2)
            return BadRequest(new { error = "invalid_user", message = "Usuario inválido." });

        var storeIds = await appDb.Stores.AsNoTracking()
            .Where(s => s.OwnerUserId == uid)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        if (storeIds.Count == 0)
            return Ok(new { services = Array.Empty<object>() });

        var services = await appDb.StoreServices.AsNoTracking()
            .Include(s => s.Store)
            .Where(s => storeIds.Contains(s.StoreId)
                && s.DeletedAtUtc == null
                && s.Published == true)
            .OrderBy(s => s.TipoServicio)
            .ThenBy(s => s.Category)
            .ToListAsync(cancellationToken);

        // Misma forma que el cliente (`publishedTransportServicesApi`): ficha plana + storeName, no anidada en `service`.
        var list = services.Select(s =>
        {
            var v = MarketCatalogRowViewFactory.ServiceFromRow(s);
            return new
            {
                v.Id,
                v.StoreId,
                storeName = s.Store?.Name ?? "",
                v.Category,
                v.TipoServicio,
                v.Descripcion,
                v.Incluye,
                v.NoIncluye,
                v.Entregables,
                v.PropIntelectual,
                v.Published,
                v.Monedas,
                v.CustomFields,
                v.PhotoUrls,
                v.Riesgos,
                v.Dependencias,
                v.Garantias,
                v.Qa,
                v.PublicCommentCount,
                v.OfferLikeCount,
                v.ViewerLikedOffer,
            };
        }).ToList();

        return Ok(new { services = list });
    }

    /// <summary>
    /// Busca en Elasticsearch (índice de catálogo): tiendas, productos, servicios y publicaciones emergentes <c>emo_*</c> (nombre, categoría, distancia km).
    /// Sin Elasticsearch o si la búsqueda falla: respuesta vacía. Tolerancia a typos: fuzzy de Lucene en la query (<c>ElasticsearchStoreSearchQuery</c>).
    /// </summary>
    /// <param name="name">Texto libre (nombre de tienda u oferta).</param>
    /// <param name="category">Filtro por categoría de catálogo.</param>
    /// <param name="kinds">Tipos de resultado (convención del servicio de búsqueda).</param>
    /// <param name="trustMin">Puntuación mínima de confianza.</param>
    /// <param name="lat">Latitud WGS84 para radio.</param>
    /// <param name="lng">Longitud WGS84 para radio.</param>
    /// <param name="km">Radio en kilómetros alrededor de <paramref name="lat"/>/<paramref name="lng"/>.</param>
    /// <param name="limit">Tamaño de página.</param>
    /// <param name="offset">Desplazamiento.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
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
    /// <param name="q">Prefijo o fragmento de búsqueda.</param>
    /// <param name="category">Filtro opcional.</param>
    /// <param name="kinds">Tipos de sugerencia.</param>
    /// <param name="limit">Máximo de ítems.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
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

    /// <summary>Obtiene el snapshot actual del mercado (tiendas, ofertas, ids) materializado desde PostgreSQL.</summary>
    [HttpGet("workspace")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetWorkspace(CancellationToken cancellationToken)
    {
        var root = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        return Ok(root);
    }

    /// <summary>Lista de tiendas desde tablas relacionales (<c>stores</c>).</summary>
    [HttpGet("workspace/stores")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaceStores(CancellationToken cancellationToken)
    {
        var snapshot = await marketWorkspace.GetStoresSnapshotAsync(cancellationToken);
        return Ok(snapshot);
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
    public async Task<IActionResult> PutWorkspaceStores(
        [FromBody] WorkspaceStorePutRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            await marketWorkspace.SaveStoreProfilesAsync(body, cancellationToken);
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "invalid_stores_body", message = "Indica la tienda con un campo \"id\" o usa la forma \"stores\".{...}." });
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
    public async Task<IActionResult> PutWorkspaceCatalogs(
        [FromBody] WorkspaceStoreCatalogsPutRequest body,
        CancellationToken cancellationToken)
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

    /// <summary>Borrado lógico de la tienda y de sus fichas de catálogo (<c>DeletedAtUtc</c>); solo el dueño.</summary>
    [HttpDelete("stores/{storeId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteStore(string storeId, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        var r = await catalog.DeleteStoreAsync(storeId, userId, cancellationToken);
        return r switch
        {
            StoreCatalogUpsertResult.Ok => NoContent(),
            StoreCatalogUpsertResult.Unauthorized => Unauthorized(new { error = "unauthorized", message = "Sesión requerida." }),
            StoreCatalogUpsertResult.StoreNotFound => NotFound(),
            StoreCatalogUpsertResult.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            StoreCatalogUpsertResult.IdMismatch => BadRequest(new { error = "id_mismatch", message = "El id del cuerpo no coincide con la ruta." }),
            StoreCatalogUpsertResult.EntityNotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>Alta/edición de una ficha de producto (dueño de la tienda); cuerpo = JSON de producto.</summary>
    /// <param name="storeId">Id de la tienda.</param>
    /// <param name="productId">Id estable del producto.</param>
    /// <param name="body">Documento JSON de la ficha de producto.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
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
        [FromBody] StoreProductPutRequest body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        try
        {
            var r = await catalog.UpsertStoreProductAsync(storeId, productId, userId, body, cancellationToken);
            return MapCatalogUpsert(r);
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }
    }

    /// <summary>Elimina un producto del catálogo (dueño de la tienda).</summary>
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
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        var r = await catalog.DeleteStoreProductAsync(storeId, productId, userId, cancellationToken);
        return MapCatalogUpsert(r);
    }

    /// <summary>Alta/edición de una ficha de servicio (dueño de la tienda).</summary>
    /// <param name="storeId">Id de la tienda.</param>
    /// <param name="serviceId">Id estable del servicio.</param>
    /// <param name="body">Documento JSON de la ficha de servicio.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
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
        [FromBody] StoreServicePutRequest body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        try
        {
            var r = await catalog.UpsertStoreServiceAsync(storeId, serviceId, userId, body, cancellationToken);
            return MapCatalogUpsert(r);
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }
        catch (CatalogValidationException ex)
        {
            return BadRequest(new { error = "catalog_validation", message = ex.Message });
        }
    }

    /// <summary>Elimina un servicio del catálogo (dueño de la tienda).</summary>
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
        var userId = currentUser.GetUserId(Request);
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostInquiry([FromBody] PostInquiryBody body, CancellationToken cancellationToken)
    {
        var bearerUserId = currentUser.GetUserId(Request);
        if (bearerUserId is null)
            return Unauthorized(new { error = "auth_required", message = "Inicia sesión para comentar." });

        var q = ((body.Question ?? body.Text) ?? "").Trim();
        if (string.IsNullOrWhiteSpace(body.OfferId) || string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "invalid_inquiry", message = "Indica la oferta y el texto." });

        var askedById = bearerUserId.Trim();
        var snap = await userAccountSync.GetProfileSnapshotByUserIdAsync(askedById, cancellationToken);
        var askedByName = string.IsNullOrWhiteSpace(snap?.DisplayName) ? "Usuario" : snap!.DisplayName.Trim();
        var askedByTrust = snap?.TrustScore ?? 0;

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

            await recommendations.RecordInteractionAsync(
                askedById,
                offerOid,
                RecommendationInteractionType.Inquiry,
                cancellationToken);

            var preview = q.Length > 500 ? q[..500] + "…" : q;
            var sellerId = await chat.GetSellerUserIdForOfferAsync(offerOid, cancellationToken);
            if (parentId is null)
            {
                if (sellerId is not null
                    && !string.Equals(askedById, sellerId, StringComparison.Ordinal))
                {
                    await chat.NotifyOfferCommentAsync(
                        new OfferCommentNotificationArgs(
                            sellerId,
                            offerOid,
                            preview,
                            askedByName,
                            askedByTrust,
                            askedById),
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
                        new OfferCommentNotificationArgs(
                            parentAuthor,
                            offerOid,
                            preview,
                            askedByName,
                            askedByTrust,
                            askedById),
                        cancellationToken);
                }
            }

            await chat.BroadcastOfferCommentsUpdatedAsync(offerOid, cancellationToken);

            return Ok(item);
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
        CancellationToken cancellationToken)
    {
        var qa = await catalog.GetOfferQaForOfferAsync(offerId, cancellationToken);
        if (qa is null)
            return NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });
        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer();
        var enriched = await offerEngagement.EnrichOfferQaListAsync(offerId, qa, likerKey, cancellationToken);
        return Ok(enriched);
    }

    /// <summary>Hidrata ficha y tienda (p. ej. enlace directo a <c>/offer/…</c> sin pasar por el home).</summary>
    [HttpGet("offers/{offerId}/card")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOfferCard(string offerId, CancellationToken cancellationToken)
    {
        var card = await catalog.TryGetPublicOfferCardAsync(offerId, cancellationToken);
        if (card is null)
            return NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });
        var oid = offerId.Trim();
        var offers = new Dictionary<string, HomeOfferViewDto>(StringComparer.Ordinal) { [oid] = card.Value.Offer };
        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer();
        await offerEngagement.EnrichHomeOffersAsync(offers, likerKey, cancellationToken);
        if (!offers.TryGetValue(oid, out var enriched))
            return NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });
        return Ok(new { offer = enriched, store = card.Value.Store });
    }

    /// <summary>Alterna el like en la oferta (requiere Bearer; no invitado anónimo).</summary>
    [HttpPost("offers/{offerId}/like")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostOfferLike(
        string offerId,
        [FromBody] ToggleEngagementBody? body,
        CancellationToken cancellationToken)
    {
        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer();
        if (likerKey is null)
            return Unauthorized(new { error = "auth_required", message = "Inicia sesión para dar me gusta." });
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
                    await chat.NotifyOfferLikeAsync(
                        new OfferLikeNotificationArgs(sellerId, offerId, likerLabel, likerTrust, likerSenderId),
                        cancellationToken);
            }
        }

        return Ok(new { liked, likeCount });
    }

    /// <summary>Alterna like en un ítem QA (id del comentario en el array persistido).</summary>
    [HttpPost("offers/{offerId}/qa/{qaCommentId}/like")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostOfferQaCommentLike(
        string offerId,
        string qaCommentId,
        [FromBody] ToggleEngagementBody? body,
        CancellationToken cancellationToken)
    {
        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer();
        if (likerKey is null)
            return Unauthorized(new { error = "auth_required", message = "Inicia sesión para dar me gusta." });
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
                        await chat.NotifyQaCommentLikeAsync(
                            new QaCommentLikeNotificationArgs(aid, offerId, likerLabel, likerTrust, likerSenderId),
                            cancellationToken);
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

    /// <summary>Clave de engagement <c>u:…</c> solo con Bearer; sin invitado anónimo.</summary>
    private string? ResolveEngagementLikerKeyForAuthenticatedViewer()
    {
        var userId = currentUser.GetUserId(Request);
        return string.IsNullOrWhiteSpace(userId) ? null : "u:" + userId.Trim();
    }

    /// <summary>Sincronización masiva de bloques <c>qa</c> del workspace (legado / importación).</summary>
    [HttpPut("inquiries")]
    [HttpPut("workspace/inquiries")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PutWorkspaceInquiries(
        [FromBody] WorkspaceInquiriesPutRequest body,
        CancellationToken cancellationToken)
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
    /// Detalle de tienda + catálogo completo; enriquece ofertas con likes/comentarios según sesión opcional.
    /// </summary>
    /// <param name="storeId">Id de la tienda.</param>
    /// <param name="body">Opcional: <c>viewerUserId</c> / <c>viewerRole</c> para metadatos en la respuesta.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("stores/{storeId}/detail")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostStoreDetail(
        string storeId,
        [FromBody] StoreDetailBody? body,
        CancellationToken cancellationToken)
    {
        var root = await marketWorkspace.GetStoreDetailAsync(storeId, cancellationToken);
        if (root is null)
            return NotFound();
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
            var snap = await userAccountSync.GetProfileSnapshotByUserIdAsync(ownerId, cancellationToken);
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

        var likerKey = ResolveEngagementLikerKeyForAuthenticatedViewer();
        await offerEngagement.EnrichStoreCatalogBlockEngagementAsync(
            root.Catalog.Products,
            root.Catalog.Services,
            likerKey,
            cancellationToken);

        return Ok(root);
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
