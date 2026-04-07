using System.Text.Json;

namespace VibeTrade.Backend.Features.Auth;

/// <summary>Persiste perfil de cuenta en PostgreSQL alineado a <c>database-model.md</c>.</summary>
public interface IUserAccountSyncService
{
    Task UpsertFromSessionUserAsync(JsonElement user, CancellationToken cancellationToken = default);
}
