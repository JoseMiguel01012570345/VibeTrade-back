using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Api;

/// <summary>Persistencia del workspace de mercado (tiendas, ofertas, hilos, rutas públicas).</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class MarketController(IMarketWorkspaceService marketWorkspace) : ControllerBase
{
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
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PutWorkspace([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        await marketWorkspace.SaveAsync(body, cancellationToken);
        return Ok();
    }
}
