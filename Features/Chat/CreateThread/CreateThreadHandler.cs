using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.CreateThread;

public sealed class CreateThreadHandler(ChatServiceCore core) : IRequestHandler<CreateThreadCommand, ChatThreadDto?>
{
    public Task<ChatThreadDto?> Handle(CreateThreadCommand request, CancellationToken cancellationToken) =>
        core.CreateOrGetThreadForBuyerAsync(
            request.BuyerUserId,
            request.OfferId,
            request.PurchaseIntent,
            request.ForceNewThread,
            cancellationToken);
}
