using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat.Core;

/// <summary>
/// Métodos auxiliares para consultas de base de datos relacionadas con chat.
/// </summary>
public static class ChatQueryHelpers
{
    public sealed record BuyerPublicFields(string? DisplayName, string? AvatarUrl);
    /// <summary>
    /// Obtiene suscripciones de tramos activas (no rechazadas ni retiradas) para un hilo específico.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetActiveCarrierUserIdsForThreadAsync(
        AppDbContext db,
        string threadId,
        CancellationToken cancellationToken) =>
        await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == threadId
                && x.Status != "rejected"
                && x.Status != "withdrawn")
            .Select(x => x.CarrierUserId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Obtiene IDs de transportistas que participaron en un hilo (incluyendo retirados pero no rechazados).
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetParticipatedCarrierUserIdsForThreadAsync(
        AppDbContext db,
        string threadId,
        CancellationToken cancellationToken) =>
        await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == threadId && x.Status != "rejected")
            .Select(x => x.CarrierUserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Obtiene IDs de transportistas activos en otros hilos (no el hilo especificado).
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetActiveCarrierUserIdsElsewhereAsync(
        AppDbContext db,
        string threadId,
        CancellationToken cancellationToken) =>
        await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId != threadId
                && x.Status != "rejected"
                && x.Status != "withdrawn")
            .Select(x => x.CarrierUserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Verifica si un usuario es transportista activo en un hilo específico.
    /// </summary>
    public static async Task<bool> IsUserActiveCarrierOnThreadAsync(
        AppDbContext db,
        string userId,
        string threadId,
        CancellationToken cancellationToken) =>
        await db.RouteTramoSubscriptions.AsNoTracking()
            .AnyAsync(
                x =>
                    x.ThreadId == threadId
                    && x.CarrierUserId == userId
                    && x.Status != "rejected"
                    && x.Status != "withdrawn",
                cancellationToken);

    /// <summary>
    /// Obtiene mensajes de chat no eliminados para una lista de thread IDs.
    /// </summary>
    public static async Task<IReadOnlyList<ChatMessageRow>> GetNonDeletedMessagesForThreadIdsAsync(
        AppDbContext db,
        IReadOnlyList<string> threadIds,
        CancellationToken cancellationToken) =>
        await db.ChatMessages.AsNoTracking()
            .Where(m => threadIds.Contains(m.ThreadId) && m.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Obtiene hilos de chat no eliminados por sus IDs.
    /// </summary>
    public static async Task<IReadOnlyList<ChatThreadRow>> GetNonDeletedThreadsByIdsAsync(
        AppDbContext db,
        IReadOnlyList<string> threadIds,
        CancellationToken cancellationToken) =>
        await db.ChatThreads.AsNoTracking()
            .Where(t => threadIds.Contains(t.Id) && t.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Obtiene miembros de grupo social para un hilo específico.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetSocialGroupMemberUserIdsAsync(
        AppDbContext db,
        string threadId,
        CancellationToken cancellationToken) =>
        await db.ChatSocialGroupMembers.AsNoTracking()
            .Where(x => x.ThreadId == threadId)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Verifica si un usuario es miembro de un grupo social.
    /// </summary>
    public static async Task<bool> IsUserSocialGroupMemberAsync(
        AppDbContext db,
        string userId,
        string threadId,
        CancellationToken cancellationToken) =>
        await db.ChatSocialGroupMembers.AsNoTracking()
            .AnyAsync(m => m.ThreadId == threadId && m.UserId == userId, cancellationToken);

    /// <summary>
    /// Obtiene campos públicos (DisplayName, AvatarUrl) de usuarios por sus IDs.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, BuyerPublicFields>> GetBuyerPublicFieldsByIdsAsync(
        AppDbContext db,
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.UserAccounts.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.AvatarUrl })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            x => x.Id,
            x => new ChatQueryHelpers.BuyerPublicFields(
                string.IsNullOrWhiteSpace(x.DisplayName) ? null : x.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(x.AvatarUrl) ? null : x.AvatarUrl.Trim()));
    }
}
