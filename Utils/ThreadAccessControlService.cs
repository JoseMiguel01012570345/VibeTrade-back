using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Utils;

/// <summary>Evalúa acceso a filas de hilo de chat (comprador, vendedor, transportista, grupo social).</summary>
public sealed class ThreadAccessControlService(AppDbContext db) : IThreadAccessControlService
{
    /// <inheritdoc />
    public async Task<bool> UserCanAccessThreadRowAsync(
        string userId,
        ChatThreadRow thread,
        CancellationToken cancellationToken = default)
    {
        if (thread.DeletedAtUtc is not null)
            return false;
        var uid = (userId ?? "").Trim();
        if (uid.Length == 0)
            return false;
        var buyerId = (thread.BuyerUserId ?? "").Trim();
        var sellerId = (thread.SellerUserId ?? "").Trim();
        if (string.Equals(uid, buyerId, StringComparison.Ordinal))
        {
            if (thread.BuyerExpelledAtUtc is not null)
                return false;
            return true;
        }
        if (string.Equals(uid, sellerId, StringComparison.Ordinal))
        {
            if (thread.SellerExpelledAtUtc is not null)
                return false;
            return true;
        }
        if (thread.IsSocialGroup
            && await ChatQueryHelpers.IsUserSocialGroupMemberAsync(db, uid, thread.Id, cancellationToken))
            return true;
        if (ChatThreadAccess.UserCanSeeThread(uid, thread))
            return true;
        if (await ChatQueryHelpers.IsUserActiveCarrierOnThreadAsync(db, uid, thread.Id, cancellationToken))
            return true;

        var carrierIdsThisThread = await ChatQueryHelpers.GetParticipatedCarrierUserIdsForThreadAsync(db, thread.Id, cancellationToken);
        if (carrierIdsThisThread.Any(cid =>
                !string.IsNullOrWhiteSpace(cid)
                && (string.Equals(cid.Trim(), uid, StringComparison.Ordinal)
                    || ChatThreadAccess.UserIdsMatchLoose(uid, cid))))
        {
            if (await db.RouteTramoSubscriptions.AsNoTracking()
                    .AnyAsync(
                        x => x.CarrierUserId == uid
                            && x.ThreadId != thread.Id
                            && x.Status != "rejected"
                            && x.Status != "withdrawn",
                        cancellationToken))
                return true;
            var otherCarrierIds = await ChatQueryHelpers.GetActiveCarrierUserIdsElsewhereAsync(db, thread.Id, cancellationToken);
            if (otherCarrierIds.Any(oid =>
                    !string.IsNullOrWhiteSpace(oid)
                    && (string.Equals(oid.Trim(), uid, StringComparison.Ordinal)
                        || ChatThreadAccess.UserIdsMatchLoose(uid, oid))))
                return true;
        }

        var carrierIds = await ChatQueryHelpers.GetActiveCarrierUserIdsForThreadAsync(db, thread.Id, cancellationToken);
        return carrierIds.Any(cid => ChatThreadAccess.UserIdsMatchLoose(uid, cid));
    }
}
