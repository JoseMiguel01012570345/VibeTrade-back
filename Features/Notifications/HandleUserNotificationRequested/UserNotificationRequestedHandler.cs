using MediatR;
using Microsoft.AspNetCore.SignalR;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Notifications;
using VibeTrade.Backend.Features.Shared.Contracts.Events;

namespace VibeTrade.Backend.Features.Notifications.HandleUserNotificationRequested;

public sealed class UserNotificationRequestedHandler(AppDbContext db, IHubContext<ChatHub> hub)
    : INotificationHandler<UserNotificationRequestedEvent>
{
    public async Task Handle(UserNotificationRequestedEvent notification, CancellationToken cancellationToken)
    {
        var userId = (notification.UserId ?? "").Trim();
        if (userId.Length < 2)
            return;

        var title = (notification.Title ?? "").Trim();
        var body = NotificationUtils.TruncatePreview(notification.Body ?? "");
        if (body.Length == 0 && title.Length == 0)
            return;

        var tid = (notification.ThreadId ?? "").Trim();
        var nid = "cn_" + Guid.NewGuid().ToString("N")[..16];
        db.ChatNotifications.Add(new ChatNotificationRow
        {
            Id = nid,
            RecipientUserId = userId,
            ThreadId = tid.Length >= 4 ? tid : null,
            MessageId = null,
            MessagePreview = body.Length > 0 ? body : title,
            AuthorStoreName = title.Length > 0 ? title : "VibeTrade",
            AuthorTrustScore = 0,
            SenderUserId = userId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ReadAtUtc = null,
            Kind = "user_notification",
            MetaJson = string.IsNullOrWhiteSpace(notification.DeepLink)
                ? null
                : $"{{\"deepLink\":{System.Text.Json.JsonSerializer.Serialize(notification.DeepLink)}}}",
        });
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(ChatHubGroupNames.ForUser(userId)).SendAsync(
            "notificationCreated",
            new
            {
                kind = "user_notification",
                threadId = tid.Length >= 4 ? tid : null,
                deepLink = notification.DeepLink,
            },
            cancellationToken);
    }
}
