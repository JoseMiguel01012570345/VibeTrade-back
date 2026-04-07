using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Api;

/// <summary>Persistencia del workspace de mercado (tiendas, ofertas, hilos, rutas públicas).</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class MarketController(IMarketWorkspaceService marketWorkspace) : ControllerBase
{
    public sealed record StoreDetailBody(string? ViewerUserId, string? ViewerRole);
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

    /// <summary>Reemplaza el snapshot del mercado (misma forma que el store Zustand del frontend).</summary>
    /// <param name="body">JSON con stores, offers, offerIds, storeCatalogs, threads, routeOfferPublic.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPut("workspace")]
    [RequestSizeLimit(104_857_600L)] // 100 MiB; alinear con Kestrel en Program.cs
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PutWorkspace([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        await marketWorkspace.SaveAsync(body, cancellationToken);
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

        return Content(root.ToJsonString(), "application/json");
    }
}
