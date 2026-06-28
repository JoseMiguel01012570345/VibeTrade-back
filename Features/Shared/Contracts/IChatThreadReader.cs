namespace VibeTrade.Backend.Features.Shared.Contracts;

/// <summary>Read-only access to chat thread metadata across features.</summary>
public interface IChatThreadReader
{
    Task<bool> UserCanAccessThreadAsync(string userId, string threadId, CancellationToken cancellationToken = default);
}
