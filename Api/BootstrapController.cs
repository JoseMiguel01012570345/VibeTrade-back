using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Bootstrap;

namespace VibeTrade.Backend.Api;

/// <summary>Carga inicial del cliente web: mercado persistido, reels vacíos y nombres de perfil vacíos.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class BootstrapController(IBootstrapService bootstrap) : ControllerBase
{
    /// <summary>Devuelve market, reels y profileDisplayNames.</summary>
    /// <remarks>Incluye cabecera recomendada <c>X-Timezone</c> (IANA) en todas las peticiones del cliente.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        using var doc = await bootstrap.GetBootstrapAsync(cancellationToken);
        var json = doc.RootElement.GetRawText();
        return Content(json, "application/json");
    }
}
