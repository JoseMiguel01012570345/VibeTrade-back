using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.EmergentOffers;

public sealed class EmergentOfferCarrierSubscriptionService(AppDbContext db) : IEmergentOfferCarrierSubscriptionService
{
    public const string ReasonBuyerWithAcceptedAgreement = "buyer_with_accepted_agreement";

    public const string MessageBuyerWithAcceptedAgreementEs =
        "No podés suscribirte como transportista: en esta operación sos el comprador con un acuerdo aceptado.";

    public async Task<EmergentCarrierSubscriptionStatus> GetStatusAsync(
        string? viewerUserId,
        string emergentOfferId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(viewerUserId))
            return new EmergentCarrierSubscriptionStatus(true, null, null);

        var eid = emergentOfferId.Trim();
        if (eid.Length < 4 || !RecommendationBatchOfferLoader.IsEmergentPublicationId(eid))
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

        if (!string.Equals(thread.BuyerUserId, viewerUserId, StringComparison.Ordinal))
            return new EmergentCarrierSubscriptionStatus(true, null, null);

        // `Status` se persiste en minúsculas (`TradeAgreementService`); evitar `string.Equals(..., StringComparison)` aquí: EF Core no lo traduce a SQL.
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
            ReasonBuyerWithAcceptedAgreement,
            MessageBuyerWithAcceptedAgreementEs);
    }
}
