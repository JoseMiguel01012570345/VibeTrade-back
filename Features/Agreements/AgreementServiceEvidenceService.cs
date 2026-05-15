using Microsoft.EntityFrameworkCore;
using Stripe;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Agreements.Interfaces;
using VibeTrade.Backend.Features.Payments;

namespace VibeTrade.Backend.Features.Agreements;

public sealed class AgreementServiceEvidenceService(
    IChatService chat,
    IChatThreadSystemMessageService threadSystemMessages,
    AppDbContext db) : IAgreementServiceEvidenceService
{
    public async Task<(int StatusCode, IReadOnlyList<AgreementServicePaymentWithEvidenceDto>? Data)> ListAsync(
        string userId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var tid = threadId.Trim();
        var aid = agreementId.Trim();
        var uid = userId.Trim();

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null) return (StatusCodes.Status404NotFound, null);
        if (!await chat.UserCanAccessThreadRowAsync(uid, t, cancellationToken).ConfigureAwait(false))
            return (StatusCodes.Status404NotFound, null);

        var list = await db.AgreementServicePayments.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.TradeAgreementId == aid)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                Pay = x,
                Evidence = db.ServiceEvidences.AsNoTracking()
                    .FirstOrDefault(e => e.AgreementServicePaymentId == x.Id),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dtos = list.Select(x =>
        {
            ServiceEvidenceDto? ev = null;
            if (x.Evidence is not null)
            {
                ev = new ServiceEvidenceDto(
                    x.Evidence.Id,
                    x.Evidence.SellerUserId,
                    x.Evidence.Text,
                    x.Evidence.Attachments,
                    x.Evidence.LastSubmittedText,
                    x.Evidence.LastSubmittedAttachments,
                    x.Evidence.LastSubmittedAtUtc,
                    x.Evidence.Status,
                    x.Evidence.CreatedAtUtc,
                    x.Evidence.UpdatedAtUtc,
                    x.Evidence.BuyerDecisionAtUtc);
            }

            return new AgreementServicePaymentWithEvidenceDto(
                x.Pay.Id,
                x.Pay.ServiceItemId,
                x.Pay.EntryMonth,
                x.Pay.EntryDay,
                x.Pay.Currency,
                x.Pay.AmountMinor,
                x.Pay.Status,
                x.Pay.CreatedAtUtc,
                x.Pay.ReleasedAtUtc,
                ev,
                x.Pay.SellerPayoutRecordedAtUtc,
                x.Pay.SellerPayoutCardBrandSnapshot,
                x.Pay.SellerPayoutCardLast4Snapshot,
                x.Pay.SellerPayoutStripeTransferId);
        }).ToList();

        return (StatusCodes.Status200OK, dtos);
    }

    public async Task<(int StatusCode, string? Error, ServiceEvidenceDto? Data)> UpsertAsync(
        string userId,
        string threadId,
        string agreementId,
        string paymentId,
        UpsertServiceEvidenceRequest body,
        CancellationToken cancellationToken)
    {
        var tid = threadId.Trim();
        var aid = agreementId.Trim();
        var pid = paymentId.Trim();
        var uid = userId.Trim();

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null) return (StatusCodes.Status404NotFound, null, null);
        if (!await chat.UserCanAccessThreadRowAsync(uid, t, cancellationToken).ConfigureAwait(false))
            return (StatusCodes.Status404NotFound, null, null);
        if (!string.Equals(t.SellerUserId, uid, StringComparison.Ordinal))
            return (StatusCodes.Status404NotFound, null, null);

        var pay = await db.AgreementServicePayments
            .FirstOrDefaultAsync(x => x.Id == pid && x.ThreadId == tid && x.TradeAgreementId == aid, cancellationToken)
            .ConfigureAwait(false);
        if (pay is null) return (StatusCodes.Status404NotFound, null, null);
        if (string.Equals(pay.Status, AgreementServicePaymentStatuses.Released, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status400BadRequest, "Pago ya liberado: no se puede editar evidencia.", null);

        var now = DateTimeOffset.UtcNow;
        var ev = await db.ServiceEvidences
            .FirstOrDefaultAsync(x => x.AgreementServicePaymentId == pid, cancellationToken)
            .ConfigureAwait(false);
        if (ev is not null &&
            string.Equals(ev.Status, ServiceEvidenceStatuses.Accepted, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status400BadRequest, "Evidencia ya aceptada: no se puede editar.", null);

        var nextStatus = body.Submit ? ServiceEvidenceStatuses.Submitted : ServiceEvidenceStatuses.Draft;
        var norm = NormalizeEvidence(body.Text, body.Attachments);

        if (ev is null)
        {
            ev = new ServiceEvidenceRow
            {
                Id = $"sevd_{Guid.NewGuid():n}",
                AgreementServicePaymentId = pid,
                SellerUserId = uid,
                Text = norm.Text,
                Attachments = norm.Atts,
                Status = nextStatus,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            if (body.Submit)
            {
                ev.LastSubmittedText = norm.Text;
                ev.LastSubmittedAttachments = norm.Atts.ToList();
                ev.LastSubmittedAtUtc = now;
            }
            db.ServiceEvidences.Add(ev);
        }
        else
        {
            if (body.Submit)
            {
                var lastNorm = NormalizeEvidence(ev.LastSubmittedText, ev.LastSubmittedAttachments);
                if (EvidenceEquals(lastNorm, norm))
                    return (StatusCodes.Status400BadRequest, "No hay cambios desde la última evidencia enviada.", null);
                ev.LastSubmittedText = norm.Text;
                ev.LastSubmittedAttachments = norm.Atts.ToList();
                ev.LastSubmittedAtUtc = now;
            }
            ev.Text = norm.Text;
            ev.Attachments = norm.Atts;
            ev.Status = nextStatus;
            ev.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var payKey = $"mes {pay.EntryMonth} día {pay.EntryDay}";
        var notice = body.Submit
            ? $"Evidencia enviada por el vendedor para servicio ({payKey})."
            : $"Evidencia guardada por el vendedor para servicio ({payKey}).";
        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(tid, notice, cancellationToken).ConfigureAwait(false);

        return (StatusCodes.Status200OK, null, new ServiceEvidenceDto(
            ev.Id,
            ev.SellerUserId,
            ev.Text,
            ev.Attachments,
            ev.LastSubmittedText,
            ev.LastSubmittedAttachments,
            ev.LastSubmittedAtUtc,
            ev.Status,
            ev.CreatedAtUtc,
            ev.UpdatedAtUtc,
            ev.BuyerDecisionAtUtc));
    }

    public async Task<(int StatusCode, string? Error)> DecideAsync(
        string userId,
        string threadId,
        string agreementId,
        string paymentId,
        DecideServiceEvidenceRequest body,
        CancellationToken cancellationToken)
    {
        var tid = threadId.Trim();
        var aid = agreementId.Trim();
        var pid = paymentId.Trim();
        var uid = userId.Trim();

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null) return (StatusCodes.Status404NotFound, null);
        if (!await chat.UserCanAccessThreadRowAsync(uid, t, cancellationToken).ConfigureAwait(false))
            return (StatusCodes.Status404NotFound, null);
        if (!string.Equals(t.BuyerUserId, uid, StringComparison.Ordinal))
            return (StatusCodes.Status404NotFound, null);

        var pay = await db.AgreementServicePayments
            .FirstOrDefaultAsync(x => x.Id == pid && x.ThreadId == tid && x.TradeAgreementId == aid, cancellationToken)
            .ConfigureAwait(false);
        if (pay is null) return (StatusCodes.Status404NotFound, null);

        var ev = await db.ServiceEvidences
            .FirstOrDefaultAsync(x => x.AgreementServicePaymentId == pid, cancellationToken)
            .ConfigureAwait(false);
        if (ev is null) return (StatusCodes.Status400BadRequest, "No hay evidencia para decidir.");
        if (!string.Equals(ev.Status, ServiceEvidenceStatuses.Submitted, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ev.Status, ServiceEvidenceStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status400BadRequest, "La evidencia no está en estado decidible.");

        var d = (body.Decision ?? "").Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(d, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            ev.Status = ServiceEvidenceStatuses.Accepted;
            ev.BuyerDecisionAtUtc = now;
            ev.UpdatedAtUtc = now;
            pay.Status = AgreementServicePaymentStatuses.Released;
            pay.ReleasedAtUtc = now;
        }
        else if (string.Equals(d, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            ev.Status = ServiceEvidenceStatuses.Rejected;
            ev.BuyerDecisionAtUtc = now;
            ev.UpdatedAtUtc = now;
        }
        else
        {
            return (StatusCodes.Status400BadRequest, "Decision inválida (accepted|rejected).");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var payKey = $"mes {pay.EntryMonth} día {pay.EntryDay}";
        var notice = string.Equals(d, "accepted", StringComparison.OrdinalIgnoreCase)
            ? $"El comprador aceptó la evidencia del servicio ({payKey}). Pago liberado (demo)."
            : $"El comprador rechazó la evidencia del servicio ({payKey}).";
        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(tid, notice, cancellationToken).ConfigureAwait(false);

        return (StatusCodes.Status200OK, null);
    }

    public async Task<(int StatusCode, string? Error)> RecordSellerPayoutAsync(
        string userId,
        string threadId,
        string agreementId,
        string paymentId,
        RecordSellerServicePayoutRequest body,
        CancellationToken cancellationToken)
    {
        var tid = threadId.Trim();
        var aid = agreementId.Trim();
        var pid = paymentId.Trim();
        var uid = userId.Trim();
        var pmId = (body.PaymentMethodId ?? "").Trim();

        if (pmId.Length == 0)
            return (StatusCodes.Status400BadRequest, "Selecciona una tarjeta para el depósito.");

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null) return (StatusCodes.Status404NotFound, null);
        if (!await chat.UserCanAccessThreadRowAsync(uid, t, cancellationToken).ConfigureAwait(false))
            return (StatusCodes.Status404NotFound, null);
        if (!string.Equals(t.SellerUserId, uid, StringComparison.Ordinal))
            return (StatusCodes.Status404NotFound, null);

        var pay = await db.AgreementServicePayments
            .FirstOrDefaultAsync(x => x.Id == pid && x.ThreadId == tid && x.TradeAgreementId == aid, cancellationToken)
            .ConfigureAwait(false);
        if (pay is null) return (StatusCodes.Status404NotFound, null);
        if (!string.Equals(pay.Status, AgreementServicePaymentStatuses.Released, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status400BadRequest, "El pago debe estar liberado para registrar el depósito.");
        if (pay.SellerPayoutRecordedAtUtc is not null)
            return (StatusCodes.Status400BadRequest, "Ya registraste el depósito de este pago.");
        if (pay.AmountMinor <= 0)
            return (StatusCodes.Status400BadRequest, "El importe del pago no es válido para liquidar.");

        var u = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == uid, cancellationToken).ConfigureAwait(false);
        var customerId = (u?.StripeCustomerId ?? "").Trim();
        if (customerId.Length == 0)
            return (StatusCodes.Status400BadRequest, "Configura tarjetas de pago en tu perfil antes de recibir el depósito.");

        var skipStripePayout = PaymentStripeEnv.SkipStripePaymentIntentCreate();
        var now = DateTimeOffset.UtcNow;

        // Demo / dev: VIBETRADE_SKIP_PAYMENT_INTENTS — sin Transfer Stripe (mismo criterio que cobros con PaymentIntent).
        if (skipStripePayout)
        {
            var tailSkip = pid.Length >= 12 ? pid.Substring(pid.Length - 12) : pid;
            pay.SellerPayoutPaymentMethodStripeId = pmId;
            pay.SellerPayoutRecordedAtUtc = now;
            pay.SellerPayoutCardBrandSnapshot = null;
            pay.SellerPayoutCardLast4Snapshot = null;
            pay.SellerPayoutStripeTransferId = $"skipped_{tailSkip}";
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var payKeySkip = $"mes {pay.EntryMonth} día {pay.EntryDay}";
            await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(
                tid,
                $"El vendedor registró liquidación (demo / sin llamada Stripe) del pago de servicio ({payKeySkip}).",
                cancellationToken).ConfigureAwait(false);

            return (StatusCodes.Status200OK, null);
        }

        var destination = (u?.StripeConnectedAccountId ?? "").Trim();
        if (!destination.StartsWith("acct_", StringComparison.Ordinal))
            return (StatusCodes.Status400BadRequest,
                "Tu cuenta debe tener una cuenta Stripe Connect (acct_) asociada como destino del giro.");

        var pmResolve =
            await AgreementCheckoutExecutor.ResolveCustomerPaymentMethodAsync(pmId, customerId, cancellationToken)
                .ConfigureAwait(false);
        if (!pmResolve.Success)
            return (StatusCodes.Status400BadRequest, pmResolve.ErrorMessage);

        var pm = pmResolve.PaymentMethod!;
        var card = pm.Card;
        if (card is null)
            return (StatusCodes.Status400BadRequest, "El método de depósito debe ser una tarjeta.");

        var curLower = pay.Currency.Trim().ToLowerInvariant();
        Transfer transfer;
        try
        {
            var xferSvc = new TransferService();
            transfer = await xferSvc.CreateAsync(
                new TransferCreateOptions
                {
                    Amount = pay.AmountMinor,
                    Currency = curLower,
                    Destination = destination,
                    Description = $"VibeTrade payout servicio {pay.Id}",
                    Metadata = new Dictionary<string, string>
                    {
                        ["agreement_service_payment_id"] = pay.Id,
                        ["seller_user_id"] = uid,
                        ["thread_id"] = tid,
                        ["seller_payment_method_id"] = pm.Id,
                    },
                    TransferGroup = string.IsNullOrWhiteSpace(pay.AgreementCurrencyPaymentId)
                        ? $"agr_{aid}"
                        : pay.AgreementCurrencyPaymentId!,
                },
                new RequestOptions
                {
                    IdempotencyKey = $"seller_svc_payout_{pid}",
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (StripeException sx)
        {
            return (StatusCodes.Status400BadRequest, AgreementCheckoutExecutor.StripeErrorUserMessage(sx));
        }

        if (string.IsNullOrWhiteSpace(transfer.Id))
            return (StatusCodes.Status400BadRequest, "Stripe no devolvió un id de transferencia.");

        pay.SellerPayoutPaymentMethodStripeId = pm.Id;
        pay.SellerPayoutRecordedAtUtc = now;
        pay.SellerPayoutCardBrandSnapshot = (card.Brand ?? "").Trim();
        pay.SellerPayoutCardLast4Snapshot = (card.Last4 ?? "").Trim();
        pay.SellerPayoutStripeTransferId = transfer.Id.Trim();

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var payKey = $"mes {pay.EntryMonth} día {pay.EntryDay}";
        var last4 = pay.SellerPayoutCardLast4Snapshot;
        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(
            tid,
                $"Liquidación Stripe del pago de servicio ({payKey}): transferencia {transfer.Id} (Connect); tarjeta registrada •••• {last4}.",
            cancellationToken).ConfigureAwait(false);

        return (StatusCodes.Status200OK, null);
    }

    private sealed record NormalizedEvidence(string Text, List<ServiceEvidenceAttachmentBody> Atts);

    private static NormalizedEvidence NormalizeEvidence(
        string? text,
        IReadOnlyList<ServiceEvidenceAttachmentBody>? atts)
    {
        var t = (text ?? "").Trim();
        var a = (atts ?? Array.Empty<ServiceEvidenceAttachmentBody>())
            .Select(x => new ServiceEvidenceAttachmentBody(
                (x.Id ?? "").Trim(),
                (x.Url ?? "").Trim(),
                (x.FileName ?? "").Trim(),
                (x.Kind ?? "").Trim()))
            .OrderBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new NormalizedEvidence(t, a);
    }

    private static bool EvidenceEquals(NormalizedEvidence a, NormalizedEvidence b)
    {
        if (!string.Equals(a.Text, b.Text, StringComparison.Ordinal)) return false;
        if (a.Atts.Count != b.Atts.Count) return false;
        for (var i = 0; i < a.Atts.Count; i++)
        {
            var x = a.Atts[i];
            var y = b.Atts[i];
            if (!string.Equals(x.Url, y.Url, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(x.Kind, y.Kind, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
