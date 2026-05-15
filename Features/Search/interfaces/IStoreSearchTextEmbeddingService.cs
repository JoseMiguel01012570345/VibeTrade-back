namespace VibeTrade.Backend.Features.Search.Interfaces;

public interface IStoreSearchTextEmbeddingService
{
    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
