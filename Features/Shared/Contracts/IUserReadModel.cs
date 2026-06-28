namespace VibeTrade.Backend.Features.Shared.Contracts;

public sealed record UserReadModelSummary(string Id, string? DisplayName, string? PhoneE164);

/// <summary>Read-only user lookups for cross-feature validation.</summary>
public interface IUserReadModel
{
    Task<UserReadModelSummary?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);
}
