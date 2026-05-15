using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Agreements.Interfaces;
using VibeTrade.Backend.Features.Payments;

namespace VibeTrade.Backend.Features.Agreements;

public sealed class AgreementMerchandiseEvidenceService(
    IChatService chat,
    IChatThreadSystemMessageService threadSystemMessages,
    AppDbContext db) : IAgreementMerchandiseEvidenceService
{
    public async Task<(int StatusCode, IReadOnlyList<AgreementMerchandiseLinePaymentWithEvidenceDto>? Data)> ListAsync(
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

        var list = await db.AgreementMerchandiseLinePaids.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.TradeAgreementId == aid)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                Pay = x,
                Evidence = db.MerchandiseEvidences.AsNoTracking()
                    .FirstOrDefault(e => e.AgreementMerchandiseLinePaidId == x.Id),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dtos = list.Select(x =>
        {
            MerchandiseEvidenceDto? ev = null;
            if (x.Evidence is not null)
            {
                ev = new MerchandiseEvidenceDto(
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

            return new AgreementMerchandiseLinePaymentWithEvidenceDto(
                x.Pay.Id,
                x.Pay.MerchandiseLineId,
                x.Pay.Currency,
                x.Pay.AmountMinor,
                x.Pay.Status,
                x.Pay.CreatedAtUtc,
                x.Pay.ReleasedAtUtc,
                ev);
        }).ToList();

        return (StatusCodes.Status200OK, dtos);
    }

    public async Task<(int StatusCode, string? Error, MerchandiseEvidenceDto? Data)> UpsertAsync(
        string userId,
        string threadId,
        string agreementId,
        string paymentId,
        UpsertMerchandiseEvidenceRequest body,
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

        var pay = await db.AgreementMerchandiseLinePaids
            .FirstOrDefaultAsync(x => x.Id == pid && x.ThreadId == tid && x.TradeAgreementId == aid, cancellationToken)
            .ConfigureAwait(false);
        if (pay is null) return (StatusCodes.Status404NotFound, null, null);
        if (string.Equals(pay.Status, AgreementMerchandiseLinePaidStatuses.Released, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status400BadRequest, "Pago ya liberado: no se puede editar evidencia.", null);

        var now = DateTimeOffset.UtcNow;
        var ev = await db.MerchandiseEvidences
            .FirstOrDefaultAsync(x => x.AgreementMerchandiseLinePaidId == pid, cancellationToken)
            .ConfigureAwait(false);
        if (ev is not null &&
            string.Equals(ev.Status, MerchandiseEvidenceStatuses.Accepted, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status400BadRequest, "Evidencia ya aceptada: no se puede editar.", null);

        var nextStatus = body.Submit ? MerchandiseEvidenceStatuses.Submitted : MerchandiseEvidenceStatuses.Draft;
        var norm = AgreementUtils.NormalizeEvidence(body.Text, body.Attachments);

        if (ev is null)
        {
            ev = new MerchandiseEvidenceRow
            {
                Id = $"mevd_{Guid.NewGuid():n}",
                AgreementMerchandiseLinePaidId = pid,
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
            db.MerchandiseEvidences.Add(ev);
        }
        else
        {
            if (body.Submit)
            {
                var lastNorm = AgreementUtils.NormalizeEvidence(ev.LastSubmittedText, ev.LastSubmittedAttachments);
                if (AgreementUtils.EvidenceEquals(lastNorm, norm))
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

        var notice = body.Submit
            ? "Evidencia enviada por el vendedor para línea de mercadería."
            : "Evidencia guardada por el vendedor para línea de mercadería.";
        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(tid, notice, cancellationToken).ConfigureAwait(false);

        return (StatusCodes.Status200OK, null, new MerchandiseEvidenceDto(
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
        DecideMerchandiseEvidenceRequest body,
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

        var pay = await db.AgreementMerchandiseLinePaids
            .FirstOrDefaultAsync(x => x.Id == pid && x.ThreadId == tid && x.TradeAgreementId == aid, cancellationToken)
            .ConfigureAwait(false);
        if (pay is null) return (StatusCodes.Status404NotFound, null);

        var ev = await db.MerchandiseEvidences
            .FirstOrDefaultAsync(x => x.AgreementMerchandiseLinePaidId == pid, cancellationToken)
            .ConfigureAwait(false);
        if (ev is null) return (StatusCodes.Status400BadRequest, "No hay evidencia para decidir.");
        if (!string.Equals(ev.Status, MerchandiseEvidenceStatuses.Submitted, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ev.Status, MerchandiseEvidenceStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status400BadRequest, "La evidencia no está en estado decidible.");

        var d = (body.Decision ?? "").Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(d, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            ev.Status = MerchandiseEvidenceStatuses.Accepted;
            ev.BuyerDecisionAtUtc = now;
            ev.UpdatedAtUtc = now;
            pay.Status = AgreementMerchandiseLinePaidStatuses.Released;
            pay.ReleasedAtUtc = now;
        }
        else if (string.Equals(d, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            ev.Status = MerchandiseEvidenceStatuses.Rejected;
            ev.BuyerDecisionAtUtc = now;
            ev.UpdatedAtUtc = now;
        }
        else
        {
            return (StatusCodes.Status400BadRequest, "Decision inválida (accepted|rejected).");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var notice = string.Equals(d, "accepted", StringComparison.OrdinalIgnoreCase)
            ? "El comprador aceptó la evidencia de mercadería. Pago liberado (demo)."
            : "El comprador rechazó la evidencia de mercadería.";
        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(tid, notice, cancellationToken).ConfigureAwait(false);

        return (StatusCodes.Status200OK, null);
    }
}
