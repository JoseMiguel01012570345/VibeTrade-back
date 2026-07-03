using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Trust.Dtos;
using VibeTrade.Backend.Features.Trust.Interfaces;

namespace VibeTrade.Backend.Features.Trust;

/// <summary>
/// Implementa el gate de confianza y la mensualidad (wiki cap. 08/10). El pago se simula: al
/// registrarlo se restaura el puntaje al umbral y se rehabilitan las interacciones.
/// </summary>
public sealed class MensualidadService(
    AppDbContext db,
    ITrustScoreLedgerService trustLedger) : IMensualidadService
{
    public async Task<TrustStatusDto?> GetStatusAsync(string userId, CancellationToken cancellationToken = default)
    {
        var uid = (userId ?? "").Trim();
        if (uid.Length < 2)
            return null;

        var acc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == uid, cancellationToken)
            .ConfigureAwait(false);
        if (acc is null)
            return null;

        return BuildStatus(acc.TrustScore);
    }

    public async Task<MensualidadPayResponse?> PayAsync(
        string userId,
        MensualidadPayRequest request,
        CancellationToken cancellationToken = default)
    {
        var uid = (userId ?? "").Trim();
        if (uid.Length < 2)
            return null;

        var acc = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == uid, cancellationToken)
            .ConfigureAwait(false);
        if (acc is null)
            return null;

        var before = acc.TrustScore;
        var wasBlocked = TrustThresholds.IsBlocked(before);
        var now = DateTimeOffset.UtcNow;

        TrustHistoryItemDto? entry = null;
        if (wasBlocked)
        {
            var appliedDelta = TrustThresholds.InteractionThreshold - before;
            acc.TrustScore = TrustThresholds.InteractionThreshold;
            trustLedger.StageEntry(
                TrustLedgerSubjects.User,
                uid,
                appliedDelta,
                acc.TrustScore,
                "Pago de mensualidad: acceso rehabilitado");
        }

        db.MensualidadPayments.Add(new MensualidadPaymentRow
        {
            Id = "mensu_" + Guid.NewGuid().ToString("N"),
            UserId = uid,
            PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? null : request.PaymentMethod!.Trim(),
            PaymentReference = string.IsNullOrWhiteSpace(request.PaymentReference) ? null : request.PaymentReference!.Trim(),
            TrustScoreBefore = before,
            TrustScoreAfter = acc.TrustScore,
            PaidAtUtc = now,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (wasBlocked)
        {
            var entryRow = await db.TrustScoreLedgerRows.AsNoTracking()
                .Where(x => x.SubjectType == TrustLedgerSubjects.User && x.SubjectId == uid)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (entryRow is not null)
                entry = new TrustHistoryItemDto(
                    entryRow.Id,
                    entryRow.CreatedAtUtc,
                    entryRow.Delta,
                    entryRow.BalanceAfter,
                    entryRow.Reason);
        }

        return new MensualidadPayResponse(
            Success: true,
            Status: BuildStatus(acc.TrustScore),
            CrossedThresholdUp: wasBlocked,
            Entry: entry);
    }

    private static TrustStatusDto BuildStatus(int score)
    {
        var blocked = TrustThresholds.IsBlocked(score);
        return new TrustStatusDto(
            TrustScore: score,
            Threshold: TrustThresholds.InteractionThreshold,
            State: TrustThresholds.StateFor(score),
            InteractionsEnabled: !blocked,
            MensualidadRequired: blocked);
    }
}
