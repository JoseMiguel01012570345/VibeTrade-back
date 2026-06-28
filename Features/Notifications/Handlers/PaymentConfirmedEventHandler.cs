using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.Shared.Contracts.Events;

namespace VibeTrade.Backend.Features.Notifications.Handlers;

public sealed class PaymentConfirmedEventHandler(AppDbContext db, INotificationService notifications)
    : INotificationHandler<PaymentConfirmedEvent>
{
    public async Task Handle(PaymentConfirmedEvent notification, CancellationToken cancellationToken)
    {
        var tid = (notification.ThreadId ?? "").Trim();
        var aid = (notification.AgreementId ?? "").Trim();
        var rsid = (notification.RouteSheetId ?? "").Trim();
        if (tid.Length < 4 || aid.Length < 8 || rsid.Length < 1)
            return;

        var paidStopIds = notification.PaidRouteStopIds?
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (paidStopIds is not { Count: > 0 })
            return;

        var row = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        var payload = row?.Payload;
        if (payload?.Paradas is not { Count: > 0 })
            return;

        await RouteLegHandoffNotifications.NotifyPaidStopsAsync(
                db,
                notifications,
                tid,
                aid,
                rsid,
                payload,
                paidStopIds,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
