namespace VibeTrade.Backend.Features.Recommendations.Popularity;

public sealed class OfferPopularityWeightOptions
{
    public const string SectionName = "PopularityWeight";

    /// <summary>
    /// Si es true, tras unos segundos de arranque se ejecuta <see cref="IOfferPopularityWeightService.RecomputeAllPublishedAsync"/>.
    /// Útil para rellenar la columna tras migrar; desactivar en producción si el dataset es muy grande.
    /// </summary>
    public bool BackfillOnStartup { get; set; }
}
