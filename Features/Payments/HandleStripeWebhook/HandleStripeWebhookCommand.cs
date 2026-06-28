using MediatR;

namespace VibeTrade.Backend.Features.Payments.HandleStripeWebhook;

public sealed record HandleStripeWebhookCommand(string Json, string StripeSignature)
    : IRequest<HandleStripeWebhookResult>;
