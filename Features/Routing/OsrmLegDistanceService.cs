using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace VibeTrade.Backend.Features.Routing;

/// <summary>Consulta OSRM <c>route/v1/driving</c> solo para <c>legs[].distance</c> (km por tramo).</summary>
public sealed class OsrmLegDistanceService(
    HttpClient http,
    IOptions<RoutingOptions> options,
    ILogger<OsrmLegDistanceService> log) : IOsrmLegDistanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<double>?> GetLegDistancesKmAsync(
        IReadOnlyList<(double Lat, double Lng)> positions,
        CancellationToken cancellationToken = default)
    {
        if (positions.Count < 2)
            return null;

        var url =
            $"{BuildBaseDrivingUrl(positions)}?overview=false&steps=false";

        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            log.LogWarning(
                "OSRM leg distances: HTTP {Status} for {Count} waypoints",
                (int)response.StatusCode,
                positions.Count);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var data = await JsonSerializer.DeserializeAsync<OsrmRouteEnvelope>(stream, JsonOptions, cancellationToken);
        return ParseLegsKm(data, positions.Count);
    }

    private string BuildBaseDrivingUrl(IReadOnlyList<(double Lat, double Lng)> positions)
    {
        var baseUrl = options.Value.OsrmRouteV1BaseUrl.TrimEnd('/');
        var coordPath = string.Join(
            ";",
            positions.Select(p =>
                $"{p.Lng.ToString(CultureInfo.InvariantCulture)},{p.Lat.ToString(CultureInfo.InvariantCulture)}"));
        return $"{baseUrl}/driving/{coordPath}";
    }

    private IReadOnlyList<double>? ParseLegsKm(OsrmRouteEnvelope? data, int waypointCount)
    {
        if (data is null || !string.Equals(data.Code, "Ok", StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning("OSRM leg distances: invalid payload (code {Code})", data?.Code);
            return null;
        }

        var routes = data.Routes;
        if (routes is null || routes.Count == 0)
        {
            log.LogWarning("OSRM leg distances: no routes");
            return null;
        }

        var legs = routes[0].Legs;
        if (legs is null || legs.Count == 0)
        {
            log.LogWarning("OSRM leg distances: no legs");
            return null;
        }

        if (legs.Count != waypointCount - 1)
        {
            log.LogWarning(
                "OSRM leg distances: expected {Expected} legs, got {Actual}",
                waypointCount - 1,
                legs.Count);
            return null;
        }

        return legs.ConvertAll(l => l.Distance / 1000d);
    }

    private sealed class OsrmRouteEnvelope
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("routes")]
        public List<OsrmRouteDto>? Routes { get; set; }
    }

    private sealed class OsrmRouteDto
    {
        [JsonPropertyName("legs")]
        public List<OsrmLegDto>? Legs { get; set; }
    }

    private sealed class OsrmLegDto
    {
        [JsonPropertyName("distance")]
        public double Distance { get; set; }
    }
}
