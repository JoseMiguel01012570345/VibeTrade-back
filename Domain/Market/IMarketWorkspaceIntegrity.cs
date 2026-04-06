namespace VibeTrade.Backend.Domain.Market;

public interface IMarketWorkspaceIntegrity
{
    /// <summary>Valida forma mínima del workspace antes de persistir.</summary>
    void ValidateOrThrow(System.Text.Json.JsonDocument document);
}
