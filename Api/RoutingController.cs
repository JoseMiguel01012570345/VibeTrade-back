using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Routing;

namespace VibeTrade.Backend.Api;

/// <summary>Rutas y métricas de recorrido (proxy controlado hacia OSRM).</summary>
[ApiController]
[Route("api/v1/routing")]
[Produces("application/json")]
[Tags("Routing")]
public sealed class RoutingController(IOsrmLegDistanceService osrm) : ControllerBase
{
    /// <summary>
    /// Distancia por tramo en km siguiendo la red (OSRM), un valor por cada par consecutivo de waypoints.
    /// </summary>
    /// <remarks>Requiere <c>Authorization: Bearer</c>.</remarks>
    [HttpPost("leg-distances")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> PostLegDistances(
        [FromBody] RoutingLegDistancesRequest? body,
        CancellationToken cancellationToken)
    {
        var bad = ValidatePositions(body, out var tuples);
        if (bad is not null)
            return bad;

        var km = await osrm.GetLegDistancesKmAsync(tuples, cancellationToken);
        if (km is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "routing_unavailable",
                message = "No se pudo calcular la ruta con OSRM.",
            });
        }

        return Ok(new { legsKm = km });
    }

    private static IActionResult? ValidatePositions(
        RoutingLegDistancesRequest? body,
        out List<(double Lat, double Lng)> tuples)
    {
        tuples = new List<(double Lat, double Lng)>();
        if (body?.Positions is null || body.Positions.Count < 2)
        {
            return new BadRequestObjectResult(new
            {
                error = "invalid_request",
                message = "Se requieren al menos 2 posiciones [lat, lng].",
            });
        }

        if (body.Positions.Count > 100)
        {
            return new BadRequestObjectResult(new
            {
                error = "invalid_request",
                message = "Máximo 100 waypoints.",
            });
        }

        foreach (var pair in body.Positions)
        {
            if (pair is null || pair.Length < 2)
            {
                return new BadRequestObjectResult(new
                {
                    error = "invalid_request",
                    message = "Cada posición debe ser [lat, lng].",
                });
            }

            var lat = pair[0];
            var lng = pair[1];
            if (double.IsNaN(lat) || double.IsNaN(lng) || double.IsInfinity(lat) || double.IsInfinity(lng))
            {
                return new BadRequestObjectResult(new
                {
                    error = "invalid_request",
                    message = "Coordenadas no numéricas.",
                });
            }

            if (lat is < -90 or > 90 || lng is < -180 or > 180)
            {
                return new BadRequestObjectResult(new
                {
                    error = "invalid_request",
                    message = "Latitud o longitud fuera de rango.",
                });
            }

            tuples.Add((lat, lng));
        }

        return null;
    }
}

/// <summary>Cuerpo: waypoints en orden; cada elemento es <c>[lat, lng]</c>.</summary>
public sealed class RoutingLegDistancesRequest
{
    public IReadOnlyList<double[]?>? Positions { get; set; }
}
