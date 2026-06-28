using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.CreateThread;

public sealed record CreateThreadCommand(
    string BuyerUserId,
    string OfferId,
    bool PurchaseIntent = true,
    bool ForceNewThread = false) : IRequest<ChatThreadDto?>;
