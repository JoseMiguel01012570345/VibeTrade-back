using MediatR;

namespace VibeTrade.Backend.Features.Payments.HandleStripeWebhook;

public sealed class HandleStripeWebhookHandler(PaymentsServiceCore core)
    : IRequestHandler<HandleStripeWebhookCommand, HandleStripeWebhookResult>
{
    public Task<HandleStripeWebhookResult> Handle(
        HandleStripeWebhookCommand request,
        CancellationToken cancellationToken) =>
        core.HandleStripeWebhookAsync(request.Json, request.StripeSignature, cancellationToken);
}
