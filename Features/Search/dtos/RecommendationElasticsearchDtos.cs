namespace VibeTrade.Backend.Features.Search.Dtos;

/// <summary>Hit léxico en índice de catálogo para recomendaciones.</summary>
public sealed record RecommendationElasticsearchHit(string OfferId, double Score, string Kind);
