using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.EmergentOffers.Interfaces;

namespace VibeTrade.Backend.Features.EmergentOffers.EmergentOffersMediator.GetCarrierSubscription;

public sealed class GetCarrierSubscriptionHandler(AppDbContext db)
    : IRequestHandler<GetCarrierSubscriptionQuery, EmergentCarrierSubscriptionStatus>
{
    public async Task<EmergentCarrierSubscriptionStatus> Handle(
        GetCarrierSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ViewerUserId))
            return new EmergentCarrierSubscriptionStatus(true, null, null);

        if (!EmergentOfferUtils.TryNormalizeEmergentOfferId(request.EmergentOfferId, out var eid))
            return new EmergentCarrierSubscriptionStatus(true, null, null);

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eid && x.RetractedAtUtc == null, cancellationToken);
        if (em is null)
            return new EmergentCarrierSubscriptionStatus(true, null, null);

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == em.ThreadId && x.DeletedAtUtc == null,
                cancellationToken);
        if (thread is null)
            return new EmergentCarrierSubscriptionStatus(true, null, null);

        if (!string.Equals(thread.BuyerUserId, request.ViewerUserId, StringComparison.Ordinal))
            return new EmergentCarrierSubscriptionStatus(true, null, null);

        var hasAccepted = await db.TradeAgreements.AsNoTracking()
            .AnyAsync(
                x => x.ThreadId == thread.Id
                    && x.DeletedAtUtc == null
                    && x.Status == "accepted",
                cancellationToken);

        if (!hasAccepted)
            return new EmergentCarrierSubscriptionStatus(true, null, null);

        return new EmergentCarrierSubscriptionStatus(
            false,
            EmergentOfferCarrierSubscriptionService.ReasonBuyerWithAcceptedAgreement,
            EmergentOfferCarrierSubscriptionService.MessageBuyerWithAcceptedAgreementEs);
    }
}
