using System.Text.Json.Serialization;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Bootstrap;

public sealed class BootstrapResponseDto
{
    [JsonPropertyName("market")]
    public required MarketWorkspaceState Market { get; set; }

    [JsonPropertyName("reels")]
    public required BootstrapReelsStateDto Reels { get; set; }

    [JsonPropertyName("profileDisplayNames")]
    public required Dictionary<string, string> ProfileDisplayNames { get; set; }

    [JsonPropertyName("savedOfferIds")]
    public required IReadOnlyList<string> SavedOfferIds { get; set; }

    [JsonPropertyName("recommendations")]
    public required RecommendationBatchResponse Recommendations { get; set; }
}

public sealed class BootstrapReelsStateDto
{
    public IReadOnlyList<object> Items { get; set; } = Array.Empty<object>();

    [JsonPropertyName("initialComments")]
    public IReadOnlyDictionary<string, object> InitialComments { get; set; } = new Dictionary<string, object>();

    [JsonPropertyName("initialLikeCounts")]
    public IReadOnlyDictionary<string, object> InitialLikeCounts { get; set; } = new Dictionary<string, object>();
}
