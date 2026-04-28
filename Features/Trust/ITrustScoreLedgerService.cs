namespace VibeTrade.Backend.Features.Trust;

public interface ITrustScoreLedgerService
{
    /// <summary>Encola un movimiento; el llamador hace <c>SaveChanges</c> en el mismo contexto inyectado.</summary>
    void StageEntry(
        string subjectType,
        string subjectId,
        int delta,
        int balanceAfter,
        string reason);

    Task<IReadOnlyList<TrustHistoryItemDto>> ListForSubjectAsync(
        string subjectType,
        string subjectId,
        int limit,
        CancellationToken cancellationToken = default);
}
