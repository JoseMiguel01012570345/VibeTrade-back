namespace VibeTrade.Backend.Features.Search;

public interface IStoreSearchTextEmbeddingService
{
    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
