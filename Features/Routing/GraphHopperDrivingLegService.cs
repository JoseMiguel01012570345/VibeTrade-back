using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Features.Routing;

/// <summary>
/// Una petición GET <c>/route</c> por tramo (dos puntos); las polilíneas se concatenan con deduplicación en empalmes.
/// Documentación: https://docs.graphhopper.com/openapi
/// </summary>
public sealed class GraphHopperDrivingLegService(
    HttpClient http,
    IOptions<RoutingOptions> options,
    ILogger<GraphHopperDrivingLegService> log) : IDrivingLegRoutingService
{
    private static readonly TooManyRequestsRetryOptions GraphHopperTooManyRequestsRetry = new()
    {
        TotalBudget = TimeSpan.FromSeconds(60),
        InitialBackoff = TimeSpan.FromSeconds(1),
        MaxBackoff = TimeSpan.FromSeconds(60),
        OperationName = "GraphHopper route",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<double>?> GetLegDistancesKmAsync(
        IReadOnlyList<(double Lat, double Lng)> positions,
        CancellationToken cancellationToken = default)
    {
        var legs = await GetDrivingLegsAsync(positions, cancellationToken);
        return legs?.Select(l => l.DistanceKm).ToList();
    }

    public async Task<IReadOnlyList<DrivingLegResult>?> GetDrivingLegsAsync(
        IReadOnlyList<(double Lat, double Lng)> positions,
        CancellationToken cancellationToken = default)
    {
        if (positions.Count < 2)
            return null;

        if (http.BaseAddress is null)
        {
            log.LogWarning(
                "GraphHopper: falta URL base en appsettings ({Section}:{Property}).",
                RoutingOptions.SectionName,
                nameof(RoutingOptions.GraphHopperBaseUrl));
            return null;
        }

        var opt = options.Value;
        var key = (opt.GraphHopperApiKey ?? "").Trim();
        if (string.IsNullOrEmpty(key))
        {
            log.LogWarning(
                "GraphHopper: falta API key ({Section}:{Property}); las peticiones fallarán en graphhopper.com.",
                RoutingOptions.SectionName,
                nameof(RoutingOptions.GraphHopperApiKey));
        }

        var profile = (opt.GraphHopperProfile ?? "car").Trim();
        if (profile.Length == 0)
            profile = "car";

        var segmentPolylines = new List<List<List<double>>>();
        var results = new List<DrivingLegResult>();
        List<double>? prevLast = null;

        for (var i = 0; i < positions.Count - 1; i++)
        {
            var from = positions[i];
            var to = positions[i + 1];
            var parsed = await FetchSingleLegAsync(from, to, profile, key, cancellationToken);
            if (parsed is null)
                return null;

            var (km, latLngs) = parsed.Value;
            if (latLngs is null || latLngs.Count < 2)
            {
                log.LogWarning(
                    "GraphHopper: geometría vacía para tramo {Leg} ({Lat1},{Lng1})→({Lat2},{Lng2}).",
                    i + 1,
                    from.Lat,
                    from.Lng,
                    to.Lat,
                    to.Lng);
                return null;
            }

            latLngs = RoutingUtils.TrimPolylineDuplicateJoin(prevLast, latLngs);
            if (latLngs.Count < 2)
            {
                log.LogWarning("GraphHopper: tramo {Leg} quedó sin puntos suficientes tras deduplicar.", i + 1);
                return null;
            }

            latLngs = RoutingUtils.SubsamplePolylineIfNeeded(latLngs, RoutingUtils.DefaultMaxLatLngPointsPerLeg);
            prevLast = latLngs[^1];
            segmentPolylines.Add(latLngs);
            results.Add(new DrivingLegResult(km, latLngs));
        }

        var mergedRoute = RoutingUtils.AppendPolylineSegmentsDedupe(segmentPolylines);
        if (mergedRoute.Count >= 2)
            log.LogTrace(
                "GraphHopper: ruta concatenada {Segments} tramos → {Points} puntos (deduplicado).",
                segmentPolylines.Count,
                mergedRoute.Count);

        return results;
    }

    private async Task<(double DistanceKm, List<List<double>> Coordinates)?> FetchSingleLegAsync(
        (double Lat, double Lng) from,
        (double Lat, double Lng) to,
        string profile,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var q = RoutingUtils.BuildGraphHopperRouteQuery(from, to, profile, apiKey);

        using var response = await GeneralUtils.SendWithTooManyRequestsRetryAsync(
            () => http.GetAsync(q, cancellationToken),
            GraphHopperTooManyRequestsRetry,
            log,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var body429 = await response.Content.ReadAsStringAsync(cancellationToken);
            log.LogWarning(
                "GraphHopper route: HTTP 429 (Too Many Requests) tras ~{Budget}s de reintentos. Body (truncado): {Body}",
                GraphHopperTooManyRequestsRetry.TotalBudget.TotalSeconds,
                body429.Length > 400 ? body429[..400] + "…" : body429);
            return null;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            log.LogWarning(
                "GraphHopper route: HTTP {Status}. Body (truncated): {Body}",
                (int)response.StatusCode,
                body.Length > 400 ? body[..400] + "…" : body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var data = await JsonSerializer.DeserializeAsync<GhRouteEnvelope>(stream, JsonOptions, cancellationToken);
        return RoutingUtils.ParseGraphHopperLegPath(data);
    }
}
