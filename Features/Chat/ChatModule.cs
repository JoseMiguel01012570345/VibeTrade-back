using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat.Infrastructure;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Notifications;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Chat;
public static partial class ChatModule
{
    public static IServiceCollection AddChatFeature(this IServiceCollection services)
    {
        services.AddScoped<IThreadAccessControlService, ThreadAccessControlService>();
        services.AddScoped<ChatServiceCore>();
        services.AddScoped<ChatService>();
        services.AddScoped<IChatMessageInserter>(sp => sp.GetRequiredService<ChatService>());
        services.AddScoped<IChatThreadSystemMessageService>(sp =>
        {
            var lazyInserter = new Lazy<IChatMessageInserter>(() => sp.GetRequiredService<ChatService>());
            return new ChatThreadSystemMessageService(
                sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<IThreadAccessControlService>(),
                lazyInserter);
        });
        services.AddScoped<IChatService>(sp => sp.GetRequiredService<ChatService>());
        services.AddScoped<IThreadManagementService>(sp => sp.GetRequiredService<ChatService>());
        services.AddScoped<IMessageHandlingService>(sp => sp.GetRequiredService<ChatService>());
        services.AddScoped<IParticipantManagementService>(sp => sp.GetRequiredService<ChatService>());
        services.AddScoped<IOfferRelationService>(sp => sp.GetRequiredService<ChatService>());
        return services;
    }
}
