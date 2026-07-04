using MediatR;
using VibeTrade.Backend.Features.Chat.ChatMediator.AddParticipant;
using VibeTrade.Backend.Features.Chat.ChatMediator.CreateThread;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Chat.ChatMediator.ListMessages;
using VibeTrade.Backend.Features.Chat.ChatMediator.SendMessage;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Chat;

public sealed class ChatService(IMediator mediator, ChatServiceCore core)
    : IChatService,
        IThreadManagementService,
        IMessageHandlingService,
        IParticipantManagementService,
        IOfferRelationService,
        IChatMessageInserter
{
    public Task<bool> UserCanAccessThreadRowAsync(
        string userId,
        ChatThreadRow thread,
        CancellationToken cancellationToken = default) =>
        core.UserCanAccessThreadRowAsync(userId, thread, cancellationToken);

    public Task<bool> IsUserSellerForOfferAsync(
        string userId,
        string offerId,
        CancellationToken cancellationToken = default) =>
        core.IsUserSellerForOfferAsync(userId, offerId, cancellationToken);

    public Task<string?> GetSellerUserIdForOfferAsync(
        string offerId,
        CancellationToken cancellationToken = default) =>
        core.GetSellerUserIdForOfferAsync(offerId, cancellationToken);

    public Task<ChatThreadDto?> CreateOrGetThreadForBuyerAsync(
        string buyerUserId,
        string offerId,
        bool purchaseIntent = true,
        bool forceNewThread = false,
        CancellationToken cancellationToken = default) =>
        mediator.Send(
            new CreateThreadCommand(buyerUserId, offerId, purchaseIntent, forceNewThread),
            cancellationToken);

    public Task<ChatThreadDto?> CreateSocialGroupThreadAsync(
        string creatorUserId,
        IReadOnlyList<string> otherUserIds,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new AddParticipantCommand(creatorUserId, otherUserIds), cancellationToken);

    public Task<ChatThreadDto?> GetThreadIfVisibleAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default) =>
        core.GetThreadIfVisibleAsync(userId, threadId, cancellationToken);

    public Task<ChatThreadDto?> GetThreadByOfferIfVisibleAsync(
        string userId,
        string offerId,
        CancellationToken cancellationToken = default) =>
        core.GetThreadByOfferIfVisibleAsync(userId, offerId, cancellationToken);

    public Task<IReadOnlyList<ChatThreadSummaryDto>> ListThreadsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        core.ListThreadsForUserAsync(userId, cancellationToken);

    public Task<IReadOnlyList<ChatThreadMemberDto>?> ListSocialThreadMembersAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default) =>
        core.ListSocialThreadMembersAsync(userId, threadId, cancellationToken);

    public Task<ChatThreadDto?> PatchSocialGroupTitleAsync(
        string userId,
        string threadId,
        string? title,
        CancellationToken cancellationToken = default) =>
        core.PatchSocialGroupTitleAsync(userId, threadId, title, cancellationToken);

    public Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new ListMessagesQuery(userId, threadId), cancellationToken);

    public Task<ChatMessageDto?> PostMessageAsync(
        PostChatMessageArgs request,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new SendMessageCommand(request), cancellationToken);

    public Task<ChatMessageDto?> UpdateMessageStatusAsync(
        UpdateChatMessageStatusArgs request,
        CancellationToken cancellationToken = default) =>
        core.UpdateMessageStatusAsync(request, cancellationToken);

    public Task<int> AckAllPendingIncomingDeliveredAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        core.AckAllPendingIncomingDeliveredAsync(userId, cancellationToken);

    public Task<bool> DeleteThreadAsync(string userId, string threadId, CancellationToken cancellationToken = default) =>
        core.DeleteThreadAsync(userId, threadId, cancellationToken);

    public Task<ChatMessageDto> InsertChatMessageAsync(
        ChatThreadRow thread,
        string senderUserId,
        ChatMessagePayload payloadObj,
        CancellationToken cancellationToken) =>
        core.InsertChatMessageAsync(thread, senderUserId, payloadObj, cancellationToken);
}
