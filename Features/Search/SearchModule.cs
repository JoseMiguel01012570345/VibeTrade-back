using VibeTrade.Backend.Features.Search.Interfaces;
using VibeTrade.Backend.Infrastructure.Elasticsearch;

namespace VibeTrade.Backend.Features.Search;

public static class SearchModule
{
    public static IServiceCollection AddSearchFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMarketCatalogStoreSearchService, MarketCatalogStoreSearchService>();
        services.Configure<ElasticsearchStoreSearchOptions>(
            configuration.GetSection(ElasticsearchStoreSearchOptions.SectionName));
        services.AddSingleton<IStoreSearchTextEmbeddingService, StoreSearchMlNetTfIdfEmbeddingService>();
        services.AddScoped<IElasticsearchStoreSearchQuery, ElasticsearchStoreSearchQuery>();
        services.AddScoped<IStoreSearchIndexWriter, ElasticsearchStoreSearchIndexWriter>();
        services.AddScoped<ICatalogSearchLiveIndexSync, CatalogSearchLiveIndexSync>();
        return services;
    }
}