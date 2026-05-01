using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

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
    private const int MaxLatLngPointsPerLeg = 4000;

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

            latLngs = DrivingPolylineMerge.TrimDuplicateJoin(prevLast, latLngs);
            if (latLngs.Count < 2)
            {
                log.LogWarning("GraphHopper: tramo {Leg} quedó sin puntos suficientes tras deduplicar.", i + 1);
                return null;
            }

            latLngs = DrivingPolylineMerge.SubsampleIfNeeded(latLngs, MaxLatLngPointsPerLeg);
            prevLast = latLngs[^1];
            segmentPolylines.Add(latLngs);
            results.Add(new DrivingLegResult(km, latLngs));
        }

        var mergedRoute = DrivingPolylineMerge.AppendSegmentsDedupe(segmentPolylines);
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
        var q =
            $"route?type=json&points_encoded=false&profile={Uri.EscapeDataString(profile)}"
            + $"&point={Invariant(from.Lat)},{Invariant(from.Lng)}"
            + $"&point={Invariant(to.Lat)},{Invariant(to.Lng)}";
        if (apiKey.Length > 0)
            q += $"&key={Uri.EscapeDataString(apiKey)}";

        using var response = await http.GetAsync(q, cancellationToken);
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
        return ParsePath(data);
    }

    private static (double DistanceKm, List<List<double>> Coordinates)? ParsePath(GhRouteEnvelope? data)
    {
        if (data?.Paths is null || data.Paths.Count == 0)
            return null;
        var path = data.Paths[0];
        var km = path.Distance / 1000d;
        var coords = path.Points?.Coordinates;
        if (coords is null || coords.Count < 2)
            return null;

        var latLng = new List<List<double>>();
        foreach (var c in coords)
        {
            if (c.Count < 2)
                continue;
            var lng = c[0];
            var lat = c[1];
            var pair = new List<double> { lat, lng };
            if (latLng.Count > 0 && DrivingPolylineMerge.SameLatLng(latLng[^1], pair))
                continue;
            latLng.Add(pair);
        }

        return latLng.Count >= 2 ? (km, latLng) : null;
    }

    private static string Invariant(double v) => v.ToString(CultureInfo.InvariantCulture);

    private sealed class GhRouteEnvelope
    {
        [JsonPropertyName("paths")]
        public List<GhPathDto>? Paths { get; set; }
    }

    private sealed class GhPathDto
    {
        [JsonPropertyName("distance")]
        public double Distance { get; set; }

        [JsonPropertyName("points")]
        public GhGeoJsonDto? Points { get; set; }
    }

    private sealed class GhGeoJsonDto
    {
        [JsonPropertyName("coordinates")]
        public List<List<double>>? Coordinates { get; set; }
    }
}
